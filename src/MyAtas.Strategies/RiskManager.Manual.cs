using System;
using System.ComponentModel;
using System.Linq;
using ATAS.Strategies.Chart;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Shared;
using MyAtas.Risk.Engine;
using MyAtas.Risk.Models;

namespace MyAtas.Strategies
{
    // Nota: esqueleto "safe". No env�a ni cancela �rdenes.
    public class RiskManagerManualStrategy : ChartStrategy
    {
        // =================== Activation ===================
        [Category("Activation"), DisplayName("Manage manual entries")]
        public bool ManageManualEntries { get; set; } = true;

        [Category("Activation"), DisplayName("Ignore orders with prefix")]
        public string IgnorePrefix { get; set; } = "468"; // no interferir con la 468

        [Category("Activation"), DisplayName("Owner prefix (this strategy)")]
        public string OwnerPrefix { get; set; } = "RM:";

        // =================== Position Sizing ===================
        public enum RmSizingMode { Manual, FixedRiskUSD, PercentAccount }

        [Category("Position Sizing"), DisplayName("Mode")]
        public RmSizingMode SizingMode { get; set; } = RmSizingMode.Manual;

        [Category("Position Sizing"), DisplayName("Risk per trade (USD)")]
        public decimal RiskPerTradeUsd { get; set; } = 100m;

        [Category("Position Sizing"), DisplayName("Risk % of account")]
        public decimal RiskPercentOfAccount { get; set; } = 0.5m;

        [Category("Position Sizing"), DisplayName("Tick value overrides (SYM=V;...)")]
        public string TickValueOverrides { get; set; } = "MNQ=0.5;NQ=5;MES=1.25;ES=12.5";

        [Category("Position Sizing"), DisplayName("Default stop (ticks)")]
        public int DefaultStopTicks { get; set; } = 12;

        [Category("Position Sizing"), DisplayName("Fallback tick size")]
        public decimal FallbackTickSize { get; set; } = 0.25m;

        [Category("Position Sizing"), DisplayName("Fallback tick value (USD)")]
        public decimal FallbackTickValueUsd { get; set; } = 12.5m;

        // =================== Breakeven ===================
        public enum RmBeMode { Off, OnTP1Touch, OnTP1Fill }

        [Category("Breakeven"), DisplayName("Mode")]
        public RmBeMode BreakEvenMode { get; set; } = RmBeMode.OnTP1Touch;

        [Category("Breakeven"), DisplayName("BE offset (ticks)")]
        public int BeOffsetTicks { get; set; } = 4;

        [Category("Breakeven"), DisplayName("Virtual BE")]
        public bool VirtualBreakEven { get; set; } = false;

        // =================== Trailing (placeholder) ===================
        public enum RmTrailMode { Off, BarByBar, TpToTp }

        [Category("Trailing"), DisplayName("Mode")]
        public RmTrailMode TrailingMode { get; set; } = RmTrailMode.Off;

        [Category("Trailing"), DisplayName("Distance (ticks)")]
        public int TrailDistanceTicks { get; set; } = 8;

        [Category("Trailing"), DisplayName("Confirm bars")]
        public int TrailConfirmBars { get; set; } = 1;

        // =================== Diagnostics ===================
        [Category("Diagnostics"), DisplayName("Enable logging")]
        public bool EnableLogging { get; set; } = true;

        // =================== Internal Helpers ===================
        private int _lastSeenBar = -1;
        private readonly RiskEngine _engine = new RiskEngine();

        private bool IsFirstTickOf(int currentBar)
        {
            if (currentBar != _lastSeenBar) { _lastSeenBar = currentBar; return true; }
            return false;
        }


        protected override void OnCalculate(int bar, decimal value)
        {
            // Heartbeat y "observaci�n" pasiva. No toca �rdenes.
            if (!EnableLogging) return;

            try
            {
                if (IsFirstTickOf(bar))
                {
                    DebugLog.W("RM/HEARTBEAT", $"bar={bar} t={GetCandle(bar).Time:HH:mm:ss} mode={SizingMode} be={BreakEvenMode} trail={TrailingMode}");
                }
            }
            catch { /* noop */ }

            // En fases posteriores: aqu� detectaremos una "entrada manual" y
            // pediremos un plan a la librer�a. Hoy solo observamos.
        }

