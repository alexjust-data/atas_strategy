using System;
using System.IO;
using System.Reflection;

namespace MyAtas.Shared
{
    public static class BuildInfo
    {
        // "Debug 2025-09-14 19:21" | "Release 2025-09-14 19:21"
        public static string BuildTime
        {
            get
            {
                var mode =
#if DEBUG
                    "Debug ";
#else
                    "Release ";
#endif
                try
                {
                    // 1) este ensamblado (Shared)
                    string path = typeof(BuildInfo).Assembly.Location;
                    // 2) el ensamblado en ejecución (estrategia/indicador)
                    if (string.IsNullOrEmpty(path))
                        path = Assembly.GetExecutingAssembly()?.Location ?? "";
                    // 3) base dir como último recurso
                    if (string.IsNullOrEmpty(path))
                        path = AppContext.BaseDirectory ?? "";

                    DateTime dt = (!string.IsNullOrEmpty(path) && File.Exists(path))
                        ? File.GetLastWriteTime(path)
                        : DateTime.Now; // fallback estable

                    return mode + dt.ToString("yyyy-MM-dd HH:mm");
                }
                catch
                {
                    return mode + "unknown";
                }
            }
        }
    }
}