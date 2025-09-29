namespace MyAtas.Risk.Engine;

using MyAtas.Risk.Interfaces;
using MyAtas.Risk.Models;

public sealed class RiskEngine : IRiskManager
{
    public RiskPlan BuildPlan(EntryContext ctx)
    {
        // Esqueleto: sin lógica todavía
        return new RiskPlan(
            TotalQty: 1,
            StopLoss: new BracketLeg(ctx.EntryPrice, 1),
            TakeProfits: new[] { new BracketLeg(ctx.EntryPrice, 1) },
            OcoPolicy: OcoPolicy.OneOcoPerTarget,
            Reason: "stub"
        );
    }

    public ModifyPlan? OnEvent(PositionSnapshot snapshot, RiskEvent riskEvent)
    {
        // Esqueleto: sin lógica
        return null;
    }

    public decimal ComputeR(decimal entryPx, decimal stopPx, int direction)
    {
        var r = direction > 0 ? (entryPx - stopPx) : (stopPx - entryPx);
        return r <= 0 ? 0 : r;
    }
}