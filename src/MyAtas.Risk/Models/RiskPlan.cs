namespace MyAtas.Risk.Models;

public enum OcoPolicy { OneOcoPerTarget, SingleOcoAll, None }

public record BracketLeg(decimal Price, int Quantity);

public record RiskPlan(
    int TotalQty,
    BracketLeg StopLoss,
    IReadOnlyList<BracketLeg> TakeProfits,
    OcoPolicy OcoPolicy,
    string Reason
);