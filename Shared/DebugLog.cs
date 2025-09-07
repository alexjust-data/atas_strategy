using System;
using System.IO;

namespace MyAtasIndicator.Shared
{
    public static class DebugLog
    {
        private static readonly string _file =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "ATAS", "Logs", "Auto468.log");

        public static void W(string tag, string msg)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {msg}";
                File.AppendAllText(_file, line + Environment.NewLine);
            }
            catch { /* no romper nunca la ejecución por log */ }
        }
    }
}
