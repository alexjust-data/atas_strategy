namespace MyAtas.Risk.Interfaces;

using MyAtas.Risk.Models;

public interface ITrailingCtrl
{
    ModifyPlan? Evaluate(PositionSnapshot snapshot, TrailingConfig cfg, RiskEvent riskEvent);
}