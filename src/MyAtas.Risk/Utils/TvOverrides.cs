namespace MyAtas.Risk.Utils;

public static class TvOverrides
{
    // Parse "MNQ=0.5;NQ=5;ES=12.5"
    public static decimal Resolve(string symbol, string overridesStr, decimal fallback = 0.5m)
    {
        if (string.IsNullOrWhiteSpace(overridesStr)) return fallback;
        foreach (var kv in overridesStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = kv.Split('=', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2) continue;
            if (symbol.IndexOf(p[0], StringComparison.OrdinalIgnoreCase) >= 0 &&
                decimal.TryParse(p[1], out var v) && v > 0) return v;
        }
        return fallback;
    }
}