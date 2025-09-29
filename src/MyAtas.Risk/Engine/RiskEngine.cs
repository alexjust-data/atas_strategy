namespace MyAtas.Risk.Engine;

using MyAtas.Risk.Interfaces;
using MyAtas.Risk.Models;

public sealed class RiskEngine : IRiskManager
{
    private readonly IPositionSizer _sizer = new PositionSizer();
    private readonly IBracketPlanner _planner = new BracketPlanner();

    public RiskPlan BuildPlan(EntryContext ctx)
        => new RiskPlan(1, new BracketLeg(ctx.EntryPrice,1), new[] { new BracketLeg(ctx.EntryPrice,1) }, OcoPolicy.OneOcoPerTarget, "stub");

    // Sobrecarga conveniente para RM Manual:
    public RiskPlan BuildPlan(EntryContext ctx, SizingConfig sizing, BracketConfig brackets, out string sizingReason)
    {
        var qty = _sizer.ComputeQty(ctx, sizing, out sizingReason);
        var plan = _planner.Build(ctx, brackets);

        // Asigna cantidades a SL/TP según splits (%)
        var splits = brackets.Splits ?? Array.Empty<int>();
        if (splits.Length == 0) splits = new[] { 100 };
        var pctSum = Math.Max(1, splits.Sum());

        var tpLegs = new List<BracketLeg>();
        for (int i = 0; i < plan.TakeProfits.Count; i++)
        {
            var pct = i < splits.Length ? splits[i] : 0;
            var q = (int)Math.Max(0, Math.Round(qty * (pct / (decimal)pctSum)));
            tpLegs.Add(new BracketLeg(plan.TakeProfits[i].Price, q));
        }
        // Ajuste final: asegurar suma tpQty == qty
        var diff = qty - tpLegs.Sum(l => l.Quantity);
        if (tpLegs.Count > 0 && diff != 0)
        {
            var last = tpLegs[^1];
            tpLegs[^1] = new BracketLeg(last.Price, Math.Max(0, last.Quantity + diff));
        }

        return new RiskPlan(
            TotalQty: qty,
            StopLoss: new BracketLeg(plan.StopLoss.Price, qty),
            TakeProfits: tpLegs,
            OcoPolicy: plan.OcoPolicy,
            Reason: $"{plan.Reason}; qty={qty}"
        );
    }

    public ModifyPlan? OnEvent(PositionSnapshot snapshot, RiskEvent riskEvent) => null;

    public decimal ComputeR(decimal entryPx, decimal stopPx, int direction)
    {
        var r = direction > 0 ? (entryPx - stopPx) : (stopPx - entryPx);
        return r <= 0 ? 0 : r;
    }
}