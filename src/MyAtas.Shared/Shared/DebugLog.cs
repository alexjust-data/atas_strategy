using System;
using System.IO;
using System.Threading;

namespace MyAtas.Shared
{
    public static class DebugLog
    {
        private static readonly object _lockObj = new object();
        private static string _logFilePath = null;
        private static bool _initialized = false;

        private static void InitializeLogFile()
        {
            if (_initialized) return;
            
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var logDir = Path.Combine(baseDir, "ATAS_Strategy_Logs");
                
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(logDir, $"ATAS_468_Debug_{timestamp}.log");
                
                // Escribir header inicial con rutas completas
                File.WriteAllText(_logFilePath, $"=== ATAS 468 Strategy Debug Log ===\n");
                File.AppendAllText(_logFilePath, $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                File.AppendAllText(_logFilePath, $"Base Directory: {baseDir}\n");
                File.AppendAllText(_logFilePath, $"Log Directory: {logDir}\n");
                File.AppendAllText(_logFilePath, $"Full Log Path: {_logFilePath}\n");
                File.AppendAllText(_logFilePath, $"Current Working Dir: {Environment.CurrentDirectory}\n");
                File.AppendAllText(_logFilePath, $"Process Name: {System.Diagnostics.Process.GetCurrentProcess().ProcessName}\n");
                File.AppendAllText(_logFilePath, "=====================================\n\n");
                
                // También escribir la ruta al console para que la veas inmediatamente
                Console.WriteLine($"*** LOG FILE CREATED AT: {_logFilePath} ***");
                System.Diagnostics.Debug.WriteLine($"*** LOG FILE CREATED AT: {_logFilePath} ***");
                
                _initialized = true;
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"*** LOG FILE CREATION FAILED: {ex.Message} ***");
            }
        }

        public static void W(string tag, string msg)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logLine = $"[{timestamp}] [{tag}] {msg}";
            
            try
            {
                // Outputs originales
                System.Diagnostics.Debug.WriteLine(logLine);
                Console.WriteLine(logLine);
                
                // EMERGENCY LOG: Siempre escribir al proyecto si el log normal falla
                try
                {
                    // Usar directorio del proyecto en lugar del Desktop
                    var projectRoot = @"C:\Users\AlexJ\Desktop\projects\01_atas\06_ATAS_strategy - v2";
                    var emergencyPath = Path.Combine(projectRoot, "EMERGENCY_ATAS_LOG.txt");
                    File.AppendAllText(emergencyPath, logLine + Environment.NewLine);
                }
                catch { /* ignore */ }
                
                // Logging a archivo normal
                lock (_lockObj)
                {
                    InitializeLogFile();
                    if (_logFilePath != null)
                    {
                        File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                    }
                }
            }
            catch { /* no-op */ }
        }

        // Método para logging crítico (siempre se guarda)
        public static void Critical(string tag, string msg)
        {
            W($"CRITICAL-{tag}", msg);
        }

        // Método para obtener la ruta del log actual
        public static string GetLogPath()
        {
            lock (_lockObj)
            {
                InitializeLogFile();
                return _logFilePath ?? "Log file not available";
            }
        }

        public static string PathHint()
        {
            try { return AppDomain.CurrentDomain.BaseDirectory ?? ""; }
            catch { return ""; }
        }

        // Método para agregar separadores en el log
        public static void Separator(string title = null)
        {
            var sep = new string('=', 50);
            if (!string.IsNullOrEmpty(title))
                W("LOG", $"{sep} {title} {sep}");
            else
                W("LOG", sep);
        }
    }
}