using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ATAS.Indicators;
using MyAtas.Shared;

public static class IndicatorsAssemblyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var where = asm.Location;
            var ver = asm.GetName().Version?.ToString();
            var indicators = asm.GetTypes()
                .Where(t => !t.IsAbstract && typeof(Indicator).IsAssignableFrom(t))
                .Select(t => t.FullName)
                .ToArray();

            DebugLog.W("468/IND-ASM", $"Loaded DLL={where} v={ver} | Indicators found={indicators.Length}");
            foreach (var i in indicators)
                DebugLog.W("468/IND-ASM", $"Indicator type: {i}");
        }
        catch (Exception ex)
        {
            DebugLog.W("468/IND-ASM", "ModuleInitializer EX: " + ex);
        }
    }
}