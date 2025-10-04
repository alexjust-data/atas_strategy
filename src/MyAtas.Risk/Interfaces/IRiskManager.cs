namespace MyAtas.Risk.Interfaces;

using MyAtas.Risk.Models;

public interface IRiskManager
{
    RiskPlan BuildPlan(EntryContext ctx);

    // Eventos de ejecuci�n/mercado � evaluamos si toca BE/Trailing/Reconcile
    ModifyPlan? OnEvent(PositionSnapshot snapshot, RiskEvent riskEvent);

    // Utilidad: c�lculo de R m�ltiplos, conversi�n ticks, etc. (no ATAS)
    decimal ComputeR(decimal entryPx, decimal stopPx, int direction /* +1/-1 */);
}