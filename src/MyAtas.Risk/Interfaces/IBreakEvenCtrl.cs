namespace MyAtas.Risk.Interfaces;

using MyAtas.Risk.Models;

public interface IBreakEvenCtrl
{
    ModifyPlan? Evaluate(PositionSnapshot snapshot, BreakEvenConfig cfg, RiskEvent riskEvent);
}