using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Collections;
using ATAS.Strategies.Chart;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Indicators;
using MyAtas.Shared;

namespace MyAtas.Strategies
{
    [DisplayName("468 – Simple Strategy (GL close + 2 confluences) - FIXED")]
    public class FourSixEightSimpleStrategy : ChartStrategy
    {
        // ====================== USER PARAMETERS ======================
        [Category("General"), DisplayName("Quantity")]
        public int Quantity { get; set; } = 1;

        [Category("Validation"), DisplayName("Validate GL cross on close (N)")]
        public bool ValidateGenialCrossLocally { get; set; } = true;

        [Category("Validation"), DisplayName("Hysteresis (ticks)")]
        public int HysteresisTicks { get; set; } = 0;

        [Category("General"), DisplayName("Allow only one position at a time")]
        public bool OnlyOnePosition { get; set; } = false;

        // ---- ONLY TWO (optional) CONFLUENCES—DEFAULT ON ----
        [Category("Confluences"), DisplayName("Require GenialLine slope with signal direction")]
        public bool RequireGenialSlope { get; set; } = true;

        [Category("Confluences"), DisplayName("Require EMA8 vs Wilder8 (EMA8>W8 for long; EMA8<W8 for short)")]
        public bool RequireEmaVsWilder { get; set; } = true;

        [Category("Confluences"), DisplayName("EMA vs Wilder tolerance (ticks)")]
        public int EmaVsWilderToleranceTicks { get; set; } = 1;

        // === Execution window ===
        [Category("Execution"), DisplayName("Strict N+1 open (require first tick)")]
        public bool StrictN1Open { get; set; } = true;

        [Category("Execution"), DisplayName("Open tolerance (ticks)")]
        public int OpenToleranceTicks { get; set; } = 2;

        // ====================== RISK / TARGETS ======================
        [Category("Risk/Targets"), DisplayName("Use SL from signal candle")]
        public bool UseSignalCandleSL { get; set; } = true;

        [Category("Risk/Targets"), DisplayName("SL offset (ticks)")]
        public int StopOffsetTicks { get; set; } = 1;

        [Category("Risk/Targets"), DisplayName("Enable TP1")]
        public bool EnableTP1 { get; set; } = true;
        [Category("Risk/Targets"), DisplayName("TP1 (R multiple)")]
        public decimal TP1_R { get; set; } = 1.0m;

        [Category("Risk/Targets"), DisplayName("Enable TP2")]
        public bool EnableTP2 { get; set; } = true;
        [Category("Risk/Targets"), DisplayName("TP2 (R multiple)")]
        public decimal TP2_R { get; set; } = 2.0m;

        [Category("Risk/Targets"), DisplayName("Enable TP3")]
        public bool EnableTP3 { get; set; } = false;
        [Category("Risk/Targets"), DisplayName("TP3 (R multiple)")]
        public decimal TP3_R { get; set; } = 3.0m;

        // --- Execution Control ---
        [Category("Execution"), DisplayName("Attach brackets from actual net fill")]
        public bool AttachBracketsFromNet { get; set; } = true;

        [Category("Execution"), DisplayName("Top-up missing qty to target")]
        public bool TopUpMissingQty { get; set; } = false;

        // --- Risk/Timing ---
        [Category("Risk/Timing"), DisplayName("Enable cooldown after flat")]
        public bool EnableCooldown { get; set; } = true;

        [Category("Risk/Timing"), DisplayName("Cooldown bars after flat")]
        public int CooldownBars { get; set; } = 2;

        // ====================== INTERNAL STATE ======================
        private FourSixEightIndicator _ind;
        private Pending? _pending;           // captured at N (GL-cross close confirmed)
        private Guid _lastUid;               // last indicator UID observed
        private Guid _lastExecUid = Guid.Empty; // last UID executed (true anti-dup)

        // Candado de estado y tracking de órdenes para OnlyOnePosition
        private bool _tradeActive = false;             // true tras enviar la entrada hasta quedar plano y sin órdenes vivas
        private readonly List<Order> _liveOrders = new(); // órdenes creadas por ESTA estrategia y aún activas

        // Post-fill bracket control
        private int _targetQty = 0;          // qty solicitada en la entrada
        private int _lastSignalBar = -1;     // N de la señal que originó la entrada
        private int _entryDir = 0;           // +1 BUY / -1 SELL
        private bool _bracketsPlaced = false; // ya colgados los brackets

        // Cooldown management
        private int _cooldownUntilBar = -1;   // bar index hasta el que no se permite re-entrada
        private int _lastFlatBar = -1;        // último bar en el que quedamos planos

        private struct Pending { public Guid Uid; public int BarId; public int Dir; }

