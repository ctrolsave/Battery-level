// Battery Level: muestra en la barra de tareas el % de batería de los dispositivos
// Razer (vía Synapse o HID) y de todos los dispositivos Bluetooth conectados.
// Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("Battery Level")]
[assembly: AssemblyProduct("Battery Level")]
[assembly: AssemblyCompany("Ctrl + Save Design Studio")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace BateriaTray
{
    enum Tipo { Mouse, Bluetooth }

    class Lectura
    {
        public string Clave;    // identifica el ícono
        public string Nombre;
        public int Porcentaje;
        public bool Cargando;
        public Tipo Tipo;
        public string Detalle = ""; // de dónde sale el dato

        public static string ClaveRazer(string nombre)
        {
            return "razer-" + Regex.Replace(nombre.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        }
    }

    // ------------------------------------------------------------------ Joysticks (XInput)
    // Joysticks Xbox y compatibles conectados por el adaptador inalámbrico de Xbox o por USB.
    // Windows solo da 4 niveles (vacío, bajo, medio, lleno), así que el % es aproximado.
    static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_BATTERY_INFORMATION { public byte BatteryType, BatteryLevel; }

        [DllImport("xinput1_4.dll")]
        static extern int XInputGetBatteryInformation(int usuario, byte tipo, out XINPUT_BATTERY_INFORMATION info);

        public static List<Lectura> Leer()
        {
            var lista = new List<Lectura>();
            for (int i = 0; i < 4; i++)
            {
                XINPUT_BATTERY_INFORMATION b;
                if (XInputGetBatteryInformation(i, 0, out b) != 0) continue;
                // 0 desconectado, 1 con cable (sin batería), 0xFF desconocido.
                if (b.BatteryType == 0 || b.BatteryType == 1 || b.BatteryType == 0xFF) continue;
                int[] pct = { 5, 30, 65, 100 };
                string[] nivel = { "casi vacía", "baja", "media", "llena" };
                int n = Math.Min((int)b.BatteryLevel, 3);
                var l = new Lectura();
                l.Clave = "xinput-" + i;
                l.Nombre = "Joystick Xbox " + (i + 1);
                l.Porcentaje = pct[n];
                l.Tipo = Tipo.Bluetooth;
                l.Detalle = "Batería " + nivel[n] + " (aprox.: Windows da solo 4 niveles para joysticks por adaptador o USB)";
                lista.Add(l);
            }
            return lista;
        }
    }

    // ------------------------------------------------------------------ Razer
    static class Razer
    {
        const int GENERIC_READ = unchecked((int)0x80000000), GENERIC_WRITE = 0x40000000;
        const int FILE_SHARE_RW = 3, OPEN_EXISTING = 3;
        const int DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public short Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public short[] Reserved;
            public short a, b, c, d, e, f, g, h, i, j;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, int access, int share, IntPtr sec, int disp, int flags, IntPtr tmpl);

        // PID 00CD = dongle inalámbrico, 00CC = cable (Basilisk V3 Pro 35K).
        static readonly string[] Pids = { "pid_00cd", "pid_00cc" };

        static List<string> Rutas()
        {
            var lista = new List<string>();
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return lista;
            try
            {
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
                {
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                        {
                            string ruta = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                            string r = ruta.ToLowerInvariant();
                            if (r.Contains("vid_1532")) lista.Add(ruta);
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            // Primero el dongle, después el cable.
            lista.Sort((a, b) => Orden(a).CompareTo(Orden(b)));
            return lista;
        }

        static int Orden(string ruta)
        {
            string r = ruta.ToLowerInvariant();
            for (int i = 0; i < Pids.Length; i++) if (r.Contains(Pids[i])) return i;
            return 99;
        }

        static SafeFileHandle Abrir(string ruta)
        {
            var h = CreateFile(ruta, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!h.IsInvalid) return h;
            h.Dispose();
            // Los mouse no se dejan abrir para lectura; los reportes "feature" andan igual con acceso 0.
            return CreateFile(ruta, 0, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        static int LargoFeature(SafeFileHandle h)
        {
            IntPtr pre;
            if (!HidD_GetPreparsedData(h, out pre)) return 0;
            try
            {
                var caps = new HIDP_CAPS();
                HidP_GetCaps(pre, ref caps);
                return caps.FeatureReportByteLength;
            }
            finally { HidD_FreePreparsedData(pre); }
        }

        // Protocolo Razer: reporte de 90 bytes (+1 del id). Devuelve el byte arg[1] o -1.
        static int Pedir(SafeFileHandle h, byte tid, byte clase, byte comando)
        {
            // Razer Synapse usa el mismo canal, así que a veces la respuesta que llega es
            // la de un pedido suyo. En ese caso se vuelve a leer y, si hace falta, a pedir.
            for (int intento = 0; intento < 8; intento++)
            {
                byte[] r = new byte[91];
                r[2] = tid; r[6] = 0x02; r[7] = clase; r[8] = comando;
                byte crc = 0; for (int i = 3; i < 89; i++) crc ^= r[i];
                r[89] = crc;
                if (!HidD_SetFeature(h, r, r.Length)) return -1;
                for (int lectura = 0; lectura < 4; lectura++)
                {
                    Thread.Sleep(lectura == 0 ? 60 : 30);
                    byte[] resp = new byte[91];
                    if (!HidD_GetFeature(h, resp, resp.Length)) return -1;
                    if (resp[7] != clase || resp[8] != comando) continue; // respuesta ajena
                    byte estado = resp[1];
                    if (estado == 0x02) return resp[10];
                    if (estado == 0x01) continue;                          // ocupado
                    if (estado == 0x04 || estado == 0x05) return -1;       // dormido / no soportado
                }
                Thread.Sleep(40 + intento * 20);
            }
            return -1;
        }

        public static Lectura Leer()
        {
            foreach (string ruta in Rutas())
            {
                using (var h = Abrir(ruta))
                {
                    if (h.IsInvalid || LargoFeature(h) != 91) continue;
                    foreach (byte tid in new byte[] { 0x1F, 0xFF, 0x3F })
                    {
                        int crudo = Pedir(h, tid, 0x07, 0x80);
                        if (crudo < 0) continue;
                        int carga = Pedir(h, tid, 0x07, 0x84);
                        var l = new Lectura();
                        l.Nombre = "Razer Basilisk V3 Pro 35K";
                        l.Clave = Lectura.ClaveRazer(l.Nombre);
                        l.Porcentaje = (int)Math.Round(crudo * 100.0 / 255.0);
                        l.Cargando = carga == 1 || ruta.ToLowerInvariant().Contains("pid_00cc");
                        l.Tipo = Tipo.Mouse;
                        l.Detalle = ruta.ToLowerInvariant().Contains("pid_00cc") ? "por cable" : "por el dongle";
                        return l;
                    }
                }
            }
            return null;
        }
    }

    // ------------------------------------------------- Razer vía Synapse (log)
    // Con Razer Synapse abierto, sus efectos de luces ocupan el canal del dongle y el
    // pedido directo no recibe respuesta. Synapse anota la batería en su log, así que
    // se lee de ahí: "powerStatus":{"chargingStatus":"Charging","level":27}
    static class Synapse
    {
        static readonly Regex Estado = new Regex(
            @"powerStatus\\?""\s*:\s*\{\\?""chargingStatus\\?""\s*:\s*\\?""(\w+)\\?""\s*,\s*\\?""level\\?""\s*:\s*(\d+)");
        static readonly Regex Hora = new Regex(@"^\[(\d{4}/\d\d/\d\d \d\d:\d\d:\d\d)", RegexOptions.Multiline);

        public static bool Abierto()
        {
            return System.Diagnostics.Process.GetProcessesByName("RazerAppEngine").Length > 0;
        }

        static readonly Regex Producto = new Regex(@"productName\\?""\s*:\s*\{\\?""en\\?""\s*:\s*\\?""([^""\\]+)");

        // Devuelve el último estado de cada dispositivo Razer con batería que anotó Synapse.
        public static List<Lectura> Leer()
        {
            var lista = new List<Lectura>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      @"Razer\RazerAppEngine\User Data\Logs");
            if (!Directory.Exists(dir)) return lista;
            var archivos = new List<FileInfo>(new DirectoryInfo(dir).GetFiles("systray_systrayv2*.log"));
            archivos.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            var vistos = new HashSet<string>();
            for (int i = 0; i < archivos.Count && i < 2 && lista.Count == 0; i++)
            {
                string texto = Cola(archivos[i].FullName, 3 * 1024 * 1024);
                MatchCollection ms = Estado.Matches(texto);
                for (int j = ms.Count - 1; j >= 0; j--)
                {
                    // El nombre está antes, dentro del mismo objeto del dispositivo.
                    int desde = Math.Max(0, ms[j].Index - 20000);
                    MatchCollection ps = Producto.Matches(texto.Substring(desde, ms[j].Index - desde));
                    string nombre = ps.Count > 0 ? ps[ps.Count - 1].Groups[1].Value : "Razer";
                    if (!nombre.StartsWith("Razer", StringComparison.OrdinalIgnoreCase)) nombre = "Razer " + nombre;

                    string estado = ms[j].Groups[1].Value;
                    if (estado == "off") continue; // registro de un estado viejo o del dispositivo apagado
                    int nivel = int.Parse(ms[j].Groups[2].Value);
                    if (nivel < 0 || nivel > 100) continue;
                    if (vistos.Contains(nombre)) continue; // ya tomé el estado más nuevo de este

                    // Synapse lo anota seguido; si hace mucho que no, el dispositivo se apagó.
                    Match h = UltimaHora(texto, ms[j].Index);
                    DateTime cuando;
                    if (h != null && DateTime.TryParseExact(h.Groups[1].Value, "yyyy/MM/dd HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out cuando)
                        && (DateTime.Now - cuando).TotalMinutes > 45)
                    { vistos.Add(nombre); continue; }
                    vistos.Add(nombre);

                    var l = new Lectura();
                    l.Nombre = nombre;
                    l.Clave = Lectura.ClaveRazer(nombre);
                    l.Porcentaje = nivel;
                    l.Cargando = estado == "Charging";
                    l.Tipo = Tipo.Mouse;
                    l.Detalle = "según Razer Synapse" + (h != null ? ", " + h.Groups[1].Value.Substring(11, 5) : "");
                    lista.Add(l);
                }
            }
            return lista;
        }

        static Match UltimaHora(string texto, int hasta)
        {
            int ini = texto.LastIndexOf("\n[", hasta, StringComparison.Ordinal);
            if (ini < 0) return null;
            Match m = Hora.Match(texto, ini + 1);
            return m.Success ? m : null;
        }

        static string Cola(string ruta, int bytes)
        {
            using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long desde = Math.Max(0, fs.Length - bytes);
                fs.Seek(desde, SeekOrigin.Begin);
                var buf = new byte[fs.Length - desde];
                int leidos = 0, n;
                while (leidos < buf.Length && (n = fs.Read(buf, leidos, buf.Length - leidos)) > 0) leidos += n;
                return Encoding.UTF8.GetString(buf, 0, leidos);
            }
        }
    }

    // -------------------------------------------------------------- Bluetooth
    static class Bluetooth
    {
        const int DIGCF_PRESENT = 2, DIGCF_ALLCLASSES = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA { public int cbSize; public Guid g; public int devInst; public IntPtr reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct DEVPROPKEY { public Guid fmtid; public int pid; public DEVPROPKEY(string g, int p) { fmtid = new Guid(g); pid = p; } }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(IntPtr g, string enumerador, IntPtr w, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, int i, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA d, StringBuilder id, int size, out int req);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA d, ref DEVPROPKEY key, out int tipo, byte[] buf, int size, out int req, int flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        static DEVPROPKEY KBateria = new DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        static DEVPROPKEY KConectado = new DEVPROPKEY("83DA6326-97A6-4088-9453-A1923F573B29", 15);
        static DEVPROPKEY KNombre = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 14);

        static readonly Regex Mac = new Regex(@"([0-9A-F]{12})(_C0+)?$", RegexOptions.IgnoreCase);

        class Disp { public string Nombre; public bool Conectado; public int Bateria = -1; }

        static byte[] Prop(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY k)
        {
            int tipo, req;
            byte[] buf = new byte[512];
            if (!SetupDiGetDevicePropertyW(set, ref d, ref k, out tipo, buf, buf.Length, out req, 0)) return null;
            byte[] r = new byte[req]; Array.Copy(buf, r, req); return r;
        }

        public static List<Lectura> Leer()
        {
            var mapa = new Dictionary<string, Disp>();
            foreach (string en in new[] { "BTHENUM", "BTHLE" })
            {
                IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, en, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
                if (set == new IntPtr(-1)) continue;
                try
                {
                    var d = new SP_DEVINFO_DATA(); d.cbSize = Marshal.SizeOf(d);
                    for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
                    {
                        var sb = new StringBuilder(512); int req;
                        if (!SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req)) continue;
                        string id = sb.ToString();
                        Match m = Mac.Match(id);
                        if (!m.Success) continue;
                        string mac = m.Groups[1].Value.ToUpperInvariant();
                        if (mac == "000000000000") continue;
                        Disp disp;
                        if (!mapa.TryGetValue(mac, out disp)) { disp = new Disp(); mapa[mac] = disp; }

                        byte[] b = Prop(set, ref d, KBateria);
                        if (b != null && b.Length >= 1) disp.Bateria = Math.Max(disp.Bateria, b[0]);

                        bool esPrincipal = id.IndexOf("\\DEV_", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (esPrincipal)
                        {
                            byte[] c = Prop(set, ref d, KConectado);
                            if (c != null && c.Length >= 1 && c[0] != 0) disp.Conectado = true;
                            byte[] n = Prop(set, ref d, KNombre);
                            if (n != null) disp.Nombre = Encoding.Unicode.GetString(n).TrimEnd('\0');
                        }
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(set); }
            }

            var lista = new List<Lectura>();
            foreach (var kv in mapa)
            {
                Disp x = kv.Value;
                if (!x.Conectado || x.Bateria < 0 || x.Bateria > 100 || string.IsNullOrEmpty(x.Nombre)) continue;
                var l = new Lectura();
                l.Clave = "bt-" + kv.Key;
                l.Nombre = x.Nombre;
                l.Porcentaje = x.Bateria;
                l.Detalle = "Bluetooth";
                // El Basilisk también se puede conectar por Bluetooth (se llama "BSKV3P").
                l.Tipo = x.Nombre.StartsWith("BSK", StringComparison.OrdinalIgnoreCase) ? Tipo.Mouse : Tipo.Bluetooth;
                lista.Add(l);
            }
            return lista;
        }
    }

    // ---------------------------------------------------------------- Dibujo
    static class Arte
    {
        public static bool TemaClaro()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                return v is int && (int)v == 1;
            }
        }

        public static Color Texto { get { return TemaClaro() ? Color.FromArgb(255, 20, 20, 20) : Color.White; } }

        static Bitmap logoRazer;
        static bool logoBuscado;

        // El logo se toma del Razer Synapse instalado en esta PC (no viene dentro de la app).
        public static Bitmap LogoRazer()
        {
            if (logoBuscado) return logoRazer;
            logoBuscado = true;
            string[] rutas = {
                @"C:\Program Files\Razer\RazerAppEngine\Apps\Common\icon.ico",
                @"C:\Program Files (x86)\Razer\ChromaBroadcast\Razer.ico" };
            foreach (string r in rutas)
            {
                try
                {
                    if (!File.Exists(r)) continue;
                    using (var ic = new Icon(r, 64, 64)) logoRazer = ic.ToBitmap();
                    return logoRazer;
                }
                catch { }
            }
            return null;
        }

        static string Glifo(Lectura l)
        {
            if (l.Tipo == Tipo.Mouse) return "\uE962";                                   // mouse
            if (Regex.IsMatch(l.Nombre, @"controller|xbox|dualsense|gamepad", RegexOptions.IgnoreCase)) return "\uE7FC";
            if (Regex.IsMatch(l.Nombre, @"\bgo\b|flip|charge|speaker|parlante|boombox", RegexOptions.IgnoreCase)) return "\uE7F5";
            return "\uE7F6";                                                              // auriculares
        }

        public static void Logo(Graphics g, Lectura l, RectangleF r, Color color)
        {
            if (l.Tipo == Tipo.Mouse)
            {
                Bitmap b = LogoRazer();
                if (b != null)
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(b, r);
                    return;
                }
            }
            using (var f = new Font("Segoe Fluent Icons", r.Height * 0.95f, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(color))
            {
                var sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Glifo(l), f, br, new RectangleF(r.X - r.Width, r.Y - r.Height, r.Width * 3, r.Height * 3), sf);
            }
        }

        public static Color ColorNivel(float pct, bool cargando)
        {
            if (cargando) return Color.FromArgb(68, 214, 44);
            if (pct <= 15) return Color.FromArgb(240, 70, 70);
            if (pct <= 30) return Color.FromArgb(250, 180, 40);
            return Color.Empty; // usa el color del texto, como la batería de Windows
        }

        static System.Drawing.Drawing2D.GraphicsPath Redondeado(RectangleF r, float radio)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void RellenarRedondeado(Graphics g, Brush b, RectangleF r, float radio)
        {
            using (var p = Redondeado(r, radio)) g.FillPath(b, p);
        }

        // Batería horizontal. pct es el nivel animado; brillo (0..1) hace latir la carga.
        public static void Bateria(Graphics g, RectangleF cuerpo, float pct, bool cargando, float brillo, Color tinta)
        {
            float s = cuerpo.Height / 11f;
            float grosor = Math.Max(1f, 1.1f * s);
            Color nivel = ColorNivel(pct, cargando);
            if (nivel.IsEmpty) nivel = tinta;

            using (var lapiz = new Pen(Color.FromArgb(220, tinta), grosor))
            using (var p = Redondeado(cuerpo, 2.6f * s))
                g.DrawPath(lapiz, p);
            using (var b = new SolidBrush(Color.FromArgb(220, tinta)))
                RellenarRedondeado(g, b, new RectangleF(cuerpo.Right + grosor * 0.6f, cuerpo.Y + cuerpo.Height * 0.3f, 1.8f * s, cuerpo.Height * 0.4f), 0.8f * s);

            float m = grosor + 1.0f * s;
            var interior = new RectangleF(cuerpo.X + m, cuerpo.Y + m, cuerpo.Width - 2 * m, cuerpo.Height - 2 * m);
            float ancho = interior.Width * Math.Max(0f, Math.Min(100f, pct)) / 100f;
            if (pct > 0 && ancho < 1.5f * s) ancho = 1.5f * s;
            if (ancho > 0)
            {
                int alfa = cargando ? (int)(150 + 105 * brillo) : 255;
                using (var b = new SolidBrush(Color.FromArgb(alfa, nivel)))
                    RellenarRedondeado(g, b, new RectangleF(interior.X, interior.Y, ancho, interior.Height), 1.2f * s);
            }

            if (cargando)
            {
                // Rayo en el centro, con borde oscuro para que se lea sobre el relleno.
                float cx = cuerpo.X + cuerpo.Width / 2f, cy = cuerpo.Y + cuerpo.Height / 2f, h = cuerpo.Height * 0.95f;
                PointF[] rayo = {
                    new PointF(cx + 0.10f * h, cy - 0.55f * h), new PointF(cx - 0.32f * h, cy + 0.08f * h),
                    new PointF(cx - 0.02f * h, cy + 0.08f * h), new PointF(cx - 0.12f * h, cy + 0.55f * h),
                    new PointF(cx + 0.32f * h, cy - 0.08f * h), new PointF(cx + 0.02f * h, cy - 0.08f * h) };
                using (var borde = new Pen(Color.FromArgb(230, 20, 20, 20), Math.Max(1f, 1.2f * s)))
                using (var b = new SolidBrush(Color.FromArgb(255, 255, 230, 80)))
                {
                    borde.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawPolygon(borde, rayo);
                    g.FillPolygon(b, rayo);
                }
            }
        }

        // Ícono chico para la bandeja (sirve para el menú y los avisos).
        public static Icon IconoBandeja(out IntPtr handle)
        {
            int lado = Math.Max(16, SystemInformation.SmallIconSize.Width) * 2;
            using (var bmp = new Bitmap(lado, lado))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float h = lado * 0.42f;
                Bateria(g, new RectangleF(lado * 0.08f, (lado - h) / 2f, lado * 0.74f, h), 70, false, 0, Texto);
                handle = bmp.GetHicon();
                return Icon.FromHandle(handle);
            }
        }

        // Ícono de cada dispositivo en el área oculta: el número grande y abajo una barrita de nivel.
        public static Icon IconoDispositivo(Lectura l, out IntPtr handle)
        {
            int lado = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = new Bitmap(lado, lado))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Color tinta = Texto;
                Color nivel = ColorNivel(l.Porcentaje, l.Cargando);
                if (nivel.IsEmpty) nivel = tinta;

                string n = l.Porcentaje.ToString();
                float tam = lado * (n.Length >= 3 ? 0.52f : 0.72f);
                var sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                using (var f = new Font("Segoe UI", tam, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(l.Porcentaje <= 15 && !l.Cargando ? nivel : tinta))
                    g.DrawString(n, f, b, new RectangleF(-3, -lado * 0.06f, lado + 6, lado * 0.80f), sf);

                float alto = Math.Max(4f, lado * 0.22f);
                var caja = new RectangleF(1, lado - alto - 0.5f, lado - 2.5f, alto);
                using (var p = new Pen(Color.FromArgb(210, tinta), 1f))
                    g.DrawRectangle(p, caja.X, caja.Y, caja.Width, caja.Height);
                using (var b = new SolidBrush(nivel))
                    g.FillRectangle(b, caja.X + 1.5f, caja.Y + 1.5f, Math.Max(1f, (caja.Width - 3f) * l.Porcentaje / 100f), caja.Height - 3f);

                handle = bmp.GetHicon();
                return Icon.FromHandle(handle);
            }
        }
    }

    // ---------------------------------------- Panel pegado a la barra de tareas
    class Barra : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct PUNTO { public int X, Y; public PUNTO(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] struct TAM { public int W, H; public TAM(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct MEZCLA { public byte Op, Flags, Alfa, Formato; }

        [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string n);
        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, int flags);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref PUNTO pos, ref TAM tam, IntPtr src, ref PUNTO srcPos, int key, ref MEZCLA m, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        static readonly IntPtr TOPMOST = new IntPtr(-1);

        public event Action<Lectura, Rectangle> ClicDispositivo;
        public event Action CambioDispositivos;

        protected override void WndProc(ref Message m)
        {
            // WM_DEVICECHANGE: se conectó o desconectó algún dispositivo.
            if (m.Msg == 0x0219 && CambioDispositivos != null) CambioDispositivos();
            base.WndProc(ref m);
        }

        List<Lectura> datos = new List<Lectura>();
        readonly Dictionary<string, float> mostrado = new Dictionary<string, float>(); // nivel animado
        readonly List<RectangleF> zonas = new List<RectangleF>();
        readonly System.Windows.Forms.Timer anim, vigia;
        readonly ToolTip ayuda = new ToolTip();
        int encima = -1, apretado = -1;
        float fase;
        Rectangle lugar;

        public Barra(ContextMenuStrip menu)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ContextMenuStrip = menu;
            ayuda.ShowAlways = true;

            anim = new System.Windows.Forms.Timer(); anim.Interval = 33;
            anim.Tick += delegate { Animar(); };
            vigia = new System.Windows.Forms.Timer(); vigia.Interval = 1000;
            vigia.Tick += delegate { Ubicar(false); };
            vigia.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000; // layered, toolwindow, topmost, noactivate
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        float Escala { get { using (var g = CreateGraphics()) return g.DpiX / 96f; } }

        public void Poner(List<Lectura> nuevos)
        {
            datos = nuevos;
            foreach (var l in datos)
                if (!mostrado.ContainsKey(l.Clave)) mostrado[l.Clave] = 0; // arranca vacía y se llena
            Ubicar(true);
            anim.Start();
        }

        public void Rellenar()
        {
            // Al hacer clic la batería se vacía y se vuelve a llenar hasta el nivel real.
            foreach (var l in datos) mostrado[l.Clave] = 0;
            anim.Start();
        }

        void Animar()
        {
            bool sigue = false;
            foreach (var l in datos)
            {
                float v = mostrado[l.Clave], t = l.Porcentaje;
                if (Math.Abs(t - v) > 0.3f) { v += (t - v) * 0.16f; sigue = true; } else v = t;
                mostrado[l.Clave] = v;
                if (l.Cargando) sigue = true;
            }
            fase += 0.07f;
            Dibujar();
            if (!sigue) anim.Stop();
        }

        bool PantallaCompleta()
        {
            IntPtr f = GetForegroundWindow();
            if (f == IntPtr.Zero || f == Handle) return false;
            var c = new StringBuilder(64); GetClassName(f, c, 64);
            string cls = c.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            RECT r; GetWindowRect(f, out r);
            Rectangle pant = Screen.FromHandle(f).Bounds;
            return r.L <= pant.Left && r.T <= pant.Top && r.R >= pant.Right && r.B >= pant.Bottom;
        }

        void Ubicar(bool redibujar)
        {
            IntPtr barra = FindWindow("Shell_TrayWnd", null);
            RECT rb, rt;
            if (barra == IntPtr.Zero || !GetWindowRect(barra, out rb)) return;
            IntPtr bandeja = FindWindowEx(barra, IntPtr.Zero, "TrayNotifyWnd", null);
            if (bandeja == IntPtr.Zero || !GetWindowRect(bandeja, out rt)) { rt = rb; rt.L = rb.R - 300; }

            bool ocultar = datos.Count == 0 || PantallaCompleta() || rb.B - rb.T > rb.R - rb.L;
            if (ocultar) { if (Visible) Hide(); return; }

            int alto = rb.B - rb.T;
            int ancho = Ancho();
            var nuevo = new Rectangle(rt.L - ancho, rb.T, ancho, alto);
            if (nuevo != lugar || redibujar || !Visible)
            {
                lugar = nuevo;
                if (!Visible) Show();
                Dibujar();
            }
            SetWindowPos(Handle, TOPMOST, lugar.X, lugar.Y, lugar.Width, lugar.Height, 0x10 | 0x40); // noactivate, showwindow
        }

        // Medidas de cada dispositivo: [logo] [batería] 27%
        float Logo { get { return 16 * Escala; } }
        float BatAncho { get { return 22 * Escala; } }
        float BatAlto { get { return 11 * Escala; } }

        Font Fuente() { return new Font("Segoe UI", 12 * Escala, FontStyle.Regular, GraphicsUnit.Pixel); }

        float AnchoTexto(Graphics g, Font f, string s)
        {
            return g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        int Ancho()
        {
            float e = Escala, total = 4 * e;
            zonas.Clear();
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var f = Fuente())
            {
                foreach (var l in datos)
                {
                    // El ancho se reserva para "100%" así no salta cuando cambia el número.
                    float w = 8 * e + Logo + 6 * e + BatAncho + 3 * e + 5 * e + AnchoTexto(g, f, "100%") + 8 * e;
                    zonas.Add(new RectangleF(total, 0, w, 0));
                    total += w + 2 * e;
                }
            }
            return (int)Math.Ceiling(total + 2 * e);
        }

        void Dibujar()
        {
            if (lugar.Width <= 0 || lugar.Height <= 0 || !IsHandleCreated) return;
            float e = Escala;
            Color tinta = Arte.Texto;
            bool claro = Arte.TemaClaro();
            using (var bmp = new Bitmap(lugar.Width, lugar.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var f = Fuente())
                using (var br = new SolidBrush(tinta))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.FromArgb(1, 0, 0, 0)); // casi invisible, pero recibe el mouse

                    for (int i = 0; i < datos.Count && i < zonas.Count; i++)
                    {
                        var l = datos[i];
                        var z = zonas[i];
                        var fondo = new RectangleF(z.X, 4 * e, z.Width, lugar.Height - 8 * e);
                        zonas[i] = fondo;
                        if (i == encima || i == apretado)
                        {
                            int a = i == apretado ? 26 : 16;
                            using (var hb = new SolidBrush(claro ? Color.FromArgb(a * 2, 0, 0, 0) : Color.FromArgb(a, 255, 255, 255)))
                                Arte.RellenarRedondeado(g, hb, fondo, 4 * e);
                        }

                        float cy = lugar.Height / 2f, x = z.X + 8 * e;
                        Arte.Logo(g, l, new RectangleF(x, cy - Logo / 2f, Logo, Logo), tinta);
                        x += Logo + 6 * e;

                        float v; if (!mostrado.TryGetValue(l.Clave, out v)) v = l.Porcentaje;
                        float brillo = (float)(0.5 + 0.5 * Math.Sin(fase * 2.2 + i));
                        Arte.Bateria(g, new RectangleF(x, cy - BatAlto / 2f, BatAncho, BatAlto), v, l.Cargando, brillo, tinta);
                        x += BatAncho + 3 * e + 5 * e;

                        string texto = Math.Round(v) + "%";
                        Color col = Arte.ColorNivel(l.Porcentaje, false);
                        using (var tb = new SolidBrush(l.Porcentaje <= 15 && !l.Cargando ? col : tinta))
                        {
                            float altoTexto = f.GetHeight(g);
                            g.DrawString(texto, f, tb, x, cy - altoTexto / 2f + 0.5f * e);
                        }
                    }
                }
                Pintar(bmp);
            }
        }

        void Pintar(Bitmap bmp)
        {
            IntPtr pantalla = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(pantalla);
            IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), viejo = SelectObject(mem, hb);
            try
            {
                var tam = new TAM(bmp.Width, bmp.Height);
                var origen = new PUNTO(0, 0);
                var pos = new PUNTO(lugar.X, lugar.Y);
                var m = new MEZCLA(); m.Op = 0; m.Flags = 0; m.Alfa = 255; m.Formato = 1;
                UpdateLayeredWindow(Handle, pantalla, ref pos, ref tam, mem, ref origen, 0, ref m, 2);
            }
            finally
            {
                SelectObject(mem, viejo); DeleteObject(hb); DeleteDC(mem); ReleaseDC(IntPtr.Zero, pantalla);
            }
        }

        int Zona(Point p)
        {
            for (int i = 0; i < zonas.Count; i++)
                if (p.X >= zonas[i].Left && p.X < zonas[i].Right) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = Zona(e.Location);
            if (z == encima) return;
            encima = z;
            Dibujar();
            if (z >= 0 && z < datos.Count)
            {
                var l = datos[z];
                string t = l.Nombre + "\n" + l.Porcentaje + "%" + (l.Cargando ? " · cargando" : "") +
                           (string.IsNullOrEmpty(l.Detalle) ? "" : "\n" + l.Detalle);
                ayuda.Show(t, this, (int)zonas[z].X, -(int)(58 * Escala), 4000);
            }
            else ayuda.Hide(this);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            encima = -1; apretado = -1;
            ayuda.Hide(this);
            Dibujar();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            apretado = Zona(e.Location);
            Dibujar();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int z = Zona(e.Location);
            apretado = -1;
            ayuda.Hide(this);
            if (z >= 0 && z < datos.Count && ClicDispositivo != null)
            {
                var r = zonas[z];
                ClicDispositivo(datos[z], new Rectangle(lugar.X + (int)r.X, lugar.Y, (int)r.Width, lugar.Height));
            }
            Dibujar();
        }
    }

    // --------------------------------------------- Ventanita con los detalles
    class Detalle : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        readonly List<Lectura> datos;
        readonly Dictionary<string, float> nivel = new Dictionary<string, float>();
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        readonly bool claro = Arte.TemaClaro();
        readonly float e;
        float fase;

        public Detalle(List<Lectura> lecturas, Rectangle ancla)
        {
            datos = lecturas;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = claro ? Color.FromArgb(243, 243, 243) : Color.FromArgb(36, 36, 36);
            using (var g = CreateGraphics()) e = g.DpiX / 96f;

            int ancho = (int)(320 * e), alto = (int)((24 + 64 * datos.Count + 30) * e);
            Size = new Size(ancho, alto);
            Rectangle area = Screen.FromRectangle(ancla).WorkingArea;
            int x = Math.Min(ancla.Right - ancho + (int)(40 * e), area.Right - ancho - (int)(12 * e));
            Location = new Point(Math.Max(area.Left, x), ancla.Top - alto - (int)(12 * e));

            foreach (var l in datos) nivel[l.Clave] = 0;
            anim.Interval = 16;
            anim.Tick += delegate
            {
                fase += 0.05f;
                foreach (var l in datos)
                {
                    float v = nivel[l.Clave];
                    nivel[l.Clave] = Math.Abs(l.Porcentaje - v) > 0.3f ? v + (l.Porcentaje - v) * 0.12f : l.Porcentaje;
                }
                Invalidate();
            };
            anim.Start();
            Deactivate += delegate { Close(); };
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs k) { if (k.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnHandleCreated(EventArgs ev)
        {
            base.OnHandleCreated(ev);
            int redondo = 2, oscuro = claro ? 0 : 1;
            DwmSetWindowAttribute(Handle, 33, ref redondo, 4); // esquinas redondeadas de Windows 11
            DwmSetWindowAttribute(Handle, 20, ref oscuro, 4);
        }

        protected override void OnClosed(EventArgs ev) { anim.Stop(); anim.Dispose(); base.OnClosed(ev); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color tinta = claro ? Color.FromArgb(20, 20, 20) : Color.White;
            Color gris = claro ? Color.FromArgb(100, 100, 100) : Color.FromArgb(170, 170, 170);

            using (var fNombre = new Font("Segoe UI Semibold", 13 * e, GraphicsUnit.Pixel))
            using (var fChico = new Font("Segoe UI", 11.5f * e, GraphicsUnit.Pixel))
            using (var fGrande = new Font("Segoe UI Semibold", 20 * e, GraphicsUnit.Pixel))
            using (var bTinta = new SolidBrush(tinta))
            using (var bGris = new SolidBrush(gris))
            {
                float y = 16 * e;
                for (int i = 0; i < datos.Count; i++)
                {
                    var l = datos[i];
                    float v = nivel[l.Clave];
                    float x = 16 * e;
                    Arte.Logo(g, l, new RectangleF(x, y + 12 * e, 28 * e, 28 * e), tinta);
                    x += 40 * e;

                    string nombre = l.Nombre;
                    g.DrawString(nombre, fNombre, bTinta, new RectangleF(x, y + 6 * e, 150 * e, 20 * e),
                                 new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
                    string estado = l.Cargando ? "Cargando" : (l.Porcentaje <= 15 ? "Batería baja" : "En batería");
                    if (!string.IsNullOrEmpty(l.Detalle)) estado += " · " + l.Detalle;
                    g.DrawString(estado, fChico, bGris, new RectangleF(x, y + 27 * e, 160 * e, 34 * e));

                    float brillo = (float)(0.5 + 0.5 * Math.Sin(fase * 3 + i));
                    Arte.Bateria(g, new RectangleF(204 * e, y + 19 * e, 36 * e, 18 * e), v, l.Cargando, brillo, tinta);
                    string pct = Math.Round(v) + "%";
                    Color c = Arte.ColorNivel(l.Porcentaje, false);
                    using (var bp = new SolidBrush(l.Porcentaje <= 15 && !l.Cargando ? c : tinta))
                    {
                        var sf = new StringFormat(); sf.Alignment = StringAlignment.Far; sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(pct, fGrande, bp, new RectangleF(240 * e, y + 8 * e, Width - 252 * e, 40 * e), sf);
                    }
                    y += 64 * e;
                }
                using (var fFirma = new Font("Segoe UI", 10 * e, GraphicsUnit.Pixel))
                    g.DrawString("Proyecto elaborado por Ctrl + Save Design Studio", fFirma, bGris, 16 * e, Height - 24 * e);
            }
        }
    }

    // ----------------------------------------------------------- Preferencias
    static class Preferencias
    {
        public const string Ruta = @"Software\Ctrl + Save Design Studio\Battery Level";

        // Claves de los dispositivos que el usuario sacó de la barra (siguen en el área oculta).
        public static HashSet<string> Ocultos()
        {
            var h = new HashSet<string>();
            using (var k = Registry.CurrentUser.OpenSubKey(Ruta))
            {
                string[] v = k == null ? null : k.GetValue("OcultosEnBarra") as string[];
                if (v != null) foreach (string s in v) h.Add(s);
            }
            return h;
        }

        public static void GuardarOcultos(HashSet<string> h)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Ruta))
                k.SetValue("OcultosEnBarra", new List<string>(h).ToArray(), RegistryValueKind.MultiString);
        }
    }

    // ------------------------------------------------------------------- App
    class App : ApplicationContext
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public const string ClaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string NombreRun = "BatteryLevel";
        const int Bajo = 15;

        readonly HashSet<string> avisados = new HashSet<string>();
        readonly HashSet<string> ocultos = Preferencias.Ocultos();
        readonly Dictionary<string, NotifyIcon> iconos = new Dictionary<string, NotifyIcon>();
        readonly Dictionary<string, IntPtr> hIconos = new Dictionary<string, IntPtr>();
        readonly NotifyIcon general;   // solo aparece si no hay ningún dispositivo
        readonly IntPtr hGeneral;
        readonly ContextMenuStrip menu;
        readonly Barra barra;
        readonly System.Windows.Forms.Timer timer, pronto;
        readonly SynchronizationContext ui;
        List<Lectura> ultimas = new List<Lectura>();
        Detalle detalle;
        int leyendo, repetir;

        public App()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            MigrarInicio();

            menu = new ContextMenuStrip();
            menu.Opening += delegate { ArmarMenu(); };
            ArmarMenu();

            general = new NotifyIcon();
            general.Icon = Arte.IconoBandeja(out hGeneral);
            general.Text = "Battery Level: sin dispositivos";
            general.ContextMenuStrip = menu;
            general.Visible = true;

            barra = new Barra(menu);
            barra.ClicDispositivo += delegate(Lectura l, Rectangle r) { barra.Rellenar(); AbrirDetalle(r); };
            barra.CambioDispositivos += delegate { Pronto(); };
            barra.CreateControl();
            var h = barra.Handle; // crea la ventana (sin mostrarla) para recibir los avisos de conexión

            // Cuando se conecta o desconecta algo, Windows avisa; se lee a los 3 s y otra vez a los 12 s,
            // porque algunos auriculares informan la batería unos segundos después de conectarse.
            pronto = new System.Windows.Forms.Timer();
            pronto.Tick += delegate
            {
                pronto.Stop();
                Actualizar();
                if (repetir-- > 0) { pronto.Interval = 9000; pronto.Start(); }
            };

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 30 * 1000;
            timer.Tick += delegate { Actualizar(); };
            timer.Start();
            Actualizar();
        }

        void Pronto()
        {
            pronto.Stop();
            pronto.Interval = 3000;
            repetir = 1;
            pronto.Start();
        }

        void ArmarMenu()
        {
            menu.Items.Clear();

            var titulo = new ToolStripMenuItem("Mostrar en la barra de tareas");
            titulo.Enabled = false;
            menu.Items.Add(titulo);
            if (ultimas.Count == 0)
            {
                var nada = new ToolStripMenuItem("   (no hay dispositivos con batería conectados)");
                nada.Enabled = false;
                menu.Items.Add(nada);
            }
            foreach (var l in ultimas)
            {
                string clave = l.Clave;
                var item = new ToolStripMenuItem(l.Nombre + "  ·  " + l.Porcentaje + "%" + (l.Cargando ? " (cargando)" : ""));
                item.Checked = !ocultos.Contains(clave);
                item.Click += delegate
                {
                    if (!ocultos.Remove(clave)) ocultos.Add(clave);
                    Preferencias.GuardarOcultos(ocultos);
                    barra.Poner(EnBarra());
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Ver detalle", null, delegate { AbrirDetalle(Rectangle.Empty); });
            menu.Items.Add("Actualizar ahora", null, delegate { Actualizar(); });
            var inicio = new ToolStripMenuItem("Iniciar con Windows");
            inicio.Checked = InicioActivado();
            inicio.Click += delegate { CambiarInicio(!InicioActivado()); };
            menu.Items.Add(inicio);
            menu.Items.Add(new ToolStripSeparator());
            var firma = new ToolStripMenuItem("Proyecto elaborado por Ctrl + Save Design Studio");
            firma.Enabled = false;
            menu.Items.Add(firma);
            menu.Items.Add("Salir", null, delegate { Salir(); });
        }

        List<Lectura> EnBarra()
        {
            return ultimas.FindAll(l => !ocultos.Contains(l.Clave));
        }

        void AbrirDetalle(Rectangle ancla)
        {
            if (detalle != null && !detalle.IsDisposed) { detalle.Close(); return; }
            if (ultimas.Count == 0) return;
            if (ancla == Rectangle.Empty)
            {
                Rectangle t = Screen.PrimaryScreen.Bounds, w = Screen.PrimaryScreen.WorkingArea;
                ancla = new Rectangle(t.Right - 200, w.Bottom, 160, Math.Max(1, t.Bottom - w.Bottom));
            }
            detalle = new Detalle(ultimas, ancla);
            detalle.Show();
            detalle.Activate();
        }

        public static List<Lectura> LeerRazer()
        {
            if (Synapse.Abierto())
            {
                try { var s = Synapse.Leer(); if (s.Count > 0) return s; } catch { }
            }
            var lista = new List<Lectura>();
            var r = Razer.Leer();
            if (r != null) lista.Add(r);
            return lista;
        }

        public static List<Lectura> LeerTodo()
        {
            var lecturas = new List<Lectura>();
            try { lecturas.AddRange(LeerRazer()); } catch { }
            try { lecturas.AddRange(Bluetooth.Leer()); } catch { }
            // Si el mouse Razer ya respondió por el dongle o Synapse, no repetirlo por Bluetooth.
            if (lecturas.Exists(l => l.Clave.StartsWith("razer-")))
                lecturas.RemoveAll(l => l.Clave.StartsWith("bt-") && l.Tipo == Tipo.Mouse);
            // Joysticks por XInput. Un joystick Xbox por Bluetooth también aparece en XInput,
            // pero el Bluetooth da el % exacto: se saltean tantos como ya se leyeron por Bluetooth.
            try
            {
                var xi = XInput.Leer();
                int yaPorBt = lecturas.FindAll(l => l.Clave.StartsWith("bt-") &&
                    Regex.IsMatch(l.Nombre, @"xbox|controller|gamepad", RegexOptions.IgnoreCase)).Count;
                for (int i = yaPorBt; i < xi.Count; i++) lecturas.Add(xi[i]);
            }
            catch { }
            return lecturas;
        }

        void Actualizar()
        {
            if (Interlocked.Exchange(ref leyendo, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Lectura> lecturas;
                try { lecturas = LeerTodo(); } catch { lecturas = new List<Lectura>(); }
                ui.Post(delegate { Mostrar(lecturas); Interlocked.Exchange(ref leyendo, 0); }, null);
            });
        }

        void Mostrar(List<Lectura> lecturas)
        {
            ultimas = lecturas;
            barra.Poner(EnBarra());

            // Un ícono por dispositivo en el área de notificación (por defecto quedan en la parte oculta, "^").
            var presentes = new HashSet<string>();
            foreach (var l in lecturas)
            {
                presentes.Add(l.Clave);
                NotifyIcon ni;
                if (!iconos.TryGetValue(l.Clave, out ni))
                {
                    ni = new NotifyIcon();
                    ni.ContextMenuStrip = menu;
                    ni.MouseClick += delegate(object s, MouseEventArgs m) { if (m.Button == MouseButtons.Left) AbrirDetalle(Rectangle.Empty); };
                    iconos[l.Clave] = ni;
                }
                IntPtr hv;
                hIconos.TryGetValue(l.Clave, out hv);
                IntPtr hn;
                ni.Icon = Arte.IconoDispositivo(l, out hn);
                hIconos[l.Clave] = hn;
                if (hv != IntPtr.Zero) DestroyIcon(hv);
                string t = l.Nombre + ": " + l.Porcentaje + "%" + (l.Cargando ? " (cargando)" : "");
                ni.Text = t.Length > 63 ? t.Substring(0, 63) : t;
                ni.Visible = true;
            }
            foreach (var clave in new List<string>(iconos.Keys))
            {
                if (presentes.Contains(clave)) continue;
                iconos[clave].Visible = false;
                iconos[clave].Dispose();
                iconos.Remove(clave);
                IntPtr hv;
                if (hIconos.TryGetValue(clave, out hv)) { DestroyIcon(hv); hIconos.Remove(clave); }
            }
            general.Visible = lecturas.Count == 0;

            foreach (var l in lecturas)
            {
                if (l.Porcentaje <= Bajo && !l.Cargando)
                {
                    if (avisados.Add(l.Clave))
                        iconos[l.Clave].ShowBalloonTip(8000, "Batería baja", l.Nombre + " está al " + l.Porcentaje + "%.", ToolTipIcon.Warning);
                }
                else if (l.Porcentaje > Bajo + 5 || l.Cargando) avisados.Remove(l.Clave);
            }
        }

        static bool InicioActivado()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(ClaveRun))
                return k != null && k.GetValue(NombreRun) != null;
        }

        static void CambiarInicio(bool activar)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(ClaveRun))
            {
                if (activar) k.SetValue(NombreRun, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(NombreRun, false);
            }
        }

        // La versión anterior se llamaba "Bateria Tray": si arrancaba con Windows, pasa a esta.
        static void MigrarInicio()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ClaveRun, true))
                {
                    if (k == null || k.GetValue("BateriaTray") == null) return;
                    k.DeleteValue("BateriaTray", false);
                    k.SetValue(NombreRun, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch { }
        }

        void Salir()
        {
            timer.Stop(); pronto.Stop();
            foreach (var ni in iconos.Values) { ni.Visible = false; ni.Dispose(); }
            foreach (var hv in hIconos.Values) DestroyIcon(hv);
            general.Visible = false; general.Dispose();
            DestroyIcon(hGeneral);
            barra.Close();
            ExitThread();
        }
    }

    // ---------------------------------------------------------- Desinstalar
    // Lo llama Windows desde "Aplicaciones instaladas" (BatteryLevel.exe --desinstalar).
    static class Desinstalador
    {
        public const string ClaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BatteryLevel";

        public static void Ejecutar(bool silencioso)
        {
            if (!silencioso && MessageBox.Show("¿Querés desinstalar Battery Level?", "Battery Level",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int yo = System.Diagnostics.Process.GetCurrentProcess().Id;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("BatteryLevel"))
            {
                if (p.Id == yo) continue;
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }

            try { using (var k = Registry.CurrentUser.OpenSubKey(App.ClaveRun, true)) if (k != null) k.DeleteValue(App.NombreRun, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Preferencias.Ruta, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(ClaveDesinstalar, false); } catch { }
            try
            {
                string atajo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Battery Level.lnk");
                if (File.Exists(atajo)) File.Delete(atajo);
            }
            catch { }

            // La carpeta se borra unos segundos después, cuando este programa ya se cerró.
            // Solo se borra la carpeta oficial de instalación, nunca otra (por ejemplo, si
            // alguien copió el programa al Escritorio y lo ejecuta con --desinstalar desde ahí).
            string carpeta = Path.GetFullPath(Path.GetDirectoryName(Application.ExecutablePath)).TrimEnd('\\');
            string oficial = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Battery Level")).TrimEnd('\\');
            if (string.Equals(carpeta, oficial, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(carpeta, "instalado.txt")))
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"" + carpeta + "\"");
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi);
            }
            if (!silencioso)
                MessageBox.Show("Battery Level se desinstaló.", "Battery Level", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            SetProcessDPIAware(); // medidas en píxeles reales, para alinearse con la barra de tareas
            if (!CtrlSave.Integridad.Comprobar(Application.ExecutablePath, "Battery Level")) return;
            string arg = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (arg == "--desinstalar")
            {
                Application.EnableVisualStyles();
                Desinstalador.Ejecutar(args.Length > 1 && args[1] == "--silencioso");
                return;
            }

            if (arg == "--prueba")
            {
                // Diagnóstico: escribe lo que lee en prueba.txt, al lado del exe.
                var sb = new StringBuilder();
                foreach (var l in App.LeerTodo())
                    sb.AppendLine(l.Clave + " | " + l.Nombre + " | " + l.Porcentaje + "% | cargando=" + l.Cargando + " | " + l.Detalle);
                if (sb.Length == 0) sb.AppendLine("Sin dispositivos con batería.");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "prueba.txt"), sb.ToString(), Encoding.UTF8);
                return;
            }

            bool nuevo;
            using (new Mutex(true, "CtrlSave.BatteryLevel", out nuevo))
            {
                if (!nuevo) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Application.Run(new App());
            }
        }
    }
}
