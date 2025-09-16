/*
 * ATAS 468 Strategy - Dual Logging System
 * =====================================
 *
 * SISTEMA DE LOGS DUAL:
 * 1. EMERGENCY_ATAS_LOG.txt   → Persistente (todas las sesiones)
 * 2. ATAS_SESSION_LOG.txt     → Solo sesión actual (se limpia cada nueva sesión)
 * 3. ATAS_SESSION_ID.tmp      → Control PID para detectar nuevas sesiones
 *
 * DETECCIÓN DE NUEVA SESIÓN:
 * - Se considera nueva sesión cuando no existe el archivo PID o el PID es diferente
 * - En nueva sesión: se limpia ATAS_SESSION_LOG.txt y se escribe header en ambos
 *
 * ESTRUCTURA DE ARCHIVOS:
 * 06_ATAS_strategy - v2/
 * ├── logs/
 * │   ├── current/
 * │   │   ├── ATAS_SESSION_LOG.txt    ← Solo sesión actual (se limpia)
 * │   │   └── ATAS_SESSION_ID.tmp     ← PID de control
 * │   └── emergency/
 * │       └── EMERGENCY_ATAS_LOG.txt  ← Persistente (TODAS las sesiones)
 * └── src/...
 *
 * USO PRÁCTICO:
 * - Sesión actual: tail -f logs/current/ATAS_SESSION_LOG.txt
 * - Histórico: grep "pattern" logs/emergency/EMERGENCY_ATAS_LOG.txt
 * - Nuevas sesiones: grep "NEW ATAS SESSION" logs/emergency/EMERGENCY_ATAS_LOG.txt
 */

using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Linq;

namespace MyAtas.Shared
{
    public enum LogLevel
    {
        Info,
        Warning,
        Critical,
        Error
    }

    public static class DebugLog
    {
        private static readonly object _lockObj = new object();
        private static readonly string _projectRoot = @"C:\Users\AlexJ\Desktop\projects\01_atas\06_ATAS_strategy - v2";

        // Rutas de archivos del sistema dual
        private static readonly string _emergencyPath = Path.Combine(_projectRoot, "logs", "emergency", "EMERGENCY_ATAS_LOG.txt");
        private static readonly string _sessionPath = Path.Combine(_projectRoot, "logs", "current", "ATAS_SESSION_LOG.txt");
        private static readonly string _sessionIdPath = Path.Combine(_projectRoot, "logs", "current", "ATAS_SESSION_ID.tmp");

        // Estado del sistema
        private static bool _sessionInitialized = false;
        private static string _legacyLogPath = null;
        private static bool _legacyInitialized = false;

        /// <summary>
        /// Inicializa la sesión detectando si es nueva o continuación
        /// </summary>
        private static void EnsureSessionInitialized()
        {
            if (_sessionInitialized) return;

            lock (_lockObj)
            {
                if (_sessionInitialized) return;

                try
                {
                    var currentPid = Process.GetCurrentProcess().Id;
                    bool isNewSession = false;

                    // Detectar si es nueva sesión
                    if (!File.Exists(_sessionIdPath))
                    {
                        isNewSession = true;
                    }
                    else
                    {
                        try
                        {
                            var storedPid = File.ReadAllText(_sessionIdPath).Trim();
                            if (!int.TryParse(storedPid, out int pid) || pid != currentPid)
                            {
                                isNewSession = true;
                            }
                        }
                        catch
                        {
                            isNewSession = true;
                        }
                    }

                    if (isNewSession)
                    {
                        InitializeNewSession(currentPid);
                    }

                    _sessionInitialized = true;
                }
                catch (Exception ex)
                {
                    // Fallback: escribir al menos al console
                    Console.WriteLine($"*** LOG INITIALIZATION FAILED: {ex.Message} ***");
                    _sessionInitialized = true; // Evitar loops infinitos
                }
            }
        }

        /// <summary>
        /// Inicializa una nueva sesión de ATAS
        /// </summary>
        private static void InitializeNewSession(int currentPid)
        {
            try
            {
                // Actualizar archivo de control de sesión
                File.WriteAllText(_sessionIdPath, currentPid.ToString());

                // Limpiar log de sesión (nueva sesión = archivo limpio)
                File.WriteAllText(_sessionPath, "");

                // Crear header de nueva sesión
                var sessionHeader = CreateSessionHeader(currentPid);

                // Escribir header en ambos archivos
                AppendToFile(_emergencyPath, sessionHeader);
                AppendToFile(_sessionPath, sessionHeader);

                // También al console para visibilidad inmediata
                Console.WriteLine($"*** NEW ATAS SESSION DETECTED (PID: {currentPid}) ***");
                Console.WriteLine($"*** Session Log: {_sessionPath} ***");
                Console.WriteLine($"*** Emergency Log: {_emergencyPath} ***");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"*** NEW SESSION INIT FAILED: {ex.Message} ***");
            }
        }

