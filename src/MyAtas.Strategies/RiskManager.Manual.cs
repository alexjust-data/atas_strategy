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
    // Enums externos para serializaciÃ³n correcta de ATAS
    public enum RmSizingMode { Manual, FixedRiskUSD, PercentAccount }
    public enum RmBeMode { Off, OnTP1Touch, OnTP1Fill }
    public enum RmTrailMode { Off, BarByBar, TpToTp }
    public enum RmStopPlacement { ByTicks, PrevBarOppositeExtreme }  // modo de colocaciÃ³n del SL
    public enum RmPrevBarOffsetSide { Outside, Inside }              // NEW: lado del offset (fuera/dentro)

    // Nota: esqueleto "safe". No envï¿½a ni cancela ï¿½rdenes.
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
        [Description("Si la entrada manual ejecuta menos contratos que el objetivo calculado por el RM, la estrategia enviarÃ¡ una orden a mercado por la diferencia (delta).")]
        public bool EnforceManualQty { get; set; } = true;

        [Category("Position Sizing"), DisplayName("Mode")]
        public RmSizingMode SizingMode { get; set; } = RmSizingMode.Manual;

        [Category("Position Sizing"), DisplayName("Manual qty")]
        [Description("Cantidad objetivo de la ESTRATEGIA. Si difiere de la qty del ChartTrader y 'Enforce manual qty' estÃ¡ activo, el RM ajustarÃ¡ con orden a mercado.")]
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

        // === Stop placement ===
        [Category("Stops & TPs"), DisplayName("Stop placement mode")]
        [Description("ByTicks: usa 'Default stop (ticks)'. PrevBarOppositeExtreme: coloca el SL en el extremo opuesto de la vela N-1 (+offset).")]
        public RmStopPlacement StopPlacementMode { get; set; } = RmStopPlacement.ByTicks;

        [Category("Stops & TPs"), DisplayName("Prev-bar offset (ticks)")]
        [Description("Holgura aÃ±adida al extremo de la vela N-1 (1 = un tick mÃ¡s allÃ¡ del High/Low).")]
        public int PrevBarOffsetTicks { get; set; } = 1;

        [Category("Stops & TPs"), DisplayName("Prev-bar offset side")]
        [Description("Outside: fuera del extremo (mÃ¡s allÃ¡ del High/Low). Inside: dentro del rango de la vela.")]
        public RmPrevBarOffsetSide PrevBarOffsetSide { get; set; } = RmPrevBarOffsetSide.Outside;

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

        // =================== Stop-to-Flat (RM Close) ===================
        // Cuando el usuario pulsa el botÃ³n rojo de ATAS (Stop Strategy),
        // queremos: cancelar brackets propios y hacer FLATTEN de la posiciÃ³n.
        private bool _stopToFlat = false;
        private DateTime _rmStopGraceUntil = DateTime.MinValue;     // mientras now<=esto, estamos drenando cancel/fill
        private const int _rmStopGraceMs = 2200;                    // holgura post-cancel/flatten
        private DateTime _nextStopSweepAt = DateTime.MinValue;
        private const int _stopSweepEveryMs = 250;                  // sweep periÃ³dico durante el stop

        // ==== Diagnostics / Build stamp ====
        private const string BuildStamp = "RM.Manual/stop-to-flat 2025-09-30T22:00Z";

        // ==== Post-Close grace & timeouts ====
        private DateTime _postCloseUntil = DateTime.MinValue; // if now <= this â†’ inGrace
        private readonly int _postCloseGraceMs = 2200;        // un poco mÃ¡s de holgura tras Close
        private readonly int _cleanupWaitMs = 300;            // wait before aggressive cleanup (ms)
        private readonly int _maxRetryMs = 2000;              // absolute escape from WAIT (ms)

        // ==== State snapshots for external-close detection ====
        private bool _hadRmBracketsPrevTick = false;          // were there RM brackets last tick?
        private int  _prevNet = 0;                            // last net position snapshot
        private DateTime _lastExternalCloseAt = DateTime.MinValue;
        private const int ExternalCloseDebounceMs = 1500;

        // ==== Attach protection ====
        private DateTime _lastAttachArmAt = DateTime.MinValue;
        private const int AttachProtectMs = 1200;             // proteger el attach mÃ¡s tiempo

        // --- Estado de net para detectar 0â†’â‰ 0 (entrada) ---
        private bool _pendingAttach = false;
        private decimal _pendingEntryPrice = 0m;
        private DateTime _pendingSince = DateTime.MinValue;
        private int _pendingDirHint = 0;                 // +1/-1 si logramos leerlo del Order
        private int _pendingFillQty = 0;                 // qty del fill manual (si la API lo expone)
        private readonly int _attachThrottleMs = 200; // consolidaciÃ³n mÃ­nima
        private readonly int _attachDeadlineMs = 120; // fallback rÃ¡pido si el net no llega
        private readonly System.Collections.Generic.List<Order> _liveOrders = new();
        private readonly object _liveOrdersLock = new();
        // Ancla de contexto para SL por estructura: Ã­ndice de N-1 "en el momento del fill"
        private int _pendingPrevBarIdxAtFill = -1;

        // Helper property for compatibility
        private bool IsActivated => ManageManualEntries;

        // Constructor explÃ­cito para evitar excepciones durante carga ATAS
        public RiskManagerManualStrategy()
        {
            try
            {
                // No inicializar aquÃ­ para evitar problemas de carga
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

        // Lee net de forma robusta priorizando TradingManager.Position (net de CUENTA)
        private int ReadNetPosition()
        {
            var snap = ReadPositionSnapshot();
            return snap.NetQty;
        }

        private (int NetQty, decimal AvgPrice) ReadPositionSnapshot()
        {
            try
            {
                if (Portfolio == null || Security == null)
                    return (0, 0m);

                // 0) PRIMERO: TradingManager.Position (CUENTA seleccionada)
                try
                {
                    var tm = this.TradingManager;
                    var tmPos = tm?.GetType().GetProperty("Position")?.GetValue(tm);
                    if (tmPos != null)
                    {
                        int netQty = 0; decimal avgPrice = 0m;
                        foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                        {
                            var p = tmPos.GetType().GetProperty(name);
                            if (p != null)
                            {
                                var v = p.GetValue(tmPos);
                                if (v != null) { netQty = Convert.ToInt32(v); break; }
                            }
                        }
                        foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                        {
                            var p = tmPos.GetType().GetProperty(name);
                            if (p != null)
                            {
                                var v = p.GetValue(tmPos);
                                if (v != null) { avgPrice = Convert.ToDecimal(v); if (avgPrice > 0m) break; }
                            }
                        }
                        if (EnableLogging)
                            DebugLog.W("RM/SNAP", $"TM.Position net={netQty} avg={avgPrice:F2}");
                        return (netQty, avgPrice);
                    }
                } catch { /* fallback */ }

                // 1) Portfolio.GetPosition(Security)
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

                // 2) Iterar Portfolio.Positions
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

        // Devuelve el objeto Position nativo de ATAS (para usar ClosePosition del TradingManager).
        private object GetAtasPositionObject()
        {
            try
            {
                if (Portfolio == null || Security == null) return null;
                var getPos = Portfolio.GetType().GetMethod("GetPosition", new[] { Security.GetType() });
                if (getPos != null)
                {
                    var pos = getPos.Invoke(Portfolio, new object[] { Security });
                    return pos; // puede ser null si no hay posiciÃ³n
                }
            }
            catch { }
            return null;
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
            return 0m; // NADA de GetCandle() aquÃ­
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

            // Heartbeat del estado de Stop-to-Flat (visible en logs)
            if (EnableLogging && IsFirstTickOf(bar))
            {
                var now = DateTime.UtcNow;
                DebugLog.W("RM/HEARTBEAT",
                    $"bar={bar} t={GetCandle(bar).Time:HH:mm:ss} mode={SizingMode} be={BreakEvenMode} trail={TrailingMode} build={BuildStamp} graceUntil={_postCloseUntil:HH:mm:ss.fff} inGrace={(now <= _postCloseUntil)}");
                DebugLog.W("RM/STOP", $"tick={bar} inStop={_stopToFlat} stopGraceUntil={_rmStopGraceUntil:HH:mm:ss.fff} inGrace={(now <= _rmStopGraceUntil)}");
            }

            // Si estamos parando (Stop-to-Flat), no armes/adjuntes brackets nuevos
            if (_stopToFlat)
            {
                // Limpieza pasiva mientras drenamos: reporta si siguen vivos SL/TP
                var flat = Math.Abs(ReadNetPositionSafe()) == 0;
                var live = HasLiveRmBrackets();
                if (EnableLogging)
                    DebugLog.W("RM/STOP", $"drain: flat={flat} liveBrackets={live} inGrace={(DateTime.UtcNow <= _rmStopGraceUntil)}");

                // Barrido periÃ³dico durante el stop: re-cancelar y re-flatten si hace falta (sin duplicar)
                var now = DateTime.UtcNow;
                if (now >= _nextStopSweepAt && now <= _rmStopGraceUntil)
                {
                    CancelNonBracketWorkingOrders("stop-sweep");
                    // reintento preferente: flatten nativo
                    var closedAgain = TryClosePositionViaTradingManager();
                    if (!closedAgain)
                        EnsureFlattenOutstanding("stop-sweep");
                    _nextStopSweepAt = now.AddMilliseconds(_stopSweepEveryMs);
                }
                // No retornamos de OnCalculate global: simplemente dejamos que no se dispare TryAttachBracketsNow()
            }

            // Fallback por barra si quedÃ³ pendiente
            if (_pendingAttach && (DateTime.UtcNow - _pendingSince).TotalMilliseconds >= _attachThrottleMs)
                TryAttachBracketsNow();

            // === Net & external-close detection (with attach protection) ===
            try
            {
                var currentNet = ReadNetPosition();
                var isFlat = Math.Abs(currentNet) == 0;
                var hadBrNow = HasLiveRmBrackets();

                bool transitionClose = (_prevNet != 0 && currentNet == 0);
                bool bracketsEdgeClose = (_hadRmBracketsPrevTick && !hadBrNow && isFlat);
                bool recentAttach = _pendingAttach && (DateTime.UtcNow - _lastAttachArmAt).TotalMilliseconds < AttachProtectMs;
                bool debounce = (DateTime.UtcNow - _lastExternalCloseAt).TotalMilliseconds < ExternalCloseDebounceMs;

                if ((transitionClose || bracketsEdgeClose) && !debounce)
                {
                    CancelResidualBrackets("external close detected");
                    if (!recentAttach) _pendingAttach = false; // <- NO matar attach reciÃ©n armado
                    _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                    _lastExternalCloseAt = DateTime.UtcNow;
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", $"External close â†’ grace until={_postCloseUntil:HH:mm:ss.fff}, recentAttach={recentAttach}");
                }

                // Update prev snapshots for next tick
                _hadRmBracketsPrevTick = hadBrNow;
                _prevNet = currentNet;
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"OnCalculate net/check EX: {ex.Message}");
            }
        }

        protected override void OnOrderChanged(Order order)
        {
            // ==== Enhanced logging for ALL order events ====
            try
            {
                var c = order?.Comment ?? "";
                var st = order.Status();
                if (EnableLogging)
                    DebugLog.W("RM/EVT", $"OnOrderChanged: id={order?.Id} comment='{c}' status={st} side={order?.Direction} qty={order?.QuantityToFill} canceled={order?.Canceled}");
                if (_stopToFlat && EnableLogging)
                    DebugLog.W("RM/STOP", $"EVT: id={order?.Id} comment='{c}' status={st} qty={order?.QuantityToFill} canceled={order?.Canceled}");

                // Track also EXTERNAL orders (ChartTrader) for later mass-cancel on Stop
                // Scope to this instrument+portfolio
                if (order?.Security?.ToString() == Security?.ToString() &&
                    order?.Portfolio?.ToString() == Portfolio?.ToString())
                {
                    lock (_liveOrdersLock)
                    {
                        _liveOrders.Add(order);
                        if (_liveOrders.Count > 512)
                            _liveOrders.RemoveRange(0, _liveOrders.Count - 512);
                    }
                }
            }
            catch { /* ignore logging issues */ }

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

                // Keep legacy "Close position" detection (when comment is present)
                if (comment.IndexOf("Close position", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pendingAttach = false;
                    CancelResidualBrackets("user pressed Close (comment match)");
                    var graceUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                    _postCloseUntil = graceUntil;
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", $"Grace window opened after Close (EVT), until={graceUntil:HH:mm:ss.fff}");
                    return;
                }

                // SÃ³lo nos interesan fills/parciales
                if (!(st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: status={st} (not filled/partly)");
                    return;
                }

                // Marca attach pendiente; la direcciÃ³n la deduciremos con el net por barra o ahora mismo
                _pendingDirHint = ExtractDirFromOrder(order);
                _pendingFillQty = ExtractFilledQty(order);

                // Solo guardar entryPrice si el order tiene AvgPrice vÃ¡lido; si no, lo obtendremos de la posiciÃ³n
                var orderAvgPrice = ExtractAvgFillPrice(order);
                _pendingEntryPrice = orderAvgPrice > 0m ? orderAvgPrice : 0m;

                // Anclar la "prev-bar" en el instante del fill (N-1 respecto a la barra visible ahora)
                try
                {
                    // En ATAS: Ãºltimo Ã­ndice es CurrentBar-1 (vela actual). La "prev" cerrada es CurrentBar-2.
                    _pendingPrevBarIdxAtFill = Math.Max(0, CurrentBar - 2);
                    if (EnableLogging)
                        DebugLog.W("RM/STOPMODE", $"Anchor prevBarIdx set at fill (N-1): {_pendingPrevBarIdxAtFill} (curBarAtFill={CurrentBar})");
                }
                catch { _pendingPrevBarIdxAtFill = -1; }

                // Armar attach con protecciÃ³n temporal
                _pendingAttach = true;
                _pendingSince = DateTime.UtcNow;
                _lastAttachArmAt = _pendingSince;

                if (EnableLogging)
                    DebugLog.W("RM/ORD", $"Manual order DETECTED â†’ pendingAttach=true | dir={_pendingDirHint} fillQty={_pendingFillQty} entryPx={_pendingEntryPrice:F2}");

                TryAttachBracketsNow(); // intenta en el mismo tick; si el gate decide WAIT, ya lo reintentarÃ¡ OnCalculate
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"OnOrderChanged EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        // New: capture newly seen orders too (fires when order is registered)
        protected override void OnNewOrder(Order order)
        {
            try
            {
                if (order == null) return;
                if (EnableLogging)
                    DebugLog.W("RM/EVT", $"OnNewOrder: id={order.Id} comment='{order.Comment}' type={order.Type} status={order.Status()} qty={order.QuantityToFill}");
                if (order.Security?.ToString() == Security?.ToString() &&
                    order.Portfolio?.ToString() == Portfolio?.ToString())
                {
                    lock (_liveOrdersLock)
                    {
                        _liveOrders.Add(order);
                        if (_liveOrders.Count > 512)
                            _liveOrders.RemoveRange(0, _liveOrders.Count - 512);
                    }
                }
            }
            catch { }
        }

        // =================== Stop Strategy hooks ===================
        protected override void OnStarted()
        {
            try
            {
                _stopToFlat = false;
                _rmStopGraceUntil = DateTime.MinValue;
                _nextStopSweepAt   = DateTime.MinValue;
                if (EnableLogging) DebugLog.W("RM/STOP", "Started â†’ reset stop-to-flat flags");
            }
            catch { }
        }

        protected override void OnStopping()
        {
            try
            {
                _stopToFlat = true;
                _rmStopGraceUntil = DateTime.UtcNow.AddMilliseconds(_rmStopGraceMs);
                _nextStopSweepAt   = DateTime.UtcNow; // primer barrido inmediato
                if (EnableLogging) DebugLog.W("RM/STOP", $"OnStopping â†’ engage StopToFlat, grace until={_rmStopGraceUntil:HH:mm:ss.fff}");

                // 1) Cancelar brackets + cualquier otra orden viva del instrumento
                CancelResidualBrackets("stop-to-flat");
                CancelNonBracketWorkingOrders("stop-to-flat");

                // 2) FLATTEN: intentar SIEMPRE el cierre nativo y dejar fallback armado
                var snap = ReadPositionSnapshot();
                if (EnableLogging) DebugLog.W("RM/STOP", $"Stop snapshot: net={snap.NetQty} avg={snap.AvgPrice:F2} (via TM/Portfolio)");
                var tmClosed = TryClosePositionViaTradingManager();
                if (EnableLogging) DebugLog.W("RM/STOP", $"ClosePosition attempt (TM) result={tmClosed}");
                // 3) Fallback garantizado: EnsureFlattenOutstanding no duplica y no-op si net==0
                EnsureFlattenOutstanding("OnStopping");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/STOP", $"OnStopping EX: {ex.Message}");
            }
            // No bloquees aquÃ­: ATAS seguirÃ¡ el ciclo; terminamos de drenar en eventos/ticks
        }

        protected override void OnStopped()
        {
            try
            {
                if (EnableLogging) DebugLog.W("RM/STOP", "OnStopped â†’ strategy stopped (final)");
                _stopToFlat = false;
                _rmStopGraceUntil = DateTime.MinValue;
            }
            catch { }
        }

        private void TryAttachBracketsNow()
        {
            try
            {
                if (!_pendingAttach) return;

                // Si estamos parando, no adjuntar nada (evita re-entradas durante stop)
                if (_stopToFlat)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", "Skipping attach: strategy is stopping");
                    _pendingAttach = false;
                    return;
                }

                // 0) Pre-check & diagnostics
                var now = DateTime.UtcNow;
                // Para los cierres por Stop usamos la gracia local de stop
                var inGrace = (now <= _postCloseUntil) || (now <= _rmStopGraceUntil);
                if (EnableLogging)
                    DebugLog.W("RM/ATTACH", $"Pre-check: pendingSince={_pendingSince:HH:mm:ss.fff} inGrace={inGrace} (closeGrace={(now <= _postCloseUntil)} stopGrace={(now <= _rmStopGraceUntil)})");

                // TelemetrÃ­a de estados de brackets antes del check
                LogOrderStateHistogram("pre-attach");

                // 1) Are there live brackets? (only SL:/TP:, ignore ENF)
                var any468  = HasLiveOrdersWithPrefix(IgnorePrefix);
                var anyRmSl = HasLiveOrdersWithPrefix(OwnerPrefix + "SL:");
                var anyRmTp = HasLiveOrdersWithPrefix(OwnerPrefix + "TP:");
                var anyBrackets = any468 || anyRmSl || anyRmTp;
                if (EnableLogging)
                    DebugLog.W("RM/ATTACH", $"Bracket check: any468={any468} anyRmSl={anyRmSl} anyRmTp={anyRmTp} anyBrackets={anyBrackets}");

                if (anyBrackets && !inGrace)
                {
                    var waitedMs = (int)(now - _pendingSince).TotalMilliseconds;
                    if (waitedMs > _maxRetryMs)
                    {
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs); // bypass
                        if (EnableLogging)
                            DebugLog.W("RM/GRACE", $"MaxRetry {waitedMs}ms â†’ forcing grace & proceeding");
                        // sigue sin return
                    }
                    else if (waitedMs > _cleanupWaitMs)
                    {
                        CancelResidualBrackets($"cleanup timeout waited={waitedMs}ms");
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                        _pendingSince = DateTime.UtcNow;
                        if (EnableLogging)
                            DebugLog.W("RM/GRACE", $"Cleanup timeout â†’ grace reset to {_postCloseUntil:HH:mm:ss.fff}");
                        return; // reintenta
                    }
                    else
                    {
                        if (EnableLogging)
                            DebugLog.W("RM/WAIT", $"live brackets â†’ retry (waited={waitedMs}ms)");
                        return;
                    }
                }
                else if (anyBrackets && inGrace)
                {
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", "post-close grace ACTIVE â†’ ignoring live-brackets block");
                }

                // 2) Gate: si estamos en GRACE, saltamos el gate y vamos a FALLBACK ya mismo
                var netNow = ReadNetPosition();
                var gateWaitedMs = (int)(DateTime.UtcNow - _pendingSince).TotalMilliseconds;
                if (EnableLogging) DebugLog.W("RM/GATE", $"Gate check: _prevNet={_prevNet} netNow={netNow} waitedMs={gateWaitedMs} deadline={_attachDeadlineMs} inGrace={inGrace}");

                if (!inGrace)
                {
                    if (Math.Abs(_prevNet) == 0 && Math.Abs(netNow) > 0)
                    {
                        if (EnableLogging) DebugLog.W("RM/GATE", $"VALID TRANSITION: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms â†’ ATTACH");
                    }
                    else
                    {
                        if (gateWaitedMs < _attachDeadlineMs)
                        {
                            if (EnableLogging) DebugLog.W("RM/GATE", $"WAITING: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms < deadline={_attachDeadlineMs}ms â†’ WAIT");
                            return;
                        }
                        if (!(AllowAttachFallback && _pendingDirHint != 0))
                        {
                            _pendingAttach = false;
                            if (EnableLogging) DebugLog.W("RM/GATE", $"ABORT: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms â†’ no fallback allowed");
                            if (EnableLogging) DebugLog.W("RM/ABORT", "flat after TTL");
                            return;
                        }
                        if (EnableLogging) DebugLog.W("RM/GATE", $"FALLBACK: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms â†’ ATTACH(FALLBACK) dirHint={_pendingDirHint}");
                    }
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/GATE", $"GRACE BYPASS: skipping net gate â†’ ATTACH(FALLBACK) dirHint={_pendingDirHint}");
                }

                var dir = Math.Abs(netNow) > 0 ? Math.Sign(netNow) : _pendingDirHint;
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Direction determined: dir={dir} (netNow={netNow}, dirHint={_pendingDirHint})");

                // qty OBJETIVO del plan:
                //  - Manual  â†’ SIEMPRE la UI (ManualQty), no el net/fill
                //  - Riesgo  â†’ la calcularÃ¡ el engine
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

                // precio de entrada: order â†’ avgPrice posiciÃ³n â†’ vela previa (Close)
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
                    if (entryPx <= 0m) { if (EnableLogging) DebugLog.W("RM/PLAN", "No valid entryPx available â†’ retry"); return; }
                }

                // === STOP por estructura (opcional) ===
                var approxStopTicks = Math.Max(1, DefaultStopTicks);
                decimal? overrideStopPx = null;
                try
                {
                    if (StopPlacementMode == RmStopPlacement.PrevBarOppositeExtreme)
                    {
                        // Usar la N-1 capturada en el fill; si no estÃ¡, caer a CurrentBar-2 (prev cerrada)
                        var prevIdx = _pendingPrevBarIdxAtFill >= 0
                            ? Math.Max(0, _pendingPrevBarIdxAtFill)
                            : Math.Max(0, CurrentBar - 2);
                        var prev = GetCandle(prevIdx);
                        if (prev != null)
                        {
                            var basePx = (dir > 0) ? prev.Low : prev.High;        // LONG â†’ Low(N-1); SHORT â†’ High(N-1)
                            var offsetTicks = Math.Max(0, PrevBarOffsetTicks);
                            var offset = offsetTicks * Convert.ToDecimal(tickSize);
                            var outside = PrevBarOffsetSide == RmPrevBarOffsetSide.Outside;
                            decimal rawSL;
                            if (dir > 0) // LONG
                                rawSL = outside ? (basePx - offset) : (basePx + offset);
                            else         // SHORT
                                rawSL = outside ? (basePx + offset) : (basePx - offset);
                            overrideStopPx = ShrinkPrice(rawSL);                   // tick-safe
                            // ticks aproximados desde entryPx
                            approxStopTicks = Math.Max(1, (int)Math.Round(Math.Abs(entryPx - overrideStopPx.Value) / tickSize));
                            if (EnableLogging)
                                DebugLog.W("RM/STOPMODE", $"PrevBar SL({PrevBarOffsetSide}): used prevIdx={prevIdx} base={basePx:F2} offsetTicks={offsetTicks} â†’ SL={overrideStopPx.Value:F2} ticksâ‰ˆ{approxStopTicks}");
                        }
                    }
                } catch { /* fallback a DefaultStopTicks */ }

                var ctx = new MyAtas.Risk.Models.EntryContext(
                    Account: Portfolio?.ToString() ?? "DEFAULT",
                    Symbol: Security?.ToString() ?? "",
                    Direction: dir > 0 ? MyAtas.Risk.Models.Direction.Long : MyAtas.Risk.Models.Direction.Short,
                    EntryPrice: entryPx,
                    ApproxStopTicks: approxStopTicks,                      // <-- usa ticks desde N-1 si procede
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
                    StopTicks: approxStopTicks,                            // <-- idem
                    SlOffsetTicks: 0m,
                    TpRMultiples: tps.ToArray(),
                    Splits: splits.ToArray()
                );

                var engine = GetEngine();
                if (engine == null) { if (EnableLogging) DebugLog.W("RM/PLAN", "Engine not available â†’ abort"); return; }

                var plan = engine.BuildPlan(ctx, sizingCfg, bracketCfg, out var szReason);

                if (plan == null)
                {
                    if (EnableLogging) DebugLog.W("RM/PLAN", "BuildPlan â†’ null");
                    return;
                }
                else
                {
                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", $"Built plan: totalQty={plan.TotalQty} stop={plan.StopLoss?.Price:F2} tps={plan.TakeProfits?.Count} reason={plan.Reason}");
                }

                if (plan.TotalQty <= 0) { if (EnableLogging) DebugLog.W("RM/PLAN", "qty<=0 â†’ no attach"); _pendingAttach = false; return; }

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
                        if (EnableLogging)
                            DebugLog.W("RM/ENTRY", $"ENFORCE TRIGGER: sending MARKET {side} +{delta}");
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

                // OCO 1:1 por TP â€” cada TP lleva su propio trozo de SL
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Starting bracket loop: TakeProfits.Count={plan.TakeProfits.Count}");

                for (int idx = 0; idx < plan.TakeProfits.Count; idx++)
                {
                    var tp = plan.TakeProfits[idx];
                    var ocoId = Guid.NewGuid().ToString("N");
                    var slPriceToUse = overrideStopPx ?? plan.StopLoss.Price;
                    SubmitRmStop(ocoId, coverSide, tp.Quantity, slPriceToUse);
                    SubmitRmLimit(ocoId, coverSide, tp.Quantity, tp.Price);
                    if (EnableLogging)
                        DebugLog.W("RM/ORD", $"PAIR #{idx + 1}: OCO SL {tp.Quantity}@{slPriceToUse:F2} + TP {tp.Quantity}@{tp.Price:F2} (dir={(dir>0?"LONG":"SHORT")})");
                }

                _pendingAttach = false;
                _pendingPrevBarIdxAtFill = -1; // limpiar el ancla para la siguiente entrada
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
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop ENTER: side={side} qty={qty} triggerPx={triggerPx:F2} oco={oco} comment={comment}");

            try
            {
                var shrunkPx = ShrinkPrice(triggerPx);
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop: ShrinkPrice({triggerPx:F2}) â†’ {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Stop,
                    TriggerPrice = shrunkPx, // â† tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // â† cancelar al cerrar
                TrySetReduceOnly(order);             // â† no abrir nuevas
                TrySetCloseOnTrigger(order);         // â† cerrar al disparar

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
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit: ShrinkPrice({price:F2}) â†’ {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Limit,
                    Price = shrunkPx,       // â† tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // â† cancelar al cerrar
                TrySetReduceOnly(order);             // â† no abrir nuevas

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
                // IMPORTANTE: no marcar ReduceOnly aquÃ­ â€” queremos abrir delta
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

        // ==== Helpers: RM brackets detection & cleanup ====
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
                    // Consider LIVE only when actively working according to ATAS enum:
                    // Placed (working) or PartlyFilled (still has remainder). Ignore None/Filled/Canceled.
                    if (!o.Canceled && (st == OrderStatus.Placed || st == OrderStatus.PartlyFilled))
                        return true;
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"HasLiveRmBrackets EX: {ex.Message}");
            }
            return false;
        }

        private void LogOrderStateHistogram(string tag)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;
                int slPlaced = 0, slPart = 0, tpPlaced = 0, tpPart = 0;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    var st = o.Status();
                    bool isSL = c.StartsWith(OwnerPrefix + "SL:");
                    bool isTP = c.StartsWith(OwnerPrefix + "TP:");
                    if (isSL && st == OrderStatus.Placed) slPlaced++;
                    else if (isSL && st == OrderStatus.PartlyFilled) slPart++;
                    else if (isTP && st == OrderStatus.Placed) tpPlaced++;
                    else if (isTP && st == OrderStatus.PartlyFilled) tpPart++;
                }
                if (EnableLogging)
                    DebugLog.W("RM/STATES", $"{tag}: SL(placed={slPlaced}, partly={slPart}) TP(placed={tpPlaced}, partly={tpPart})");
            }
            catch { }
        }

        private void CancelResidualBrackets(string reason)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;
                int canceled = 0;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!(c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:"))) continue;
                    var st = o.Status();
                    if (o.Canceled || st == OrderStatus.Canceled || st == OrderStatus.Filled) continue;
                    try { CancelOrder(o); canceled++; } catch { }
                }
                if (EnableLogging) DebugLog.W("RM/CLEAN", $"Canceled residual RM brackets (n={canceled}) reason='{reason}'");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"CancelResidualBrackets EX: {ex.Message}");
            }
        }

        private void CancelNonBracketWorkingOrders(string reason)
        {
            try
            {
                // UniÃ³n: Ã³rdenes de la estrategia + externas detectadas (ChartTrader)
                var union = new System.Collections.Generic.List<Order>();
                if (this.Orders != null) union.AddRange(this.Orders);
                lock (_liveOrdersLock) union.AddRange(_liveOrders);

                var seen = new System.Collections.Generic.HashSet<string>();
                int canceled = 0, considered = 0;
                foreach (var o in union)
                {
                    if (o == null) continue;
                    // Mismo instrumento/portfolio (comparaciÃ³n laxa por ToString para evitar tipos internos)
                    if (o.Security?.ToString() != Security?.ToString()) continue;
                    if (o.Portfolio?.ToString() != Portfolio?.ToString()) continue;
                    var oid = o.Id ?? $"{o.GetHashCode()}";
                    if (!seen.Add(oid)) continue;

                    var c  = o.Comment ?? "";
                    var st = o.Status();
                    var isBracket = c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:");
                    var isMyFlat  = c.StartsWith(OwnerPrefix + "STPFLAT:");
                    var isLive    = !o.Canceled && st != OrderStatus.Canceled && st != OrderStatus.Filled; // inclusivo: None/Placed/PartlyFilled
                    if (EnableLogging)
                        DebugLog.W("RM/CLEAN", $"consider cancel: id={oid} c='{c}' st={st} canceled={o.Canceled} isBracket={isBracket} isMyFlat={isMyFlat} isLive={isLive}");

                    if (isBracket || isMyFlat) continue; // brackets ya se limpian; no matar STPFLAT
                    considered++;
                    if (isLive)
                    {
                        if (TryCancelAnyOrder(o)) canceled++;
                    }
                }
                if (EnableLogging) DebugLog.W("RM/CLEAN", $"Canceled non-bracket working orders (n={canceled}/{considered}) reason='{reason}'");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"CancelNonBracketWorkingOrders EX: {ex.Message}");
            }
        }

        // Try to cancel with both Strategy API and TradingManager (external orders)
        private bool TryCancelAnyOrder(Order o)
        {
            try
            {
                // 1) Strategy-owned way (works for this strategy orders)
                try { CancelOrder(o); return true; } catch { /* might not belong to strategy */ }

                // 2) TradingManager (platform-level) â€” sync variant
                var tm = this.TradingManager;
                if (tm != null)
                {
                    var mi = tm.GetType().GetMethod("CancelOrder", new[] { typeof(Order), typeof(bool), typeof(bool) });
                    if (mi != null)
                    {
                        mi.Invoke(tm, new object[] { o, false, false });
                        return true;
                    }
                    // 2b) Async variant
                    var mia = tm.GetType().GetMethod("CancelOrderAsync", new[] { typeof(Order), typeof(bool), typeof(bool) });
                    if (mia != null)
                    {
                        var task = (System.Threading.Tasks.Task)mia.Invoke(tm, new object[] { o, false, false });
                        // fire-and-forget; assume submitted
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"TryCancelAnyOrder EX: {ex.Message}");
            }
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
                    ro.ReduceOnly = true;   // evita abrir posiciÃ³n nueva
                    order.ExtendedOptions = flags;
                }
            } catch { }
        }

        // Flatten por TradingManager (cascada de firmas). Devuelve true si se invocÃ³ alguna variante.
        private bool TryClosePositionViaTradingManager()
        {
            try
            {
                var tm = this.TradingManager;
                if (tm == null)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", "TradingManager null â†’ fallback MARKET");
                    return false;
                }

                var tmt = tm.GetType();

                // A) Con Position del TM (si existe)
                object posObj = null;
                try { posObj = tmt.GetProperty("Position")?.GetValue(tm); } catch { /* ignore */ }
                if (posObj == null)
                {
                    // Fallback: intentar obtener Position desde Portfolio.GetPosition(Security)
                    posObj = GetAtasPositionObject();
                }

                // 1) ClosePosition(Position, bool, bool)
                if (posObj != null)
                {
                    var mi1 = tmt.GetMethod("ClosePosition", new[] { posObj.GetType(), typeof(bool), typeof(bool) });
                    if (mi1 != null)
                    {
                        var ret = mi1.Invoke(tm, new object[] { posObj, false, true });
                        if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Position,false,true) invoked");
                        return (ret as bool?) ?? true;
                    }

                    // 2) ClosePositionAsync(Position, bool, bool)
                    var mi2 = tmt.GetMethod("ClosePositionAsync", new[] { posObj.GetType(), typeof(bool), typeof(bool) });
                    if (mi2 != null)
                    {
                        try
                        {
                            var task = mi2.Invoke(tm, new object[] { posObj, false, true }) as System.Threading.Tasks.Task;
                            if (EnableLogging) DebugLog.W("RM/STOP", "ClosePositionAsync(Position,false,true) invoked");
                            return true; // asumimos submit correcto
                        }
                        catch (Exception exa)
                        {
                            if (EnableLogging) DebugLog.W("RM/STOP", $"ClosePositionAsync EX: {exa.Message}");
                        }
                    }
                }

                // 3) ClosePosition(Portfolio, Security, bool, bool)
                var mi3 = tmt.GetMethod("ClosePosition", new[] { Portfolio?.GetType(), Security?.GetType(), typeof(bool), typeof(bool) });
                if (mi3 != null)
                {
                    mi3.Invoke(tm, new object[] { Portfolio, Security, true, true });
                    if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Portfolio,Security,true,true) invoked");
                    return true;
                }

                // 4) ClosePosition(Portfolio, Security)
                var mi4 = tmt.GetMethod("ClosePosition", new[] { Portfolio?.GetType(), Security?.GetType() });
                if (mi4 != null)
                {
                    mi4.Invoke(tm, new object[] { Portfolio, Security });
                    if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Portfolio,Security) invoked");
                    return true;
                }

                if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition* not found â†’ fallback MARKET");
                return false;
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/STOP", $"TryClosePositionViaTradingManager EX: {ex.Message} â†’ fallback MARKET");
                return false;
            }
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
                    // consideramos viva si NO estÃ¡ cancelada y NO estÃ¡ llena
                    var st = o.Status();
                    return !o.Canceled
                           && st != OrderStatus.Filled
                           && st != OrderStatus.Canceled;
                });
            }
            catch { return false; }
        }

        // Log de cancelaciÃ³n fallida (para ver por quÃ© queda algo "working")
        protected override void OnOrderCancelFailed(Order order, string message)
        {
            if (!EnableLogging) return;
            try
            {
                DebugLog.W("RM/STOP", $"OnOrderCancelFailed: id={order?.Id} comment='{order?.Comment}' status={order?.Status()} msg={message}");
            } catch { }
        }

        // ========= Helpers de neta y flatten =========
        // Siempre usa el snapshot de CUENTA (TM.Position/Portfolio). No uses CurrentPosition aquÃ­.
        private int ReadNetPositionSafe()
        {
            try { return ReadNetPosition(); } catch { return 0; }
        }

        private bool HasWorkingOrdersWithPrefix(string prefix)
        {
            try
            {
                var list = this.Orders; if (list == null) return false;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!c.StartsWith(prefix)) continue;
                    var st = o.Status();
                    // "working" en ATAS = Placed o PartlyFilled (evitamos None/Filled/Canceled)
                    if (!o.Canceled && (st == OrderStatus.Placed || st == OrderStatus.PartlyFilled))
                        return true;
                }
            } catch { }
            return false;
        }

        // EnvÃ­a (si hace falta) la orden MARKET reduce-only para quedar flat.
        // Evita duplicarla si ya hay una STPFLAT "working".
        private void EnsureFlattenOutstanding(string reason)
        {
            try
            {
                var net = ReadNetPositionSafe();
                if (net == 0)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding: already flat ({reason})");
                    return;
                }
                if (HasWorkingOrdersWithPrefix(OwnerPrefix + "STPFLAT:"))
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding: STPFLAT already working ({reason})");
                    return;
                }
                var side = net > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                var qty  = Math.Abs(net);
                var comment = $"{OwnerPrefix}STPFLAT:{Guid.NewGuid():N}";
                var o = new Order
                {
                    Portfolio      = Portfolio,
                    Security       = Security,
                    Direction      = side,
                    Type           = OrderTypes.Market,
                    QuantityToFill = qty,
                    Comment        = comment
                };
                TrySetReduceOnly(o); // evita abrir si hay desincronizaciÃ³n
                OpenOrder(o);
                if (EnableLogging) DebugLog.W("RM/STOP", $"Flatten MARKET sent: {side} {qty} ({reason}) comment={comment}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding EX: {ex.Message}");
            }
        }

    }
}