        // Detectar primer tick de cada vela
        private int _lastSeenBar = -1;
        private bool IsFirstTickOf(int currentBar)
        {
            if (currentBar != _lastSeenBar) { _lastSeenBar = currentBar; return true; }
            return false;
        }

        // ====================== LIFECYCLE ======================
        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                DebugLog.Separator("STRATEGY INITIALIZATION");
                _ind = new FourSixEightIndicator();
                // Force indicator to publish only GenialLine cross signals
                _ind.TriggerSource = FourSixEightIndicator.TriggerKind.GenialLine;

                // FIXED: Safer indicator attachment via reflection
                var addInd = GetType().GetMethod("AddIndicator",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (addInd != null)
                {
                    addInd.Invoke(this, new object[] { _ind });
                    DebugLog.Critical("468/STR", "INIT OK (Indicator attached via reflection)");
                }
                else
                {
                    DebugLog.W("468/STR", "WARNING: Could not attach indicator");
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/STR", "INIT EX: " + ex);
            }
        }

        // ====================== CORE ======================
        protected override void OnCalculate(int bar, decimal value)
        {
            // --- Heartbeat ---
            try
            {
                if ((bar % 20) == 0)
                    DebugLog.W("468/STR", $"HEARTBEAT bar={bar} t={GetCandle(bar).Time:HH:mm:ss}");
            }
            catch { }

            // *** CRITICAL DEBUG: Identificar fuente de ejecución inmediata ***
            DebugLog.Critical("468/STR", $"OnCalculate ENTRY: bar={bar} t={GetCandle(bar).Time:HH:mm:ss} pending={(_pending.HasValue ? "YES" : "NO")} tradeActive={_tradeActive}");

            // --- DEBUG: Estado del pending ---
            if (_pending.HasValue)
            {
                DebugLog.W("468/STR", $"PENDING: bar={bar} pendingBar={_pending.Value.BarId} dir={(_pending.Value.Dir > 0 ? "BUY" : "SELL")} uid={_pending.Value.Uid.ToString().Substring(0, 8)} condition={bar > _pending.Value.BarId}");
            }

            // --- DEBUG: Check for signal availability and CAPTURE AT N ---
            var sig = _ind?.LastSignal;
            if (sig.HasValue)
            {
                DebugLog.W("468/STR", $"SIGNAL_CHECK: bar={bar} sigBar={sig.Value.BarId} dir={(sig.Value.Dir > 0 ? "BUY" : "SELL")} uid={sig.Value.Uid.ToString().Substring(0, 8)} lastUid={(_lastUid != Guid.Empty ? _lastUid.ToString().Substring(0, 8) : "NONE")} condition_barMatch={sig.Value.BarId == bar} condition_uidNew={sig.Value.Uid != _lastUid}");
            }

            // 1) CAPTURE AT N (only when the GL-cross signal is on THIS bar) + CLOSE CONFIRMATION
            if (sig.HasValue && sig.Value.BarId == bar && sig.Value.Uid != _lastUid)
            {
                bool closeConfirmed = true;

                if (ValidateGenialCrossLocally && bar >= 1)
                {
                    var cN  = GetCandle(bar).Close;
                    var gN  = GenialAt(bar);
                    var cN1 = GetCandle(bar - 1).Close;
                    var gN1 = GenialAt(bar - 1);
                    var eps = Ticks(Math.Max(0, HysteresisTicks));

                    // FIXED: Proper hysteresis implementation for both current and previous bar
                    if (sig.Value.Dir > 0) // BUY
                        closeConfirmed = (cN > gN + eps) && (cN1 <= gN1 + eps);
                    else                   // SELL
                        closeConfirmed = (cN < gN - eps) && (cN1 >= gN1 - eps);
                }

                if (closeConfirmed)
                {
                    _lastUid = sig.Value.Uid;
                    _pending = new Pending { Uid = sig.Value.Uid, BarId = bar, Dir = sig.Value.Dir };
                    DebugLog.Critical("468/STR", $"CAPTURE: N={bar} {(sig.Value.Dir>0?"BUY":"SELL")} uid={sig.Value.Uid} (confirmed close)");
                }
                else
                {
                    DebugLog.W("468/STR", $"IGNORE signal at N={bar} (close did not confirm; avoids intrabar flip-flop)");
                }
            }

            // 2) EXECUTE EXACTLY AT N+1 — windowed (armed/execute/expire)
            if (_pending.HasValue)
            {
                var execBar = _pending.Value.BarId + 1;
                if (bar < execBar)
                {
                    // Aún no toca; mantenemos el pending "ARMED"
                    if ((bar % 50) == 0)
                        DebugLog.W("468/STR", $"PENDING ARMED: now={bar}, execBar={execBar}");
                    return;
                }
                if (bar > execBar)
                {
                    // Nos saltamos N+1 → señal caducada (nada de pendientes eternos)
                    DebugLog.W("468/STR", $"PENDING EXPIRED: now={bar}, execBar={execBar}");
                    _pending = null;
                    return;
                }

                // Aquí bar == execBar → toca ejecutar en N+1
                DebugLog.W("468/STR", $"PROCESSING PENDING @N+1: bar={bar}, execBar={execBar}");
                var s = _pending.Value;

                // hard anti-dup by UID
                if (s.Uid == _lastExecUid)
                {
                    _pending = null;
                    DebugLog.W("468/STR", "SKIP: Already executed this UID");
                    return;
                }

                int dir = s.Dir;
                int qty = Math.Max(1, Quantity);

                // 1) Apertura N+1: estricta (primer tick) con tolerancia por precio
                if (StrictN1Open)
                {
                    if (!IsFirstTickOf(bar))
                    {
                        var openN1 = GetCandle(bar).Open;
                        var lastPx = GetCandle(bar).Close; // precio actual del bar N+1
                        var tol    = Ticks(Math.Max(0, OpenToleranceTicks));
                        if (Math.Abs(lastPx - openN1) > tol)
                        {
                            DebugLog.W("468/STR", $"EXPIRE: missed first tick and |{lastPx-openN1}| > {tol}");
                            _pending = null;
                            return;
                        }
                        DebugLog.W("468/STR", $"First-tick missed but within tolerance ({lastPx}~{openN1}) -> proceed");
                    }
                }

                // 2) Dirección de la vela que cruzó (en N) debe coincidir con la señal
                var sigCandle = GetCandle(s.BarId);
                bool candleDirOk = dir > 0 ? (sigCandle.Close > sigCandle.Open)
                                           : (sigCandle.Close < sigCandle.Open);
                if (!candleDirOk)
                {
                    _pending = null;
                    DebugLog.W("468/STR", "ABORT ENTRY: Candle direction at N does not match signal");
                    return;
                }

                // --- Confluence #1: Pendiente de GenialLine A FAVOR en N+1 (vela de ejecución)
                if (RequireGenialSlope)
                {
                    // CheckGenialSlope ya imprime prev/curr y trend real con la misma serie que usa para decidir
                    bool glOk = CheckGenialSlope(dir, bar);
                    if (!glOk) { _pending = null; DebugLog.W("468/STR", "ABORT ENTRY: Conf#1 failed"); return; }
                }

                // --- Confluencia #2: EMA8 vs Wilder8 en N+1, con tolerancia configurable y lectura segura
                if (RequireEmaVsWilder)
                {
                    bool e8Has = TryGetSeries("EMA 8", bar, out var e8_ind);
                    bool w8Has = TryGetSeries("Wilder 8", bar, out var w8_ind);

                    // Si aún no están listos en el primer tick de N+1, usa N como proxy
                    if (!e8Has && TryGetSeries("EMA 8", bar - 1, out var e8_prev)) { e8_ind = e8_prev; e8Has = true; }
                    if (!w8Has && TryGetSeries("Wilder 8", bar - 1, out var w8_prev)) { w8_ind = w8_prev; w8Has = true; }

                    // Si aún faltan series, calcula localmente con cierres (no "skip as OK")
                    if (!e8Has) e8_ind = EmaFromCloses(8, bar);
                    if (!w8Has) w8_ind = RmaFromCloses(8, bar);

                    decimal tol = Ticks(Math.Max(0, EmaVsWilderToleranceTicks));
                    bool emaOk = (dir > 0) ? (e8_ind >= w8_ind - tol)
                                           : (e8_ind <= w8_ind + tol);
                    DebugLog.W("468/STR", $"CONF#2 (EMA8 vs W8 @N+1) e8={e8_ind:F5}{(e8Has? "[IND]":"[LOCAL]")}  w8={w8_ind:F5}{(w8Has?"[IND]":"[LOCAL]")}  tol={tol:F5} -> {(emaOk ? "OK" : "FAIL")}");
                    if (!emaOk) { _pending = null; DebugLog.W("468/STR", "ABORT ENTRY: Conf#2 failed"); return; }
                }

                // --- Validar solo una posición abierta (opcional) ---
                if (OnlyOnePosition)
                {
                    int net = GetNetPosition();
                    bool inCooldown = EnableCooldown && CooldownBars > 0 && _cooldownUntilBar >= 0 && bar <= _cooldownUntilBar;
                    bool busy = _tradeActive || net != 0 || HasAnyActiveOrders() || inCooldown;
                    DebugLog.W("468/STR", $"GUARD OnlyOnePosition: active={_tradeActive} net={net} activeOrders={CountActiveOrders()} cooldown={(inCooldown ? $"YES(until={_cooldownUntilBar})" : "NO")} -> {(busy ? "BLOCK" : "PASS")}");

                    // Si net=0 pero hay órdenes activas "zombie", CANCELA en broker (no sólo limpiar lista)
                    if (net == 0 && HasAnyActiveOrders())
                    {
                        DebugLog.W("468/STR", "ZOMBIE CANCEL: net=0 but active orders present -> cancelling...");
                        CancelAllLiveActiveOrders();
                        // CRITICAL FIX: NO re-entrar en el mismo ciclo. Esperar a que OnOrderChanged limpie.
                        // Conservar señal pendiente para reevaluación en próximo tick/bar
                        DebugLog.W("468/STR", "ZOMBIE CANCEL done – will re-check entry on next OnCalculate cycle");
                        return; // ← salir y dejar que el próximo OnCalculate reevalúe
                    }

                    if (busy)
                    {
                        _pending = null;
                        DebugLog.W("468/STR", "ABORT ENTRY: OnlyOnePosition guard is active");
                        return;
                    }
                }

                // --- Todas las confluencias OK -> solo market; los brackets se adjuntan post-fill ---
                try
                {
                    SubmitMarket(dir, qty, bar, s.BarId);
                    _lastExecUid = s.Uid;

                    DebugLog.Critical("468/STR", $"ENTRY + BRACKET sent at N+1 bar={bar} (signal N={s.BarId}) dir={(dir>0?"BUY":"SELL")} qty={qty}");
                }
                catch (Exception ex)
                {
                    DebugLog.W("468/STR", "EXEC EX: " + ex);
                }
                finally
                {
                    _pending = null;
                }
            }
        }

