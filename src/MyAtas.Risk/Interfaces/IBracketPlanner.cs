namespace MyAtas.Risk.Interfaces;

using MyAtas.Risk.Models;

public interface IBracketPlanner
{
    RiskPlan Build(EntryContext ctx, BracketConfig cfg);
}