        /// <summary>
        /// Crea el header para una nueva sesión
        /// </summary>
        private static string CreateSessionHeader(int pid)
        {
            var now = DateTime.Now;
            var process = Process.GetCurrentProcess();

            return $@"
=== NEW ATAS SESSION STARTED at {now:yyyy-MM-dd HH:mm:ss} (PID: {pid}) ===
Process: {process.ProcessName}
Start Time: {process.StartTime:yyyy-MM-dd HH:mm:ss}
Working Directory: {Environment.CurrentDirectory}
Base Directory: {AppDomain.CurrentDomain.BaseDirectory}
User: {Environment.UserName}
Machine: {Environment.MachineName}
CLR Version: {Environment.Version}
================================================================================

";
        }

        /// <summary>
        /// Escribe contenido a un archivo de forma segura
        /// </summary>
        private static void AppendToFile(string path, string content)
        {
            try
            {
                File.AppendAllText(path, content);
            }
            catch
            {
                // Fail silently - el sistema dual asegura que al menos un archivo funcione
            }
        }

        /// <summary>
        /// Inicializa el sistema de logs legacy (archivo con timestamp)
        /// </summary>
        private static void InitializeLegacyLogFile()
        {
            if (_legacyInitialized) return;

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var logDir = Path.Combine(baseDir, "ATAS_Strategy_Logs");

                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _legacyLogPath = Path.Combine(logDir, $"ATAS_468_Debug_{timestamp}.log");

                // Escribir header inicial
                var header = $@"=== ATAS 468 Strategy Debug Log ===
Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Base Directory: {baseDir}
Log Directory: {logDir}
Full Log Path: {_legacyLogPath}
Current Working Dir: {Environment.CurrentDirectory}
Process Name: {Process.GetCurrentProcess().ProcessName}
=====================================

";
                File.WriteAllText(_legacyLogPath, header);

                _legacyInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"*** LEGACY LOG FILE CREATION FAILED: {ex.Message} ***");
                _legacyInitialized = true; // Evitar intentos repetidos
            }
        }

        /// <summary>
        /// Método principal de logging - escribe en todos los sistemas con validación automática de sesión
        /// </summary>
        public static void WriteLog(string category, string message, LogLevel level = LogLevel.Info)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logLine = $"[{timestamp}] {level.ToString().ToUpper().PadRight(8)} {category}: {message}";

            try
            {
                // 1. Console y Debug (inmediato)
                Console.WriteLine(logLine);
                System.Diagnostics.Debug.WriteLine(logLine);

                // 2. Validación automática de sesión antes de escribir
                if (!ValidateSessionIntegrity())
                {
                    var warningLine = $"[{timestamp}] WARNING  SESSION: Session validation failed - reinitializing";
                    Console.WriteLine(warningLine);
                    EnsureSessionInitialized(); // Reinicializar si falla validación
                }

                // 3. Sistema dual (sesión + persistente)
                EnsureSessionInitialized();
                AppendToFile(_emergencyPath, logLine + Environment.NewLine);
                AppendToFile(_sessionPath, logLine + Environment.NewLine);

                // 4. Sistema legacy (con timestamp en nombre)
                lock (_lockObj)
                {
                    InitializeLegacyLogFile();
                    if (_legacyLogPath != null)
                    {
                        AppendToFile(_legacyLogPath, logLine + Environment.NewLine);
                    }
                }
            }
            catch
            {
                // En caso de fallo total, al menos mantener console
                try
                {
                    Console.WriteLine($"[FALLBACK] {logLine}");
                }
                catch { /* no-op */ }
            }
        }

        /// <summary>
        /// Logging de advertencias (nivel Warning)
        /// </summary>
        public static void W(string tag, string msg)
        {
            WriteLog(tag, msg, LogLevel.Warning);
        }

        /// <summary>
        /// Logging crítico (nivel Critical) - siempre se guarda
        /// </summary>
        public static void Critical(string tag, string msg)
        {
            WriteLog($"CRITICAL-{tag}", msg, LogLevel.Critical);
        }

        /// <summary>
        /// Logging de errores (nivel Error)
        /// </summary>
        public static void Error(string tag, string msg)
        {
            WriteLog(tag, msg, LogLevel.Error);
        }

        /// <summary>
        /// Logging informativo (nivel Info)
        /// </summary>
        public static void Info(string tag, string msg)
        {
            WriteLog(tag, msg, LogLevel.Info);
        }

        /// <summary>
        /// Obtiene la ruta del log de sesión actual
        /// </summary>
        public static string GetSessionLogPath()
        {
            return _sessionPath;
        }

        /// <summary>
        /// Obtiene la ruta del log persistente
        /// </summary>
        public static string GetEmergencyLogPath()
        {
            return _emergencyPath;
        }

        /// <summary>
        /// Obtiene la ruta del log legacy (con timestamp)
        /// </summary>
        public static string GetLogPath()
        {
            lock (_lockObj)
            {
                InitializeLegacyLogFile();
                return _legacyLogPath ?? "Legacy log file not available";
            }
        }

        /// <summary>
        /// Hint del directorio base
        /// </summary>
        public static string PathHint()
        {
            try { return AppDomain.CurrentDomain.BaseDirectory ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// Agrega separadores decorativos en el log
        /// </summary>
        public static void Separator(string title = null)
        {
            var sep = new string('=', 80);
            if (!string.IsNullOrEmpty(title))
            {
                var paddedTitle = $" {title} ";
                var totalPadding = sep.Length - paddedTitle.Length;
                var leftPadding = totalPadding / 2;
                var rightPadding = totalPadding - leftPadding;
                var decoratedTitle = new string('=', leftPadding) + paddedTitle + new string('=', rightPadding);
                WriteLog("LOG", decoratedTitle, LogLevel.Info);
            }
            else
            {
                WriteLog("LOG", sep, LogLevel.Info);
            }
        }

        /// <summary>
        /// Valida que la sesión guardada corresponde al proceso actual
        /// </summary>
        private static bool ValidateSessionIntegrity()
        {
            try
            {
                // Si no estamos inicializados, no podemos validar
                if (!_sessionInitialized)
                    return true; // Permitir inicialización

                // Verificar que el archivo de sesión existe
                if (!File.Exists(_sessionIdPath))
                {
                    Console.WriteLine($"*** SESSION VALIDATION FAILED: Session ID file missing");
                    return false;
                }

                // Leer PID almacenado
                var storedPidText = File.ReadAllText(_sessionIdPath).Trim();
                if (!int.TryParse(storedPidText, out int storedPid))
                {
                    Console.WriteLine($"*** SESSION VALIDATION FAILED: Invalid PID format: '{storedPidText}'");
                    return false;
                }

                // Verificar que el PID almacenado corresponde al proceso actual
                var currentPid = Process.GetCurrentProcess().Id;
                if (storedPid != currentPid)
                {
                    Console.WriteLine($"*** SESSION VALIDATION FAILED: PID mismatch - Stored: {storedPid}, Current: {currentPid}");
                    return false;
                }

                // Verificar que el proceso con ese PID realmente existe y es ATAS
                try
                {
                    var process = Process.GetProcessById(storedPid);
                    var processName = process.ProcessName;

                    // Validar que es un proceso ATAS válido
                    if (!IsAtasProcess(processName, process))
                    {
                        Console.WriteLine($"*** SESSION VALIDATION FAILED: Process {storedPid} is not ATAS - Found: {processName}");
                        return false;
                    }

                    // Validación exitosa
                    return true;
                }
                catch (ArgumentException)
                {
                    Console.WriteLine($"*** SESSION VALIDATION FAILED: Process {storedPid} no longer exists");
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"*** SESSION VALIDATION FAILED: Error checking process {storedPid}: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"*** SESSION VALIDATION ERROR: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica si un proceso es realmente ATAS
        /// </summary>
        private static bool IsAtasProcess(string processName, Process process)
        {
            try
            {
                // Nombres conocidos de procesos ATAS
                var validAtasNames = new[] { "OFT.Platform", "ATAS", "ATAS.Platform", "AtasPlatform" };

                if (validAtasNames.Any(name => processName.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    return true;

                // Verificación adicional por ruta del ejecutable
                try
                {
                    var mainModule = process.MainModule;
                    if (mainModule?.FileName?.Contains("ATAS", StringComparison.OrdinalIgnoreCase) == true)
                        return true;
                }
                catch { /* MainModule puede fallar por permisos */ }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Información del sistema de logs (para debug)
        /// </summary>
        public static void LogSystemInfo()
        {
            WriteLog("SYSTEM", $"Session Log: {_sessionPath}", LogLevel.Info);
            WriteLog("SYSTEM", $"Emergency Log: {_emergencyPath}", LogLevel.Info);
            WriteLog("SYSTEM", $"Session ID File: {_sessionIdPath}", LogLevel.Info);
            WriteLog("SYSTEM", $"Legacy Log: {GetLogPath()}", LogLevel.Info);

            // Validación en tiempo real
            var isValid = ValidateSessionIntegrity();
            WriteLog("SYSTEM", $"Session Integrity: {(isValid ? "VALID" : "INVALID")}", isValid ? LogLevel.Info : LogLevel.Critical);
        }

        /// <summary>
        /// Fuerza validación manual de la sesión (para testing)
        /// </summary>
        public static bool ValidateCurrentSession()
        {
            return ValidateSessionIntegrity();
        }
    }
}