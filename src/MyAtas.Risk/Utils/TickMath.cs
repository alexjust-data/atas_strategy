namespace MyAtas.Risk.Utils;

public static class TickMath
{
    public static decimal RoundToTick(this decimal price, decimal tick) =>
        tick <= 0 ? price : Math.Round(price / tick) * tick;
}