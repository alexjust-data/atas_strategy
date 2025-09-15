using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ATAS.Strategies.Chart;
using MyAtas.Shared;

public static class StrategiesAssemblyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var where = asm.Location;
            var ver = asm.GetName().Version?.ToString();
            var strategies = asm.GetTypes()
                .Where(t => !t.IsAbstract && typeof(ChartStrategy).IsAssignableFrom(t))
                .Select(t => t.FullName)
                .ToArray();

            DebugLog.W("468/STR-ASM", $"Loaded DLL={where} v={ver} | Strategies found={strategies.Length}");
            foreach (var s in strategies)
                DebugLog.W("468/STR-ASM", $"Strategy type: {s}");
        }
        catch (Exception ex)
        {
            DebugLog.W("468/STR-ASM", "ModuleInitializer EX: " + ex);
        }
    }
}