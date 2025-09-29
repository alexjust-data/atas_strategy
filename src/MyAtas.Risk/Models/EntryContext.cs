namespace MyAtas.Risk.Models;

public enum Direction { Short = -1, Long = 1 }

public record EntryContext(
    string Account,
    string Symbol,
    Direction Direction,
    decimal EntryPrice,
    int ApproxStopTicks,       // stop aprox para sizing (si procede)
    decimal TickSize,          // p.ej. 0.25
    decimal TickValueUSD,      // p.ej. 12.5
    DateTime TimeUtc
);