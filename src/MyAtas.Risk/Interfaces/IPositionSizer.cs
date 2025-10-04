namespace MyAtas.Risk.Interfaces;

using MyAtas.Risk.Models;

public interface IPositionSizer
{
    int ComputeQty(EntryContext ctx, SizingConfig cfg, out string reason);
}