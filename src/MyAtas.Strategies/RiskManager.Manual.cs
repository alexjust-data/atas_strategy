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

        [Category("Activation"), DisplayName("Allow attach without net (fallback)")]
        public bool AllowAttachFallback { get; set; } = true;

        [Category("Activation"), DisplayName("Ignore orders with prefix")]
        public string IgnorePrefix { get; set; } = "468"; // no interferir con la 468

        [Category("Activation"), DisplayName("Owner prefix (this strategy)")]
        public string OwnerPrefix { get; set; } = "RM:";

        [Category("Activation"), DisplayName("Enforce manual qty on entry")]
        [Description("Si la entrada manual ejecuta menos contratos que el objetivo calculado por el RM, la estrategia enviará una orden a mercado por la diferencia (delta).")]
        public bool EnforceManualQty { get; set; } = true;

        [Category("Position Sizing"), DisplayName("Mode")]
        public RmSizingMode SizingMode { get; set; } = RmSizingMode.Manual;

        [Category("Position Sizing"), DisplayName("Manual qty")]
        [Description("Cantidad objetivo de la ESTRATEGIA. Si difiere de la qty del ChartTrader y 'Enforce manual qty' está activo, el RM ajustará con orden a mercado.")]
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
        private int _pendingDirHint = 0;                 // +1/-1 si logramos leerlo del Order
        private int _pendingFillQty = 0;                 // qty del fill manual (si la API lo expone)
        private readonly int _attachThrottleMs = 200; // consolidación mínima
        private readonly int _attachDeadlineMs = 120; // fallback rápido si el net no llega
        private System.Collections.Generic.List<Order> _liveOrders = new();
        private const string BuildStamp = "RM.Manual 2025-09-30T19:40Z"; // cambia en cada build

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
            var snapshot = ReadPositionSnapshot();
            return snapshot.NetQty;
        }

        private (int NetQty, decimal AvgPrice) ReadPositionSnapshot()
        {
            try
            {
                if (Portfolio == null || Security == null)
                    return (0, 0m);

                // 1) Intento directo: Portfolio.GetPosition(Security)
                try
                {
                    var getPos = Portfolio.GetType().GetMethod("GetPosition", new[] { Security.GetType() });
                    if (getPos != null)
                    {
                        var pos = getPos.Invoke(Portfolio, new object[] { Security });
                        if (pos != null)
                        {
                            var netQty = 0;
                            var avgPrice = 0m;

                            // Leer Net/Amount/Qty/Position
                            foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                            {
                                var p = pos.GetType().GetProperty(name);
                                if (p != null)
                                {
                                    var v = p.GetValue(pos);
                                    if (v != null)
                                    {
                                        netQty = Convert.ToInt32(v);
                                        break;
                                    }
                                }
                            }

                            // Leer AvgPrice/AveragePrice/EntryPrice/Price
                            foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                            {
                                var p = pos.GetType().GetProperty(name);
                                if (p != null)
                                {
                                    var v = p.GetValue(pos);
                                    if (v != null)
                                    {
                                        avgPrice = Convert.ToDecimal(v);
                                        if (avgPrice > 0m) break;
                                    }
                                }
                            }

                            if (EnableLogging && (netQty != 0 || avgPrice > 0m))
                                DebugLog.W("RM/SNAP", $"pos avgPrice={avgPrice:F2} net={netQty} (source=Portfolio.GetPosition)");
                            return (netQty, avgPrice);
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
                                var netQty = 0;
                                var avgPrice = 0m;

                                // Leer Net/Amount/Qty/Position
                                foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                                {
                                    var p = pos.GetType().GetProperty(name);
                                    if (p != null)
                                    {
                                        var v = p.GetValue(pos);
                                        if (v != null)
                                        {
                                            netQty = Convert.ToInt32(v);
                                            break;
                                        }
                                    }
                                }

                                // Leer AvgPrice/AveragePrice/EntryPrice/Price
                                foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                                {
                                    var p = pos.GetType().GetProperty(name);
                                    if (p != null)
                                    {
                                        var v = p.GetValue(pos);
                                        if (v != null)
                                        {
                                            avgPrice = Convert.ToDecimal(v);
                                            if (avgPrice > 0m) break;
                                        }
                                    }
                                }

                                if (EnableLogging && (netQty != 0 || avgPrice > 0m))
                                    DebugLog.W("RM/SNAP", $"pos avgPrice={avgPrice:F2} net={netQty} (source=Portfolio.Positions)");
                                return (netQty, avgPrice);
                            }
                        }
                    }
                }
                catch { /* devolver valores por defecto */ }
            }
            catch { }
            return (0, 0m);
        }

        private decimal ExtractAvgFillPrice(Order order)
        {
            foreach (var name in new[] { "AvgPrice", "AveragePrice", "AvgFillPrice", "Price" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Convert.ToDecimal(p.GetValue(order));
                    if (v > 0m) return v;
                }
                catch { }
            }
            return 0m; // NADA de GetCandle() aquí
        }

        private int ExtractFilledQty(Order order)
        {
            foreach (var name in new[] { "Filled", "FilledQuantity", "Quantity", "QuantityToFill", "Volume", "Lots" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = p.GetValue(order);
                    if (v == null) continue;
                    var q = Convert.ToInt32(v);
                    if (q > 0) return q;
                }
                catch { }
            }
            return 0;
        }

        private int ExtractDirFromOrder(Order order)
        {
            foreach (var name in new[] { "Direction", "Side", "OrderDirection", "OrderSide", "TradeSide" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var s = p.GetValue(order)?.ToString() ?? "";
                    if (s.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0) return +1;
                    if (s.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0) return -1;
                }
                catch { }
            }
            return 0;
        }


        protected override void OnCalculate(int bar, decimal value)
        {
            if (!IsActivated) return;

            if (EnableLogging && IsFirstTickOf(bar))
                DebugLog.W("RM/HEARTBEAT", $"bar={bar} t={GetCandle(bar).Time:HH:mm:ss} mode={SizingMode} be={BreakEvenMode} trail={TrailingMode} build={BuildStamp}");

            // Fallback por barra si quedó pendiente
            if (_pendingAttach && (DateTime.UtcNow - _pendingSince).TotalMilliseconds >= _attachThrottleMs)
                TryAttachBracketsNow();

            // Actualiza snapshot de net y limpia pendientes al cerrar posición
            try
            {
                var currentNet = ReadNetPosition();

                // Cleanup: si estábamos en posición y ahora estamos flat, limpiar pending y brackets
                if (Math.Abs(_prevNet) > 0 && Math.Abs(currentNet) == 0 && _pendingAttach)
                {
                    _pendingAttach = false;
                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", $"Position closed: cleanup pending state (prevNet={_prevNet} → currentNet={currentNet})");
                    CancelResidualBrackets("flat detected");
                }

                _prevNet = currentNet;
            }
            catch { }
        }

        protected override void OnOrderChanged(Order order)
        {
            try
            {
                if (!IsActivated || !ManageManualEntries) return;

                var comment = order?.Comment ?? "";
                var st = order.Status();

                if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged: comment={comment} status={st}");

                // Ignora la 468 y mis propias RM
                if (comment.StartsWith(IgnorePrefix))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: IgnorePrefix detected ({IgnorePrefix})");
                    return;
                }
                if (comment.StartsWith(OwnerPrefix))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: OwnerPrefix detected ({OwnerPrefix})");
                    return;
                }

                // Ignora cierres explícitos y limpia pending state
                if (comment.IndexOf("Close position", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pendingAttach = false;
                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", "cleared by Close position");
                    return;
                }

                // Sólo nos interesan fills/parciales
                if (!(st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: status={st} (not filled/partly)");
                    return;
                }

                // Marca attach pendiente; la dirección la deduciremos con el net por barra o ahora mismo
                _pendingDirHint = ExtractDirFromOrder(order);
                _pendingFillQty = ExtractFilledQty(order);

                // Solo guardar entryPrice si el order tiene AvgPrice válido; si no, lo obtendremos de la posición
                var orderAvgPrice = ExtractAvgFillPrice(order);
                _pendingEntryPrice = orderAvgPrice > 0m ? orderAvgPrice : 0m;

                _pendingAttach = true;
                _pendingSince = DateTime.UtcNow;

                if (EnableLogging)
                    DebugLog.W("RM/ORD", $"Manual order DETECTED → pendingAttach=true | dir={_pendingDirHint} fillQty={_pendingFillQty} entryPx={_pendingEntryPrice:F2}");

                TryAttachBracketsNow(); // intenta en el mismo tick; si el gate decide WAIT, ya lo reintentará OnCalculate
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"OnOrderChanged EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        private void TryAttachBracketsNow()
        {
            try
            {
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"TryAttachBracketsNow ENTER: _pendingAttach={_pendingAttach}");

                if (!_pendingAttach) return;

                // 0) Cancelar cualquier bracket RM residual si estamos flat (o en cualquier caso, para permitir re-attach limpio)
                if (Math.Abs(ReadNetPosition()) == 0 && HasLiveRmBrackets())
                    CancelResidualBrackets("pre-attach cleanup");

                // 1) ¿hay BRACKETS vivos? (SL/TP). Las órdenes de enforcement (ENF) no deben bloquear.
                var any468 = HasLiveOrdersWithPrefix(IgnorePrefix);
                var anyRmSl = HasLiveOrdersWithPrefix(OwnerPrefix + "SL:");
                var anyRmTp = HasLiveOrdersWithPrefix(OwnerPrefix + "TP:");
                var anyBrackets = any468 || anyRmSl || anyRmTp;

                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Bracket check: any468={any468} anyRmSl={anyRmSl} anyRmTp={anyRmTp} anyBrackets={anyBrackets}");

                if (anyBrackets)
                {
                    if (EnableLogging) DebugLog.W("RM/ABORT", $"live brackets exist → any468={any468} SL={anyRmSl} TP={anyRmTp}");
                    _pendingAttach = false;
                    return;
                }

                // 2) Hard gate: solo adjuntar en transición FLAT→POSICIÓN (0→≠0)
                var netNow = ReadNetPosition();
                var waitedMs = (int)(DateTime.UtcNow - _pendingSince).TotalMilliseconds;

                if (EnableLogging) DebugLog.W("RM/GATE", $"Gate check: _prevNet={_prevNet} netNow={netNow} waitedMs={waitedMs} deadline={_attachDeadlineMs}");

                if (Math.Abs(_prevNet) == 0 && Math.Abs(netNow) > 0)
                {
                    // Transición válida 0→≠0: proceder con adjuntar
                    if (EnableLogging) DebugLog.W("RM/GATE", $"VALID TRANSITION: prevNet={_prevNet} netNow={netNow} elapsed={waitedMs}ms → ATTACH");
                }
                else
                {
                    // si aún no llegamos al deadline → WAIT
                    if (waitedMs < _attachDeadlineMs)
                    {
                        if (EnableLogging) DebugLog.W("RM/GATE", $"WAITING: prevNet={_prevNet} netNow={netNow} elapsed={waitedMs}ms < deadline={_attachDeadlineMs}ms → WAIT");
                        return;
                    }
                    // deadline alcanzado → ¿podemos fallback?
                    if (!(AllowAttachFallback && _pendingDirHint != 0))
                    {
                        _pendingAttach = false;
                        if (EnableLogging) DebugLog.W("RM/GATE", $"ABORT: prevNet={_prevNet} netNow={netNow} elapsed={waitedMs}ms → no fallback allowed");
                        if (EnableLogging) DebugLog.W("RM/ABORT", "flat after TTL");
                        return;
                    }
                    if (EnableLogging) DebugLog.W("RM/GATE", $"FALLBACK: prevNet={_prevNet} netNow={netNow} elapsed={waitedMs}ms → ATTACH(FALLBACK) dirHint={_pendingDirHint}");
                }

                var dir = Math.Abs(netNow) > 0 ? Math.Sign(netNow) : _pendingDirHint;
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Direction determined: dir={dir} (netNow={netNow}, dirHint={_pendingDirHint})");

                // qty OBJETIVO del plan:
                //  - Manual  → SIEMPRE la UI (ManualQty), no el net/fill
                //  - Riesgo  → la calculará el engine
                int manualQtyToUse = ManualQty;
                if (SizingMode == RmSizingMode.Manual)
                {
                    manualQtyToUse = Math.Clamp(ManualQty, Math.Max(1, MinQty), Math.Max(1, MaxQty));
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Manual mode: Using UI ManualQty={manualQtyToUse} (ignoring net/fill for TARGET)");
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Risk-based sizing: engine will compute target qty");
                }

                // Instrumento (fallbacks)
                var tickSize = FallbackTickSize;
                try { tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize); } catch { }
                var tickValue = FallbackTickValueUsd;

                // precio de entrada: order → avgPrice posición → vela previa (Close)
                var entryPx = _pendingEntryPrice;
                if (entryPx <= 0m)
                {
                    var snap = ReadPositionSnapshot();
                    entryPx = snap.AvgPrice;
                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", $"Using position avgPrice: entryPx={entryPx:F2} (orderPrice was {_pendingEntryPrice:F2})");
                }
                if (entryPx <= 0m)
                {
                    try
                    {
                        var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                        entryPx = GetCandle(barIdx).Close;
                        if (EnableLogging) DebugLog.W("RM/PLAN", $"Fallback candle price used: entryPx={entryPx:F2} (bar={barIdx})");
                    } catch { }
                    if (entryPx <= 0m) { if (EnableLogging) DebugLog.W("RM/PLAN", "No valid entryPx available → retry"); return; }
                }

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
                    ManualQty: manualQtyToUse,
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
                {
                    var tag = (Math.Abs(netNow) > 0 ? "ATTACH" : "ATTACH(FALLBACK)");
                    DebugLog.W("RM/PLAN", $"{tag} qty={plan.TotalQty} | SL@{plan.StopLoss.Price:F2} | TPs=[{string.Join(",", plan.TakeProfits.Select(tp => $"{tp.Price:F2}x{tp.Quantity}"))}] | reason=({szReason}) {plan.Reason}");
                }

                if (plan.TotalQty <= 0) { if (EnableLogging) DebugLog.W("RM/PLAN", "qty<=0 → no attach"); _pendingAttach = false; return; }

                // 3bis) ENFORCEMENT DE CANTIDAD (si procede)
                if (EnforceManualQty)
                {
                    var currentNet = Math.Abs(ReadNetPosition());
                    var filledHint = Math.Max(0, _pendingFillQty);
                    var qSeen = Math.Max(currentNet, filledHint);
                    var targetQty = Math.Max(1, plan.TotalQty); // ahora plan.TotalQty = ManualQty (o qty por riesgo)
                    var delta = targetQty - qSeen;

                    if (EnableLogging) DebugLog.W("RM/ENTRY",
                        $"ENFORCE CHECK: target={targetQty} (from plan/UI) | seen={qSeen} (net={currentNet}, fillHint={filledHint}) | delta={delta}");

                    if (delta > 0)
                    {
                        var side = dir > 0 ? OrderDirections.Buy : OrderDirections.Sell;
                        if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE TRIGGER: Sending {side} Market Order delta=+{delta} (target={targetQty} - seen={qSeen})");
                        SubmitRmMarket(side, delta);
                    }
                    else
                    {
                        if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE SKIP: No delta needed (target={targetQty} already met by seen={qSeen})");
                    }
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/ENTRY", "ENFORCE DISABLED: EnforceManualQty=false");
                }

                var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Cover side for brackets: coverSide={coverSide} (dir={dir})");

                // OCO 1:1 por TP — cada TP lleva su propio trozo de SL
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Starting bracket loop: TakeProfits.Count={plan.TakeProfits.Count}");

                foreach (var (tp, idx) in plan.TakeProfits.Select((t,i) => (t,i)))
                {
                    if (tp.Quantity <= 0)
                    {
                        if (EnableLogging) DebugLog.W("RM/ATTACH", $"SKIP TP #{idx+1}: qty={tp.Quantity} (<=0)");
                        continue;
                    }

                    var ocoId = Guid.NewGuid().ToString("N");
                    if (EnableLogging) DebugLog.W("RM/ATTACH", $"Creating OCO pair #{idx+1}: ocoId={ocoId} qty={tp.Quantity} SL@{plan.StopLoss.Price:F2} TP@{tp.Price:F2}");

                    // IMPORTANTE: el SL de este par usa la misma qty que el TP
                    SubmitRmStop(ocoId, coverSide, tp.Quantity, plan.StopLoss.Price);
                    SubmitRmLimit(ocoId, coverSide, tp.Quantity, tp.Price);

                    if (EnableLogging)
                        DebugLog.W("RM/ORD", $"PAIR #{idx+1}: OCO={ocoId} SL {tp.Quantity}@{plan.StopLoss.Price:F2} + TP {tp.Quantity}@{tp.Price:F2}");
                }

                _pendingAttach = false;
                if (EnableLogging) DebugLog.W("RM/PLAN", "Attach COMPLETE: _pendingAttach set to false");
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
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop ENTER: side={side} qty={qty} triggerPx={triggerPx:F2} oco={oco} comment={comment}");

            try
            {
                var shrunkPx = ShrinkPrice(triggerPx);
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop: ShrinkPrice({triggerPx:F2}) → {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Stop,
                    TriggerPrice = shrunkPx, // ← tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // ← cancelar al cerrar
                TrySetReduceOnly(order);             // ← no abrir nuevas
                TrySetCloseOnTrigger(order);         // ← cerrar al disparar

                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop: Calling OpenOrder() for SL");
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"STOP SENT: {side} {qty} @{order.TriggerPrice:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmStop EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        private void SubmitRmLimit(string oco, OrderDirections side, int qty, decimal price)
        {
            var comment = $"{OwnerPrefix}TP:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit ENTER: side={side} qty={qty} price={price:F2} oco={oco} comment={comment}");

            try
            {
                var shrunkPx = ShrinkPrice(price);
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit: ShrinkPrice({price:F2}) → {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Limit,
                    Price = shrunkPx,       // ← tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // ← cancelar al cerrar
                TrySetReduceOnly(order);             // ← no abrir nuevas

                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit: Calling OpenOrder() for TP");
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"LIMIT SENT: {side} {qty} @{order.Price:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmLimit EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        // === Market (enforcement) ===
        private void SubmitRmMarket(OrderDirections side, int qty)
        {
            var comment = $"{OwnerPrefix}ENF:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmMarket ENTER: side={side} qty={qty} comment={comment}");

            try
            {
                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Market,
                    QuantityToFill = qty,
                    Comment = comment
                };
                // IMPORTANTE: no marcar ReduceOnly aquí — queremos abrir delta
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmMarket: Calling OpenOrder() for {side} +{qty} (ReduceOnly=false)");
                OpenOrder(order);
                if (EnableLogging) DebugLog.W("RM/ORD", $"ENFORCE MARKET SENT: {side} +{qty} @{GetLastPriceSafe():F2} comment={comment}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmMarket EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        private decimal GetLastPriceSafe()
        {
            try
            {
                var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                return GetCandle(barIdx).Close;
            }
            catch { return 0m; }
        }

        // === Cleanup utilities ===
        private void CancelResidualBrackets(string reason)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!c.StartsWith(OwnerPrefix)) continue;
                    if (!(c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:"))) continue;
                    var st = o.Status();
                    if (o.Canceled || st == OrderStatus.Canceled || st == OrderStatus.Filled) continue;
                    try { CancelOrder(o); } catch { }
                }
                if (EnableLogging) DebugLog.W("RM/CLEAN", $"Canceled residual RM brackets ({reason})");
            } catch { }
        }

        private bool HasLiveRmBrackets()
        {
            try
            {
                var list = this.Orders;
                if (list == null) return false;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!(c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:"))) continue;
                    var st = o.Status();
                    if (!o.Canceled && st != OrderStatus.Canceled && st != OrderStatus.Filled)
                        return true;
                }
            } catch { }
            return false;
        }

        // === Additional order options via TradingManager ===
        private void TrySetReduceOnly(Order order)
        {
            try
            {
                var flags = TradingManager?
                    .GetSecurityTradingOptions()?
                    .CreateExtendedOptions(order.Type);
                if (flags is ATAS.DataFeedsCore.IOrderOptionReduceOnly ro)
                {
                    ro.ReduceOnly = true;   // evita abrir posición nueva
                    order.ExtendedOptions = flags;
                }
            } catch { }
        }

        private void TrySetCloseOnTrigger(Order order)
        {
            try
            {
                var flags = TradingManager?
                    .GetSecurityTradingOptions()?
                    .CreateExtendedOptions(order.Type);
                if (flags is ATAS.DataFeedsCore.IOrderOptionCloseOnTrigger ct)
                {
                    ct.CloseOnTrigger = true; // cerrar cuando dispare
                    order.ExtendedOptions = flags;
                }
            } catch { }
        }

        private bool HasLiveOrdersWithPrefix(string prefix)
        {
            try
            {
                var list = this.Orders; // Strategy.Orders
                if (list == null) return false;
                return list.Any(o =>
                {
                    var c = o?.Comment ?? "";
                    if (!c.StartsWith(prefix)) return false;
                    // consideramos viva si NO está cancelada y NO está llena
                    var st = o.Status();
                    return !o.Canceled && st != OrderStatus.Filled && st != OrderStatus.Canceled;
                });
            }
            catch { return false; }
        }

    }
}