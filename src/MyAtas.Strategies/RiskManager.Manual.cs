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
    // Enums externos para serialización correcta de ATAS
    public enum RmSizingMode { Manual, FixedRiskUSD, PercentAccount }
    public enum RmBeMode { Off, OnTP1Touch, OnTP1Fill }
    public enum RmTrailMode { Off, BarByBar, TpToTp }

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

        [Category("Position Sizing"), DisplayName("Mode")]
        public RmSizingMode SizingMode { get; set; } = RmSizingMode.Manual;

        [Category("Position Sizing"), DisplayName("Manual qty")]
        public int ManualQty { get; set; } = 1;

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

        [Category("Position Sizing"), DisplayName("Min qty")]
        public int MinQty { get; set; } = 1;

        [Category("Position Sizing"), DisplayName("Max qty")]
        public int MaxQty { get; set; } = 1000;

        [Category("Position Sizing"), DisplayName("Underfunded policy")]
        public MyAtas.Risk.Models.UnderfundedPolicy Underfunded { get; set; } =
            MyAtas.Risk.Models.UnderfundedPolicy.Min1;

        // =================== Stops & TPs ===================
        [Category("Stops & TPs"), DisplayName("Preset TPs (1..3)")]
        public int PresetTPs { get; set; } = 2; // 1..3

        [Category("Stops & TPs"), DisplayName("TP1 R multiple")]
        public decimal TP1R { get; set; } = 1.0m;

        [Category("Stops & TPs"), DisplayName("TP2 R multiple")]
        public decimal TP2R { get; set; } = 2.0m;

        [Category("Stops & TPs"), DisplayName("TP3 R multiple")]
        public decimal TP3R { get; set; } = 3.0m;

        [Category("Stops & TPs"), DisplayName("TP1 split (%)")]
        public int TP1pctunit { get; set; } = 50;

        [Category("Stops & TPs"), DisplayName("TP2 split (%)")]
        public int TP2pctunit { get; set; } = 50;

        [Category("Stops & TPs"), DisplayName("TP3 split (%)")]
        public int TP3pctunit { get; set; } = 0;

        // =================== Breakeven ===================
        [Category("Breakeven"), DisplayName("Mode")]
        public RmBeMode BreakEvenMode { get; set; } = RmBeMode.OnTP1Touch;

        [Category("Breakeven"), DisplayName("BE offset (ticks)")]
        public int BeOffsetTicks { get; set; } = 4;

        [Category("Breakeven"), DisplayName("Virtual BE")]
        public bool VirtualBreakEven { get; set; } = false;

        // =================== Trailing (placeholder) ===================
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
        private RiskEngine _engine;

        // --- Estado de net para detectar 0→≠0 (entrada) ---
        private int _prevNet = 0;
        private bool _pendingAttach = false;
        private decimal _pendingEntryPrice = 0m;
        private DateTime _pendingSince = DateTime.MinValue;
        private readonly int _attachThrottleMs = 500; // throttle para fallback
        private System.Collections.Generic.List<Order> _liveOrders = new();

        // Helper property for compatibility
        private bool IsActivated => ManageManualEntries;

        // Constructor explícito para evitar excepciones durante carga ATAS
        public RiskManagerManualStrategy()
        {
            try
            {
                // No inicializar aquí para evitar problemas de carga
                _engine = null;
            }
            catch
            {
                // Constructor sin excepciones para ATAS
            }
        }

        private RiskEngine GetEngine()
        {
            if (_engine == null)
            {
                try
                {
                    _engine = new RiskEngine();
                }
                catch
                {
                    // Fallback seguro
                    _engine = null;
                }
            }
            return _engine;
        }

        private bool IsFirstTickOf(int currentBar)
        {
            if (currentBar != _lastSeenBar) { _lastSeenBar = currentBar; return true; }
            return false;
        }

        // Lee net de forma robusta SIN GetNetPosition()
        private int ReadNetPosition()
        {
            try
            {
                if (Portfolio == null || Security == null)
                    return 0;

                // 1) Intento directo: Portfolio.GetPosition(Security)
                try
                {
                    var getPos = Portfolio.GetType().GetMethod("GetPosition", new[] { Security.GetType() });
                    if (getPos != null)
                    {
                        var pos = getPos.Invoke(Portfolio, new object[] { Security });
                        if (pos != null)
                        {
                            foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                            {
                                var p = pos.GetType().GetProperty(name);
                                if (p == null) continue;
                                var v = p.GetValue(pos);
                                if (v != null) return Convert.ToInt32(v);
                            }
                        }
                    }
                }
                catch { /* seguir al fallback */ }

                // 2) Fallback: iterar Portfolio.Positions
                try
                {
                    var positionsProp = Portfolio.GetType().GetProperty("Positions");
                    var positions = positionsProp?.GetValue(Portfolio) as System.Collections.IEnumerable;
                    if (positions != null)
                    {
                        foreach (var pos in positions)
                        {
                            var secProp = pos.GetType().GetProperty("Security");
                            var secStr = secProp?.GetValue(pos)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(secStr) && (Security?.ToString() ?? "") == secStr)
                            {
                                foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                                {
                                    var p = pos.GetType().GetProperty(name);
                                    if (p == null) continue;
                                    return Convert.ToInt32(p.GetValue(pos));
                                }
                            }
                        }
                    }
                }
                catch { /* devolver 0 */ }
            }
            catch { }
            return 0;
        }

        private decimal ExtractAvgFillPrice(Order order, int fallbackBar)
        {
            try
            {
                // Precio de entrada (average fill)
                foreach (var name in new[] { "AvgPrice", "AveragePrice", "AvgFillPrice", "Price" })
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Convert.ToDecimal(p.GetValue(order));
                    if (v > 0m) return v;
                }
                // Fallback: precio actual de la barra
                return GetCandle(Math.Max(0, fallbackBar)).Close;
            }
            catch
            {
                return GetCandle(Math.Max(0, CurrentBar)).Close;
            }
        }


        protected override void OnCalculate(int bar, decimal value)
        {
            if (!IsActivated) return;

            if (EnableLogging && IsFirstTickOf(bar))
                DebugLog.W("RM/HEARTBEAT", $"bar={bar} t={GetCandle(bar).Time:HH:mm:ss} mode={SizingMode} be={BreakEvenMode} trail={TrailingMode}");

            // Fallback por barra si quedó pendiente
            if (_pendingAttach && (DateTime.UtcNow - _pendingSince).TotalMilliseconds >= _attachThrottleMs)
                TryAttachBracketsNow();

            // Actualiza snapshot de net
            try { _prevNet = ReadNetPosition(); } catch { }
        }

        protected override void OnOrderChanged(Order order)
        {
            try
            {
                if (!IsActivated || !ManageManualEntries) return;

                var comment = order?.Comment ?? "";
                var st = order.Status();

                // Ignora la 468 y mis propias RM
                if (comment.StartsWith(IgnorePrefix)) return;
                if (comment.StartsWith(OwnerPrefix)) return;

                // Ignora cierres explícitos
                if (comment.IndexOf("Close position", StringComparison.OrdinalIgnoreCase) >= 0) return;

                // Sólo nos interesan fills/parciales
                if (!(st == OrderStatus.Filled || st == OrderStatus.PartlyFilled)) return;

                // Marca attach pendiente; la dirección la deduciremos con el net por barra o ahora mismo
                _pendingAttach = true;
                _pendingEntryPrice = ExtractAvgFillPrice(order, CurrentBar);
                _pendingSince = DateTime.UtcNow;

                if (EnableLogging)
                    DebugLog.W("RM/ORD", $"Manual order filled → pendingAttach=true entryPx={_pendingEntryPrice:F2}");

                // Intento inmediato
                TryAttachBracketsNow();
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"OnOrderChanged EX: {ex.Message}");
            }
        }

        private void TryAttachBracketsNow()
        {
            try
            {
                if (!_pendingAttach) return;

                // Si hay órdenes 468 o RM vivas → no tocar
                var any468 = _liveOrders?.Any(o => (o?.Comment ?? "").StartsWith(IgnorePrefix)) == true;
                var anyRM  = _liveOrders?.Any(o => (o?.Comment ?? "").StartsWith(OwnerPrefix)) == true;
                if (any468 || anyRM)
                {
                    if (EnableLogging) DebugLog.W("RM/PLAN", $"Abort attach: any468={any468} anyRM={anyRM}");
                    _pendingAttach = false;
                    return;
                }

                // Net actual y deducción de entrada: 0 → ≠0
                var netNow = ReadNetPosition();
                if (Math.Abs(_prevNet) != 0 || Math.Abs(netNow) == 0)
                {
                    // No es transición de plano a pos ⇒ probablemente cierre/parcial: no adjuntar
                    if (EnableLogging) DebugLog.W("RM/PLAN", $"Skip attach: prevNet={_prevNet} netNow={netNow}");
                    _pendingAttach = false;
                    return;
                }

                var dir = Math.Sign(netNow); // +1 long, -1 short
                if (dir == 0)
                {
                    if (EnableLogging) DebugLog.W("RM/PLAN", "dir=0 (no net) → abort");
                    _pendingAttach = false;
                    return;
                }

                // Instrumento (fallbacks)
                var tickSize = FallbackTickSize;
                try { tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize); } catch { }
                var tickValue = FallbackTickValueUsd;

                // Precio de entrada
                var entryPx = _pendingEntryPrice > 0 ? _pendingEntryPrice : GetCandle(CurrentBar).Open;

                var ctx = new MyAtas.Risk.Models.EntryContext(
                    Account: Portfolio?.ToString() ?? "DEFAULT",
                    Symbol: Security?.ToString() ?? "",
                    Direction: dir > 0 ? MyAtas.Risk.Models.Direction.Long : MyAtas.Risk.Models.Direction.Short,
                    EntryPrice: entryPx,
                    ApproxStopTicks: Math.Max(1, DefaultStopTicks),
                    TickSize: tickSize,
                    TickValueUSD: tickValue,
                    TimeUtc: DateTime.UtcNow
                );

                var sizingCfg = new MyAtas.Risk.Models.SizingConfig(
                    Mode: SizingMode.ToString(),
                    ManualQty: Math.Max(1, ManualQty),
                    RiskUsd: RiskPerTradeUsd,
                    RiskPct: RiskPercentOfAccount,
                    AccountEquityOverride: 0m,
                    TickValueOverrides: TickValueOverrides,
                    UnderfundedPolicy: Underfunded,
                    MinQty: Math.Max(1, MinQty),
                    MaxQty: Math.Max(1, MaxQty)
                );

                // TPs desde UI (normalizados a 100%)
                var tps = new System.Collections.Generic.List<decimal>();
                var splits = new System.Collections.Generic.List<int>();
                int n = Math.Clamp(PresetTPs, 1, 3);
                if (n >= 1) { tps.Add(TP1R); splits.Add(TP1pctunit); }
                if (n >= 2) { tps.Add(TP2R); splits.Add(TP2pctunit); }
                if (n >= 3) { tps.Add(TP3R); splits.Add(TP3pctunit); }
                var sum = splits.Sum();
                if (sum <= 0) { splits.Clear(); splits.AddRange(new[] { 100 }); }
                else if (sum != 100)
                {
                    for (int i = 0; i < splits.Count; i++)
                        splits[i] = (int)Math.Max(0, Math.Round(100m * splits[i] / sum));
                    var diff = 100 - splits.Sum();
                    if (diff != 0) splits[^1] = Math.Max(0, splits[^1] + diff);
                }

                var bracketCfg = new MyAtas.Risk.Models.BracketConfig(
                    StopTicks: DefaultStopTicks,
                    SlOffsetTicks: 0m,
                    TpRMultiples: tps.ToArray(),
                    Splits: splits.ToArray()
                );

                var engine = GetEngine();
                if (engine == null) { if (EnableLogging) DebugLog.W("RM/PLAN", "Engine not available → abort"); return; }

                var plan = engine.BuildPlan(ctx, sizingCfg, bracketCfg, out var szReason);

                if (EnableLogging)
                    DebugLog.W("RM/PLAN", $"qty={plan.TotalQty} | SL@{plan.StopLoss.Price:F2} | " +
                                          $"TPs=[{string.Join(",", plan.TakeProfits.Select(tp => $"{tp.Price:F2}x{tp.Quantity}"))}] | " +
                                          $"reason=({szReason}) {plan.Reason}");

                if (plan.TotalQty <= 0) { if (EnableLogging) DebugLog.W("RM/PLAN", "qty<=0 → no attach"); _pendingAttach = false; return; }

                var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                var oco = Guid.NewGuid().ToString("N");
                SubmitRmStop(oco, coverSide, plan.TotalQty, plan.StopLoss.Price);
                foreach (var tp in plan.TakeProfits.Where(t => t.Quantity > 0))
                    SubmitRmLimit(oco, coverSide, tp.Quantity, tp.Price);

                _pendingAttach = false;
                if (EnableLogging) DebugLog.W("RM/PLAN", "Attach DONE");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/PLAN", $"TryAttachBracketsNow EX: {ex.Message}");
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