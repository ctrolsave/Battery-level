// Chequeo de integridad de los programas de Ctrl + Save Design Studio.
// Cada .exe revisa su propia firma digital al abrirse. Si alguien lo modificó
// (por ejemplo, para meterle un virus), le sacó la firma o lo volvió a firmar con
// otro certificado, muestra un aviso y no sigue.
// Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace CtrlSave
{
    static class Integridad
    {
        // Huella del certificado de firma de Ctrl + Save Design Studio (es pública,
        // está en CtrlSave-Design-Studio.cer). La clave privada no sale nunca de la PC del estudio.
        public const string Huella = "56BDEEC1FB847B3C2A684D7D0E3F7FB26E620A6C";

        const uint CERT_E_UNTRUSTEDROOT = 0x800B0109, CERT_E_CHAINING = 0x800B010A;
        const uint TRUST_E_NOSIGNATURE = 0x800B0100, TRUST_E_BAD_DIGEST = 0x80096010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public int cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile, pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_DATA
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData, pSIPClientData;
            public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData;
            public string pwszURLReference;
            public int dwProvFlags, dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        // null si el archivo está intacto y firmado por el estudio; si no, el motivo.
        public static string Problema(string ruta)
        {
            var archivo = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = ruta };
            IntPtr pArchivo = Marshal.AllocHGlobal(archivo.cbStruct);
            try
            {
                Marshal.StructureToPtr(archivo, pArchivo, false);
                var datos = new WINTRUST_DATA
                {
                    cbStruct = Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,          // sin ventanas
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1,       // un archivo
                    pFile = pArchivo,
                    dwProvFlags = 0x10 | 0x1000 // sin revisar revocación ni salir a internet
                };
                Guid accion = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                uint r = WinVerifyTrust(IntPtr.Zero, ref accion, ref datos);

                // El certificado es propio (no de una autoridad comprada), así que en otras PCs
                // Windows dice "raíz no confiable"; eso es esperable. Lo que importa es que el
                // contenido coincida con la firma y que la firma sea la nuestra.
                if (r == TRUST_E_NOSIGNATURE) return "no tiene la firma digital de Ctrl + Save Design Studio";
                if (r == TRUST_E_BAD_DIGEST) return "fue modificado después de firmarse";
                if (r != 0 && r != CERT_E_UNTRUSTEDROOT && r != CERT_E_CHAINING)
                    return "tiene una firma digital que no es válida (código 0x" + r.ToString("X8") + ")";

                var cert = X509Certificate.CreateFromSignedFile(ruta);
                if (!string.Equals(cert.GetCertHashString(), Huella, StringComparison.OrdinalIgnoreCase))
                    return "está firmado por otra persona que se hace pasar por Ctrl + Save Design Studio";
                return null;
            }
            catch (Exception ex)
            {
                return "no se pudo comprobar su firma (" + ex.Message + ")";
            }
            finally
            {
                Marshal.FreeHGlobal(pArchivo);
            }
        }

        // Revisa el propio .exe. Si está alterado, avisa y devuelve false para que el programa se cierre.
        public static bool Comprobar(string ruta, string nombre)
        {
            string p = Problema(ruta);
            if (p == null) return true;
            MessageBox.Show(
                "ATENCIÓN: esta copia de " + nombre + " NO es la original.\n\n" +
                "El archivo " + p + ". Alguien pudo haberlo alterado para meterle un virus.\n\n" +
                "Ctrl + Save Design Studio no se hace cargo de copias modificadas ni de lo que puedan hacer en tu PC. " +
                "Por seguridad, el programa no se va a abrir.\n\n" +
                "Borrá este archivo y pedí el instalador original a Ctrl + Save Design Studio.\n\n" +
                "Archivo: " + ruta,
                nombre + " - copia modificada", MessageBoxButtons.OK, MessageBoxIcon.Stop);
            return false;
        }
    }
}
