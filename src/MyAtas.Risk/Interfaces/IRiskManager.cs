namespace MyAtas.Risk.Interfaces;

using MyAtas.Risk.Models;

public interface IRiskManager
{
    RiskPlan BuildPlan(EntryContext ctx);

    // Eventos de ejecución/mercado ’ evaluamos si toca BE/Trailing/Reconcile
    ModifyPlan? OnEvent(PositionSnapshot snapshot, RiskEvent riskEvent);

    // Utilidad: cálculo de R múltiplos, conversión ticks, etc. (no ATAS)
    decimal ComputeR(decimal entryPx, decimal stopPx, int direction /* +1/-1 */);
}