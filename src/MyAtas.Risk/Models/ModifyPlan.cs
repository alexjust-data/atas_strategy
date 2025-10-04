namespace MyAtas.Risk.Models;

public enum ModifyActionKind
{
    None,
    MoveStopTo,          // mover SL a nuevo precio (p.ej. BE)
    RebuildOco,          // cancelar grupo (TP+SL) y recrearlo
    AddOrReplaceTargets, // (futuro) cambiar TPs
}

public record ModifyAction(ModifyActionKind Kind, decimal? Price = null, int? Quantity = null, string? GroupId = null);

public record ModifyPlan(
    string Reason,
    IReadOnlyList<ModifyAction> Actions
);