namespace MyAtas.Risk.Engine;

using MyAtas.Risk.Interfaces;
using MyAtas.Risk.Models;
using MyAtas.Risk.Utils;

public sealed class BracketPlanner : IBracketPlanner
{
    public RiskPlan Build(EntryContext ctx, BracketConfig cfg)
    {
        var dir = (int)ctx.Direction;
        var ts  = ctx.TickSize <= 0 ? 0.25m : ctx.TickSize;

        // 1) STOP base por ticks (aprox) ± offsets
        var stopTicks = Math.Max(1, cfg.StopTicks);
        var slPx = ctx.EntryPrice + (dir > 0 ? -stopTicks * ts : stopTicks * ts);
        slPx = (slPx).RoundToTick(ts);

        // 2) Distancia R
        var r = dir > 0 ? (ctx.EntryPrice - slPx) : (slPx - ctx.EntryPrice);
        if (r <= 0) r = ts; // fallback mínimo

        // 3) TPs por múltiplos de R
        var tps = new List<BracketLeg>();
        var multiples = cfg.TpRMultiples ?? Array.Empty<decimal>();
        foreach (var m in multiples)
        {
            var target = ctx.EntryPrice + (dir > 0 ? (m * r) : -(m * r));
            tps.Add(new BracketLeg(target.RoundToTick(ts), 0)); // qty la fijamos en el split
        }

        // 4) Splits: interpretamos como % (suma 100)
        var splits = cfg.Splits ?? Array.Empty<int>();
        var pctSum = splits.Sum();
        if (pctSum <= 0) splits = new[] { 100 }; // todo al TP1 si no viene
        pctSum = Math.Max(1, splits.Sum());
        // La qty total la aportará el sizer: la dejamos en 0 aquí y que el adapter asigne
        // o si quieres, ponemos placeholders que luego el adapter rellena
        var plan = new RiskPlan(
            TotalQty: 0,
            StopLoss: new BracketLeg(slPx, 0),
            TakeProfits: tps,
            OcoPolicy: OcoPolicy.OneOcoPerTarget,
            Reason: $"SL={stopTicks}ticks; R={r:0.#####}"
        );

        return plan;
    }
}