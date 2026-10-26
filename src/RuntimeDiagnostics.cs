using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace PotPlayerFrameClip
{
    internal static class RuntimeDiagnostics
    {
        internal static string Write(Exception error, string phase)
        {
            try
            {
                string directory = Path.Combine(AppPaths.DataDirectory, "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "runtime-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
                string header = "FrameClip " + Assembly.GetExecutingAssembly().GetName().Version +
                    " | " + (Environment.Is64BitProcess ? "x64" : "x86") + " | " + phase + Environment.NewLine;
                File.WriteAllText(path, header + error, new UTF8Encoding(false));
                return path;
            }
            catch { return null; }
        }
    }
}