        protected override void OnOrderChanged(Order order)
        {
            try
            {
                if (!ManageManualEntries) return;

                var comment = order?.Comment ?? "";
                var st = order.Status();
                var state = order.State;

                if (EnableLogging)
                {
                    DebugLog.W("RM/ORD", $"comment={comment} state={state} status={st}");
                }

                // 1) Ignora 468 y también ignora órdenes RM (para no re-enganchar)
                if (comment.StartsWith("468")) return;
                if (comment.StartsWith(OwnerPrefix)) return;

                // 2) Detecta una entrada manual "llena" (simplificado: status Filled/PartlyFilled)
                if (!(st == OrderStatus.Filled || st == OrderStatus.PartlyFilled)) return;

                // 3) Heurística: si el 'side' es Buy/Sell y no hay hijos RM, lo tratamos como entrada manual
                var sideProp = order.GetType().GetProperty("Direction");
                var side = sideProp?.GetValue(order)?.ToString() ?? "";
                int dir = side.Contains("Buy", StringComparison.OrdinalIgnoreCase) ? +1
                       : side.Contains("Sell", StringComparison.OrdinalIgnoreCase) ? -1 : 0;
                if (dir == 0) return;

                // 4) Construir EntryContext (usa fallbacks si no hay datos del instrumento)
                var tickSize = FallbackTickSize;
                try { tickSize = Security?.TickSize ?? FallbackTickSize; } catch { }
                var tickValue = FallbackTickValueUsd; // resolvemos override más abajo vía engine

                // Precio de entrada (average fill)
                decimal entryPx = 0m;
                foreach (var name in new[] { "AvgPrice", "AveragePrice", "AvgFillPrice", "Price" })
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Convert.ToDecimal(p.GetValue(order));
                    if (v > 0m) { entryPx = v; break; }
                }
                if (entryPx <= 0m) return; // no hay precio, aborta silenciosamente

                var ctx = new EntryContext(
                    Account: Portfolio?.ToString() ?? "DEFAULT",
                    Symbol: Security?.ToString() ?? "",
                    Direction: dir > 0 ? Direction.Long : Direction.Short,
                    EntryPrice: entryPx,
                    ApproxStopTicks: Math.Max(1, DefaultStopTicks),
                    TickSize: tickSize,
                    TickValueUSD: tickValue,
                    TimeUtc: DateTime.UtcNow
                );

                // 5) Config del sizer y brackets (desde UI)
                var sizingCfg = new SizingConfig(
                    Mode: SizingMode.ToString(),
                    ManualQty: 1,                      // qty manual de esta strategy
                    RiskUsd: RiskPerTradeUsd,
                    RiskPct: RiskPercentOfAccount,
                    AccountEquityOverride: 0m,                // (conectaremos más adelante)
                    TickValueOverrides: TickValueOverrides,
                    UnderfundedPolicy: UnderfundedPolicy.Min1
                );
                var bracketCfg = new BracketConfig(
                    StopTicks: DefaultStopTicks,
                    SlOffsetTicks: 0m,
                    TpRMultiples: new decimal[] { 1.0m, 2.0m }, // UI futura: editable
                    Splits: new int[] { 50, 50 }
                );

                // 6) Construir plan con el motor
                var plan = _engine.BuildPlan(ctx, sizingCfg, bracketCfg, out var szReason);

                if (EnableLogging)
                {
                    DebugLog.W("RM/PLAN", $"qty={plan.TotalQty} | SL@{plan.StopLoss.Price:F2} | " +
                                          $"TPs=[{string.Join(",", plan.TakeProfits.Select(tp => $"{tp.Price:F2}x{tp.Quantity}"))}] | " +
                                          $"reason=({szReason}) {plan.Reason}");
                }

                // 7) Adjuntar brackets (SOLO si qty>0 y no hay hijos 468/RM activos)
                if (plan.TotalQty <= 0)
                {
                    if (EnableLogging) DebugLog.W("RM/PLAN", "qty<=0 → no adjunto (policy/underfunded)");
                    return;
                }

                // 8) Crear OCO y publicar STOP + LIMIT(s) con prefijo RM
                var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                var oco = Guid.NewGuid().ToString("N");
                SubmitRmStop(oco, coverSide, plan.TotalQty, plan.StopLoss.Price);
                foreach (var tp in plan.TakeProfits.Where(t => t.Quantity > 0))
                    SubmitRmLimit(oco, coverSide, tp.Quantity, tp.Price);

            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"OnOrderChanged EX: {ex.Message}");
            }
        }

        // ====================== RM ORDER SUBMISSION HELPERS ======================
        private void SubmitRmStop(string oco, OrderDirections side, int qty, decimal triggerPx)
        {
            var comment = $"{OwnerPrefix}SL:{Guid.NewGuid():N}";
            try
            {
                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Stop,
                    TriggerPrice = triggerPx,
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"STOP submitted: {side} {qty} @{triggerPx:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmStop EX: {ex.Message}");
            }
        }

        private void SubmitRmLimit(string oco, OrderDirections side, int qty, decimal price)
        {
            var comment = $"{OwnerPrefix}TP:{Guid.NewGuid():N}";
            try
            {
                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Limit,
                    Price = price,
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"LIMIT submitted: {side} {qty} @{price:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmLimit EX: {ex.Message}");
            }
        }

    }
}