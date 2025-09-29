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

public enum UnderfundedPolicy { Min1, Abort }

// Configs (del doc)
public record SizingConfig(
    string Mode,                    // "Manual" | "FixedRiskUSD" | "PercentAccount"
    int ManualQty,                  // NUEVO: qty fijo cuando Mode=Manual
    decimal RiskUsd,
    decimal RiskPct,
    decimal AccountEquityOverride,
    string TickValueOverrides,      // "MNQ=0.5;NQ=5;ES=12.5"
    UnderfundedPolicy UnderfundedPolicy = UnderfundedPolicy.Min1
);

public record BracketConfig(
    int StopTicks,                  // stop base en ticks (aprox)
    decimal SlOffsetTicks,          // offset adicional (puede ser 0)
    decimal[] TpRMultiples,         // p.ej. [1.0m, 2.0m]
    int[] Splits                    // p.ej. [50, 50] (%) o cantidades absolutas (ver abajo)
);

public record BreakEvenConfig(string Mode, int OffsetTicks, bool Virtual);
public record TrailingConfig(string Mode, int DistanceTicks, int ConfirmBars, decimal? AtrMult);