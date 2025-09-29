namespace MyAtas.Risk.Engine;

using MyAtas.Risk.Interfaces;
using MyAtas.Risk.Models;
using MyAtas.Risk.Utils;

public sealed class PositionSizer : IPositionSizer
{
    public int ComputeQty(EntryContext ctx, SizingConfig cfg, out string reason)
    {
        reason = "Manual";
        var mode = (cfg?.Mode ?? "Manual").Trim();

        // TickValue: respeta override por símbolo si viene; si no, usa el del contexto
        var tv = TvOverrides.Resolve(ctx.Symbol, cfg.TickValueOverrides, ctx.TickValueUSD <= 0 ? 0.5m : ctx.TickValueUSD);
        var ts = ctx.TickSize <= 0 ? 0.25m : ctx.TickSize;

        if (mode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            var q = Math.Max(1, cfg.ManualQty);
            reason = $"Manual qty={q}";
            return q;
        }

        var stopTicks = Math.Max(1, ctx.ApproxStopTicks);
        var riskPerContract = stopTicks * tv; // USD por contrato dada la distancia de stop
        if (riskPerContract <= 0)
        {
            reason = $"Invalid riskPerContract (stopTicks={stopTicks}, tv={tv}) → fallback qty=1";
            return 1;
        }

        if (mode.Equals("FixedRiskUSD", StringComparison.OrdinalIgnoreCase))
        {
            var q = (int)Math.Floor(cfg.RiskUsd / riskPerContract);
            if (q < 1)
            {
                if (cfg.UnderfundedPolicy == UnderfundedPolicy.Abort)
                {
                    reason = $"Underfunded: riskUsd={cfg.RiskUsd} < riskPerContract={riskPerContract} → ABORT";
                    return 0;
                }
                q = 1;
                reason = $"Underfunded: riskUsd={cfg.RiskUsd} < {riskPerContract} → qty=1";
            }
            else
            {
                reason = $"FixedRiskUSD: {cfg.RiskUsd} / {riskPerContract} => qty={q}";
            }
            return q;
        }

        if (mode.Equals("PercentAccount", StringComparison.OrdinalIgnoreCase))
        {
            var eq = cfg.AccountEquityOverride > 0 ? cfg.AccountEquityOverride : 0m; // future: leer equity real si conectamos
            if (eq <= 0)
            {
                // Si no hay equity real, usa RiskUsd como fallback si lo configuraste, o manual=1
                var q = cfg.RiskUsd > 0 ? (int)Math.Floor(cfg.RiskUsd / riskPerContract) : 1;
                if (q < 1 && cfg.UnderfundedPolicy == UnderfundedPolicy.Min1) q = 1;
                reason = $"No equity → fallback {(cfg.RiskUsd > 0 ? "RiskUsd" : "qty=1")}: qty={q}";
                return Math.Max(0, q);
            }
            var riskUsd = eq * (cfg.RiskPct / 100m);
            var qty = (int)Math.Floor(riskUsd / riskPerContract);
            if (qty < 1)
            {
                if (cfg.UnderfundedPolicy == UnderfundedPolicy.Abort)
                {
                    reason = $"Underfunded %: {riskUsd} < {riskPerContract} → ABORT";
                    return 0;
                }
                reason = $"Underfunded %: {riskUsd} < {riskPerContract} → qty=1";
                return 1;
            }
            reason = $"%Account: equity={eq} pct={cfg.RiskPct}% riskUsd={riskUsd} / {riskPerContract} → qty={qty}";
            return qty;
        }

        reason = $"Unknown mode='{mode}' → qty=1";
        return 1;
    }
}