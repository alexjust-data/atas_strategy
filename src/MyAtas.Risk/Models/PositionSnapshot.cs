namespace MyAtas.Risk.Models;

public record ChildOrder(string OwnerPrefix, string Side, decimal Price, int Qty, string? OcoGroup);

public record PositionSnapshot(
    string Account,
    string Symbol,
    int NetQty,               // lectura de portfolio (puede ser 0 en latencia)
    int NetQtyByFills,        // suma robusta por fills
    Direction Direction,
    decimal EntryPrice,
    IReadOnlyList<ChildOrder> ActiveStops,
    IReadOnlyList<ChildOrder> ActiveTargets,
    DateTime TimeUtc
);

// Eventos de riesgo (para BE/Trailing/Reconcile)
public enum RiskEventType { EntryFill, TpTouch, TpFill, ReconcileTick, Timer }

public record RiskEvent(RiskEventType Type, int? TpIndex, decimal? Price, DateTime TimeUtc);

// Configs (del doc)
public record SizingConfig(string Mode, decimal RiskUsd, decimal RiskPct, decimal AccountEquityOverride, string TickValueOverrides);
public record BracketConfig(decimal SlOffsetTicks, decimal[] TpRMultiples, int[] Splits);
public record BreakEvenConfig(string Mode, int OffsetTicks, bool Virtual);
public record TrailingConfig(string Mode, int DistanceTicks, int ConfirmBars, decimal? AtrMult);