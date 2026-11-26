// Instalador de Battery Level. Lleva BatteryLevel.exe adentro (como recurso) y lo
// instala para el usuario actual, sin pedir permisos de administrador.
// Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Instalador de Battery Level")]
[assembly: AssemblyProduct("Battery Level")]
[assembly: AssemblyCompany("Ctrl + Save Design Studio")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace BatteryLevelSetup
{
    static class Instalacion
    {
        public const string Version = "1.2.0";
        public const string ClaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ClaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BatteryLevel";
        public const string Repo = "https://github.com/ctrolsave/Battery-level";

        public static string Carpeta
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    @"Programs\Battery Level");
            }
        }

        public static string Exe { get { return Path.Combine(Carpeta, "BatteryLevel.exe"); } }

        static void Copiar(string recurso, string destino)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso))
            using (var f = File.Create(destino))
                s.CopyTo(f);
        }

        static void CerrarAnteriores()
        {
            foreach (string nombre in new[] { "BatteryLevel", "BateriaTray" })
                foreach (var p in Process.GetProcessesByName(nombre))
                    try { p.Kill(); p.WaitForExit(4000); } catch { }
        }

        public static void Instalar(bool alIniciar, bool abrir)
        {
            CerrarAnteriores();
            Directory.CreateDirectory(Carpeta);
            Copiar("BatteryLevel.exe", Exe);
            string problema = CtrlSave.Integridad.Problema(Exe);
            if (problema != null)
            {
                try { File.Delete(Exe); } catch { }
                throw new Exception("El programa que trae este instalador " + problema +
                    ". No se instaló nada. Ctrl + Save Design Studio no se hace cargo de copias modificadas.");
            }
            Copiar("LICENSE", Path.Combine(Carpeta, "LICENSE.txt"));
            File.WriteAllText(Path.Combine(Carpeta, "instalado.txt"),
                "Battery Level " + Version + "\r\nProyecto elaborado por Ctrl + Save Design Studio\r\n");

            // Acceso directo en el menú Inicio.
            string atajo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Battery Level.lnk");
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { atajo });
                Type tl = lnk.GetType();
                tl.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { Exe });
                tl.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Carpeta });
                tl.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Batería de tus dispositivos en la barra de tareas" });
                tl.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }

            // Para que aparezca en Configuración > Aplicaciones instaladas.
            using (var k = Registry.CurrentUser.CreateSubKey(ClaveDesinstalar))
            {
                k.SetValue("DisplayName", "Battery Level");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Ctrl + Save Design Studio");
                k.SetValue("DisplayIcon", Exe);
                k.SetValue("InstallLocation", Carpeta);
                k.SetValue("UninstallString", "\"" + Exe + "\" --desinstalar");
                k.SetValue("QuietUninstallString", "\"" + Exe + "\" --desinstalar --silencioso");
                k.SetValue("URLInfoAbout", Repo);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
            }

            using (var k = Registry.CurrentUser.CreateSubKey(ClaveRun))
            {
                k.DeleteValue("BateriaTray", false); // nombre de la versión anterior
                if (alIniciar) k.SetValue("BatteryLevel", "\"" + Exe + "\"");
                else k.DeleteValue("BatteryLevel", false);
            }

            if (abrir) Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = Carpeta });
        }
    }

    class Ventana : Form
    {
        readonly CheckBox inicio = new CheckBox(), abrir = new CheckBox();
        readonly Button instalar = new Button(), cancelar = new Button();

        public Ventana()
        {
            Text = "Instalar Battery Level";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(460, 292);
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var titulo = new Label { Text = "Battery Level " + Instalacion.Version, Font = new Font("Segoe UI Semibold", 15f),
                                     AutoSize = true, Location = new Point(20, 16) };
            var texto = new Label
            {
                Text = "Muestra en la barra de tareas la batería de tu mouse o auriculares Razer y de " +
                       "todos los dispositivos Bluetooth conectados que informen batería.\n\n" +
                       "Se instala en:  " + Instalacion.Carpeta + "\n\n" +
                       "Firmado digitalmente por Ctrl + Save Design Studio.",
                Location = new Point(22, 56), Size = new Size(420, 112)
            };
            inicio.Text = "Iniciar con Windows"; inicio.Checked = true; inicio.AutoSize = true; inicio.Location = new Point(24, 172);
            abrir.Text = "Abrir Battery Level al terminar"; abrir.Checked = true; abrir.AutoSize = true; abrir.Location = new Point(24, 198);

            instalar.Text = "Instalar"; instalar.Size = new Size(100, 30); instalar.Location = new Point(240, 236);
            cancelar.Text = "Cancelar"; cancelar.Size = new Size(100, 30); cancelar.Location = new Point(346, 236);
            instalar.Click += delegate { Instalar(); };
            cancelar.Click += delegate { Close(); };
            AcceptButton = instalar; CancelButton = cancelar;

            var firma = new Label { Text = "Proyecto elaborado por Ctrl + Save Design Studio", ForeColor = Color.Gray,
                                    Font = new Font("Segoe UI", 8f), AutoSize = true, Location = new Point(22, 270) };

            Controls.AddRange(new Control[] { titulo, texto, inicio, abrir, instalar, cancelar, firma });
        }

        void Instalar()
        {
            instalar.Enabled = false; cancelar.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                Instalacion.Instalar(inicio.Checked, abrir.Checked);
                Cursor = Cursors.Default;
                MessageBox.Show(this,
                    "Listo. Battery Level quedó instalado.\n\n" +
                    "• Clic derecho sobre el panel de la barra para elegir qué dispositivos ver.\n" +
                    "• Todos los dispositivos aparecen también en los íconos ocultos (^).\n" +
                    "• Para quitarlo: Configuración > Aplicaciones > Battery Level > Desinstalar.",
                    "Battery Level", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, "No se pudo instalar:\n" + ex.Message, "Battery Level", MessageBoxButtons.OK, MessageBoxIcon.Error);
                instalar.Enabled = true; cancelar.Enabled = true;
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (!CtrlSave.Integridad.Comprobar(Application.ExecutablePath, "el instalador de Battery Level")) return;

            // Modo sin ventana: BatteryLevel-Setup.exe /silencioso [/sin-inicio] [/sin-abrir]
            bool silencioso = false, alIniciar = true, abrir = true;
            foreach (string a in args)
            {
                string x = a.ToLowerInvariant();
                if (x == "/silencioso" || x == "/s") silencioso = true;
                if (x == "/sin-inicio") alIniciar = false;
                if (x == "/sin-abrir") abrir = false;
            }
            if (silencioso) { Instalacion.Instalar(alIniciar, abrir); return; }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Ventana());
        }
    }
}