        // Mantén sincronizado el estado de órdenes (evita que queden "invisibles")
        protected override void OnOrderChanged(Order order)
        {
            try
            {
                var status = order.Status();             // enum OrderStatus
                var state  = order.State;                // enum OrderStates
                bool isActive = state == OrderStates.Active && status != OrderStatus.Filled && status != OrderStatus.Canceled;

                var comment = order?.Comment ?? "no-comment";
                var before  = _liveOrders.Count;
                DebugLog.W("468/ORD", $"OnOrderChanged: {comment} status={status} state={state} active={isActive} liveCount={before}");

                // Activar candado y colgar brackets post-fill (según net real)
                if ((comment?.StartsWith("468ENTRY:") ?? false)
                    && (status == OrderStatus.Placed || status == OrderStatus.PartlyFilled || status == OrderStatus.Filled))
                {
                    _tradeActive = true;
                    if (!_bracketsPlaced)
                    {
                        int net = Math.Abs(GetNetPosition());
                        if (net > 0 && _entryDir != 0 && _lastSignalBar >= 0)
                        {
                            BuildAndSubmitBracket(_entryDir, net, _lastSignalBar, CurrentBar);
                            _bracketsPlaced = true;
                            DebugLog.W("468/STR", $"BRACKETS ATTACHED (from net={net})");
                        }
                    }
                }

                if (!isActive)
                {
                    int removed = _liveOrders.RemoveAll(o => ReferenceEquals(o, order) || (o?.Comment == order?.Comment));
                    DebugLog.W("468/ORD", $"Removed {removed} from _liveOrders (now {_liveOrders.Count})");
                }

                // Reconciliar siempre: TP/SL deben reflejar el net vivo
                try { ReconcileBracketsWithNet(); } catch { /* best-effort */ }

                // Si estamos planos y ya no hay órdenes vivas, libera el candado
                if (GetNetPosition() == 0 && !HasAnyActiveOrders())
                {
                    _tradeActive = false;
                    _lastFlatBar = CurrentBar;
                    if (EnableCooldown && CooldownBars > 0)
                    {
                        _cooldownUntilBar = CurrentBar + Math.Max(1, CooldownBars);
                        DebugLog.W("468/STR", $"COOLDOWN armed until bar={_cooldownUntilBar} (now={CurrentBar})");
                    }
                    DebugLog.W("468/ORD", "Trade candado RELEASED: net=0 & no active orders");
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/ORD", $"OnOrderChanged ERROR: {ex.Message}");
            }
            base.OnOrderChanged(order);
        }

        // Garantía extra: si el net aparece después, adjunta brackets aquí
        protected override void OnPositionChanged(Position position)
        {
            try
            {
                var sec = position?.GetType().GetProperty("Security")?.GetValue(position);
                if (!Equals(sec, Security)) return;

                int net = Math.Abs(GetNetPosition());
                if (net > 0 && !_bracketsPlaced && _entryDir != 0 && _lastSignalBar >= 0)
                {
                    BuildAndSubmitBracket(_entryDir, net, _lastSignalBar, CurrentBar);
                    _bracketsPlaced = true;
                    DebugLog.W("468/STR", $"BRACKETS ATTACHED (via OnPositionChanged, net={net})");
                }

                // Si quedamos planos y ya no hay órdenes activas, libera candado
                if (net == 0 && !HasAnyActiveOrders())
                {
                    _tradeActive = false;
                    DebugLog.W("468/ORD", "Trade lock RELEASED by OnPositionChanged (net=0 & no active orders)");
                }
            }
            catch { }
            base.OnPositionChanged(position);
        }

        // ====================== BRACKETS - FIXED ======================
        private void BuildAndSubmitBracket(int dir, int totalQty, int signalBar, int execBar)
        {
            if (totalQty <= 0) return;

            var (slPx, tpList) = BuildBracketPrices(dir, signalBar, execBar);
            var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;

            // Crea sólo tantas patas como 'totalQty' (1→TP1, 2→TP1+TP2, 3→TP1+TP2+TP3)
            int legs = Math.Min(totalQty, 3);

            if (legs >= 1 && EnableTP1 && tpList.Count >= 1)
            {
                var o1 = Guid.NewGuid().ToString("N");
                SubmitStop(o1, coverSide, 1, slPx);
                SubmitLimit(o1, coverSide, 1, tpList[0]);
            }
            if (legs >= 2 && EnableTP2 && tpList.Count >= 2)
            {
                var o2 = Guid.NewGuid().ToString("N");
                SubmitStop(o2, coverSide, 1, slPx);
                SubmitLimit(o2, coverSide, 1, tpList[1]);
            }
            if (legs >= 3 && EnableTP3 && tpList.Count >= 3)
            {
                var o3 = Guid.NewGuid().ToString("N");
                SubmitStop(o3, coverSide, 1, slPx);
                SubmitLimit(o3, coverSide, 1, tpList[2]);
            }

            // If no TPs enabled, just submit a stop loss for full quantity
            if (legs == 0 || (!EnableTP1 && !EnableTP2 && !EnableTP3))
            {
                SubmitStop(null, coverSide, totalQty, slPx);
            }

            DebugLog.W("468/STR", $"BRACKETS: SL={slPx:F2} | TPs={string.Join(",", tpList.Select(x=>x.ToString("F2")))} | Legs={legs}/{totalQty}");
        }

        private (decimal slPx, List<decimal> tpList) BuildBracketPrices(int dir, int signalBar, int execBar)
        {
            // FIXED: Use UseSignalCandleSL property
            var refCandle = UseSignalCandleSL ? GetCandle(signalBar) : GetCandle(execBar);

            decimal sl = dir > 0 ? refCandle.Low  - Ticks(Math.Max(0, StopOffsetTicks))
                                 : refCandle.High + Ticks(Math.Max(0, StopOffsetTicks));
            sl = RoundToTick(sl);

            // Entrada de referencia: apertura de N+1 (o close de N como fallback)
            decimal entryPx;
            try
            {
                // Intentar la apertura de la vela de ejecución
                if (execBar <= CurrentBar)
                {
                    entryPx = GetCandle(execBar).Open;
                }
                else
                {
                    // Fallback: cierre de N si exec aún no existe
                    entryPx = GetCandle(signalBar).Close;
                }
            }
            catch
            {
                // Ultimate fallback
                entryPx = GetCandle(signalBar).Close;
            }

            if (entryPx <= 0) entryPx = GetCandle(signalBar).Close;

            decimal risk = Math.Abs(entryPx - sl);
            if (risk <= 0) risk = Ticks(2);

            var tps = new List<decimal>();
            if (EnableTP1) tps.Add(RoundToTick(dir > 0 ? entryPx + TP1_R * risk : entryPx - TP1_R * risk));
            if (EnableTP2) tps.Add(RoundToTick(dir > 0 ? entryPx + TP2_R * risk : entryPx - TP2_R * risk));
            if (EnableTP3) tps.Add(RoundToTick(dir > 0 ? entryPx + TP3_R * risk : entryPx - TP3_R * risk));

            DebugLog.W("468/STR", $"BRACKET-PRICES: entry~{entryPx:F2} sl={sl:F2} risk={risk:F2}");
            return (sl, tps);
        }

        private List<int> SplitQtyForTPs(int totalQty, int nTps)
        {
            var q = new List<int>();
            if (nTps <= 0) { q.Add(totalQty); return q; }
            int baseQ = Math.Max(1, totalQty / nTps);
            int rem = totalQty - baseQ * nTps;
            for (int i = 0; i < nTps; i++) q.Add(baseQ + (i < rem ? 1 : 0));
            return q;
        }

        // FIXED: Implement missing ElementAtOrDefault method
        private static T GetElementAtOrDefault<T>(List<T> list, int index, T defaultValue)
        {
            return (index >= 0 && index < list.Count) ? list[index] : defaultValue;
        }

        // ====================== ORDER WRAPPERS ======================
        private void SubmitMarket(int dir, int qty, int bar, int signalBar)
        {
            // *** CRITICAL DEBUG ***
            DebugLog.Critical("468/STR", $"SubmitMarket CALLED: dir={dir} qty={qty} bar={bar} t={GetCandle(bar).Time:HH:mm:ss} - THIS IS OUR N+1 EXECUTION");

            var order = new Order
            {
                Portfolio = Portfolio,
                Security  = Security,
                Direction = dir > 0 ? OrderDirections.Buy : OrderDirections.Sell,
                Type      = OrderTypes.Market,
                QuantityToFill = qty,
                Comment = $"468ENTRY:{DateTime.UtcNow:HHmmss}"
            };
            OpenOrder(order);
            _liveOrders.Add(order);

            // Track signal context for post-fill brackets
            _targetQty = qty;
            _lastSignalBar = signalBar;
            _entryDir = dir;
            _bracketsPlaced = false;

            DebugLog.Critical("468/STR", $"MARKET ORDER SENT: {(dir>0?"BUY":"SELL")} {qty} at N+1 (bar={bar}) - OpenOrder() called successfully");
        }

        private void SubmitLimit(string oco, OrderDirections side, int qty, decimal px)
        {
            var order = new Order
            {
                Portfolio = Portfolio,
                Security  = Security,
                Direction = side,
                Type      = OrderTypes.Limit,
                Price     = px,
                QuantityToFill = qty,
                OCOGroup  = oco,
                AutoCancel = true,
                IsAttached = true,
                Comment   = $"468TP:{DateTime.UtcNow:HHmmss}:{(oco!=null?oco.Substring(0,Math.Min(6,oco.Length)):"nooco")}"
            };
            OpenOrder(order);
            _liveOrders.Add(order);
            DebugLog.W("468/ORD", $"LIMIT submitted: {side} {qty} @{px:F2} OCO={(oco??"none")}");
        }

        private void SubmitStop(string oco, OrderDirections side, int qty, decimal triggerPx)
        {
            var order = new Order
            {
                Portfolio = Portfolio,
                Security  = Security,
                Direction = side,
                Type      = OrderTypes.Stop,
                TriggerPrice = triggerPx,
                QuantityToFill = qty,
                OCOGroup = oco,
                AutoCancel = true,
                IsAttached = true,
                Comment = $"468SL:{DateTime.UtcNow:HHmmss}:{(oco!=null?oco.Substring(0,Math.Min(6,oco.Length)):"nooco")}"
            };
            OpenOrder(order);
            _liveOrders.Add(order);
            DebugLog.W("468/ORD", $"STOP submitted: {side} {qty} @{triggerPx:F2} OCO={(oco??"none")}");
        }

        // ====================== HELPERS - FIXED ======================
        // FIXED: Use override instead of new to properly override base property
        protected virtual decimal InternalTickSize
        {
            get
            {
                try
                {
                    var instProp = GetType().GetProperty("InstrumentInfo",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var instVal = instProp?.GetValue(this);
                    var step = (decimal?)(instVal?.GetType().GetProperty("MinStep")?.GetValue(instVal)) ?? 0m;
                    if (step > 0m) return step;

                    var secVal = GetType().GetProperty("Security",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    )?.GetValue(this);
                    step = (decimal?)(secVal?.GetType().GetProperty("MinStep")?.GetValue(secVal)) ?? 0m;
                    return step > 0m ? step : 0.25m;
                }
                catch { return 0.25m; }
            }
        }

        private decimal Ticks(int n) => n * InternalTickSize;
        private decimal CloseAt(int i) => GetCandle(SafeBarIndex(i)).Close;
        private decimal RoundToTick(decimal price)
        {
            var steps = Math.Round(price / InternalTickSize, MidpointRounding.AwayFromZero);
            return steps * InternalTickSize;
        }

        // FIXED: Add safe bar index method
        private int SafeBarIndex(int i)
        {
            return Math.Max(0, Math.Min(i, CurrentBar));
        }

        private int GetNetPosition()
        {
            // Vía 1: Portfolio.GetPosition(Security)
            try
            {
                var portfolio = GetType().GetProperty("Portfolio", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this);
                var security  = GetType().GetProperty("Security",  BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this);
                var getPos    = portfolio?.GetType().GetMethod("GetPosition", new[] { security?.GetType() });
                var pos       = (getPos != null && security != null) ? getPos.Invoke(portfolio, new[] { security }) : null;
                var qProp     = pos?.GetType().GetProperty("NetQuantity") ?? pos?.GetType().GetProperty("NetPosition") ?? pos?.GetType().GetProperty("Quantity");
                if (qProp != null) return Convert.ToInt32(Math.Truncate(Convert.ToDecimal(qProp.GetValue(pos))));
            }
            catch { /* ignore */ }
            // Vía 2: enumerar Positions y filtrar por Security actual
            try
            {
                var positions = GetType().GetProperty("Positions", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this) as IEnumerable;
                var mySec     = GetType().GetProperty("Security",  BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this);
                if (positions != null && mySec != null)
                {
                    foreach (var p in positions)
                    {
                        var sec  = p.GetType().GetProperty("Security")?.GetValue(p);
                        if (!Equals(sec, mySec)) continue;
                        var qProp= p.GetType().GetProperty("NetQuantity") ?? p.GetType().GetProperty("NetPosition") ?? p.GetType().GetProperty("Quantity");
                        if (qProp == null) continue;
                        var qty  = Convert.ToInt32(Math.Truncate(Convert.ToDecimal(qProp.GetValue(p))));
                        if (qty != 0) return qty;
                    }
                }
            }
            catch { /* ignore */ }
            return 0;
        }

        private bool HasLiveOrders()
        {
            try
            {
                foreach (var o in _liveOrders)
                {
                    var state = o?.GetType().GetProperty("State")?.GetValue(o)?.ToString() ?? "";
                    if (!(state.Contains("Filled") || state.Contains("Cancelled") || state.Contains("Rejected")))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private bool HasAnyActiveOrders()
        {
            try
            {
                foreach (var o in _liveOrders)
                {
                    if (o == null) continue;
                    var st = o.Status();
                    var act = o.State;
                    if (act == OrderStates.Active && st != OrderStatus.Filled && st != OrderStatus.Canceled)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private int CountActiveOrders()
        {
            int n = 0;
            try
            {
                foreach (var o in _liveOrders)
                {
                    if (o == null) continue;
                    var st = o.Status();
                    var act = o.State;
                    if (act == OrderStates.Active && st != OrderStatus.Filled && st != OrderStatus.Canceled)
                        n++;
                }
            }
            catch { }
            return n;
        }

        private void CancelAllLiveActiveOrders()
        {
            try
            {
                foreach (var o in _liveOrders)
                {
                    if (o == null) continue;
                    if (o.State == OrderStates.Active && o.Status() != OrderStatus.Filled && o.Status() != OrderStatus.Canceled)
                        CancelOrder(o);
                }
            }
            catch { }
        }

        private decimal GenialAt(int i)
        {
            try
            {
                if (_ind != null)
                {
                    var s = _ind.DataSeries.FirstOrDefault(ds => ds.Name == "GENIAL LINE (c9)");
                    if (s != null && i >= 0 && i < s.Count) return (decimal)s[i];
                }
                return GetCandle(SafeBarIndex(i)).Close; // FIXED: Use safe index
            }
            catch { return GetCandle(SafeBarIndex(i)).Close; }
        }

        private bool TryGetSeries(string name, int i, out decimal v)
        {
            v = 0m;
            try
            {
                if (_ind != null)
                {
                    var s = _ind.DataSeries.FirstOrDefault(ds => ds.Name == name);
                    if (s != null && i >= 0 && i < s.Count)
                    {
                        v = (decimal)s[i];
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        // Mantengo SeriesAt para usos no críticos, pero evita usarlo en confluencias
        private decimal SeriesAt(string name, int i)
        {
            return TryGetSeries(name, i, out var v) ? v : GetCandle(SafeBarIndex(i)).Close;
        }

        private bool CheckGenialSlope(int dir, int i)
        {
            if (i < 1) return true; // muy al principio, sé permisivo

            // Leer SIEMPRE de la serie de GenialLine; si N+1 aún no está, usa N.
            bool hasN1 = TryGetSeries("GENIAL LINE (c9)", i, out var gN1);
            bool hasN  = TryGetSeries("GENIAL LINE (c9)", i - 1, out var gN);

            // Fallback coherente: si falta N+1, compara N vs N-1; si falta N, aborta con true (no bloquear al inicio)
            if (!hasN1 && hasN)
            {
                // Mueve la ventana una vela atrás: usa N como "actual" y N-1 como "anterior"
                bool hasNm1 = TryGetSeries("GENIAL LINE (c9)", i - 2, out var gNm1);
                if (!hasNm1) return true;
                gN1 = gN; gN = gNm1;  // gN1=actual(N), gN=anterior(N-1)
                DebugLog.W("468/STR", $"CONF#1 (GL slope) using N/N-1 (series not ready at N+1)");
            }
            else if (!hasN1 && !hasN)
            {
                return true;
            }

            bool up   = gN1 > gN;
            bool down = gN1 < gN;
            bool ok = dir > 0 ? up : down; // BUY exige subiendo; SELL exige bajando (estricto)

            DebugLog.W("468/STR", $"CONF#1 (GL slope @N+1) gN={gN:F5} gN1={gN1:F5} " +
                                   $"trend={(up? "UP": (down? "DOWN":"FLAT"))} -> {(ok? "OK":"FAIL")}");
            return ok;
        }

        // === Posición abierta: consulta en vivo (robusto a reinicios y cierres manuales) ===
        private bool HasOpenPosition()
        {
            try
            {
                var portfolio = GetType().GetProperty("Portfolio",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(this);
                var security  = GetType().GetProperty("Security",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(this);

                DebugLog.W("468/POS", $"HasOpenPosition: portfolio={(portfolio != null ? "OK" : "NULL")} security={(security != null ? "OK" : "NULL")}");

                if (portfolio != null && security != null)
                {
                    var getPos = portfolio.GetType().GetMethod("GetPosition", new[] { security.GetType() });
                    DebugLog.W("468/POS", $"GetPosition method: {(getPos != null ? "FOUND" : "NOT_FOUND")}");

                    if (getPos != null)
                    {
                        var pos = getPos.Invoke(portfolio, new[] { security });
                        DebugLog.W("468/POS", $"Position object: {(pos != null ? "OK" : "NULL")}");

                        if (pos != null)
                        {
                            var qProp = pos.GetType().GetProperty("NetQuantity")
                                        ?? pos.GetType().GetProperty("NetPosition")
                                        ?? pos.GetType().GetProperty("Quantity");

                            DebugLog.W("468/POS", $"Quantity property: {(qProp != null ? qProp.Name : "NOT_FOUND")}");

                            if (qProp != null)
                            {
                                var qty = Convert.ToDecimal(qProp.GetValue(pos));
                                DebugLog.W("468/POS", $"Current quantity: {qty}");
                                if (qty != 0) return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/POS", "HasOpenPosition EX: " + ex.Message);
            }
            DebugLog.W("468/POS", "HasOpenPosition: NO POSITION DETECTED");
            return false;
        }

        // Cancela TPs sobrantes y asegura que el/los SL cubran exactamente el net vivo
        private void ReconcileBracketsWithNet()
        {
            int net = Math.Abs(GetNetPosition());
            if (net < 0) net = 0;

            // TPs activos de esta estrategia
            var tps = _liveOrders
                .Where(o => o != null && (o.Comment?.StartsWith("468TP:") ?? false)
                       && o.State == OrderStates.Active && o.Status() != OrderStatus.Filled && o.Status() != OrderStatus.Canceled)
                .ToList();

            // Si hay más TPs que net -> cancelar sobrantes
            if (tps.Count > net)
            {
                // dejar los más cercanos primero (ordenar por Price asc. es suficiente para BUY)
                var toCancel = tps.OrderBy(o => o.Price).Skip(net).ToList();
                foreach (var o in toCancel)
                    try { CancelOrder(o); } catch { }
            }

            // Stops activos
            var sls = _liveOrders
                .Where(o => o != null && (o.Comment?.StartsWith("468SL:") ?? false)
                       && o.State == OrderStates.Active && o.Status() != OrderStatus.Filled && o.Status() != OrderStatus.Canceled)
                .ToList();
            int slQty = 0;
            foreach (var s in sls) { try { slQty += (int)s.QuantityToFill; } catch { } }

            // Si la suma de SL != net -> simplificar a un único SL = net
            if (slQty != net)
            {
                foreach (var s in sls) { try { CancelOrder(s); } catch { } }
                if (net > 0 && _lastSignalBar >= 0 && _entryDir != 0)
                {
                    var (slPx, _) = BuildBracketPrices(_entryDir, _lastSignalBar, CurrentBar);
                    var coverSide = _entryDir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                    var oco = Guid.NewGuid().ToString();
                    SubmitStop(oco, coverSide, net, slPx);
                }
            }
        }

        // === Cálculo local para evitar races de N+1 ===
        private decimal EmaFromCloses(int len, int bar)
        {
            if (len <= 1) return GetCandle(SafeBarIndex(bar)).Close;
            decimal k = 2m / (len + 1m);
            decimal ema = GetCandle(0).Close;
            int last = SafeBarIndex(bar);
            for (int i = 1; i <= last; i++)
                ema = ema + k * (GetCandle(i).Close - ema);
            return ema;
        }

        private decimal RmaFromCloses(int len, int bar)
        {
            if (len <= 1) return GetCandle(SafeBarIndex(bar)).Close;
            decimal rma = GetCandle(0).Close;
            int last = SafeBarIndex(bar);
            for (int i = 1; i <= last; i++)
                rma = (rma * (len - 1) + GetCandle(i).Close) / len;
            return rma;
        }
    }
}