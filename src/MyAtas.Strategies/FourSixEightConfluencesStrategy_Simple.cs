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
    public enum EmaWilderRule { Strict, Inclusive, Window }
    public enum AntiFlatMode { TimeOnly, BarsOnly, Hybrid }

    // ====================== RISK MANAGEMENT ENUMS ======================
    public enum PositionSizingMode { Manual, FixedRiskUSD, PercentOfAccount }
    public enum BreakevenMode { Disabled, Manual, OnTPFill }

    [DisplayName("468 – Simple Strategy (GL close + 2 confluences) - FIXED")]
    public partial class FourSixEightSimpleStrategy : ChartStrategy
    {
        // --- Helper: net por suma de fills (BUY=+1, SELL=-1) ---
        private int NetByFills()
        {
            try { return _orderFills?.Values.Sum() ?? 0; }
            catch { return 0; }
        }

        // *** PHANTOM FIX: Registro robusto de signo por orden hija ***
        private void RegisterChildSign(string orderId, int dir, bool isEntry)
        {
            // Convención de signos:
            //  - ENTRY aporta dir * qty al netByFills (abre posición)
            //  - TP/SL aportan -dir * qty (cierran/reducen)
            int sign = isEntry ? dir : -dir;
            if (!string.IsNullOrEmpty(orderId))
            {
                _childSign[orderId] = sign;
                DebugLog.W("468/POS", $"RegisterChildSign: {orderId} = {sign} (dir={dir} isEntry={isEntry})");
            }
        }

        // *** PHANTOM FIX: Limpieza atómica tras confirmación de flat ***
        private void AtomicFlatCleanup(string reason)
        {
            _tradeActive = false;
            _bracketsPlaced = false;
            _bracketsAttachedAt = DateTime.MinValue;
            _antiFlatUntilBar = -1;
            _flatStreak = 0;
            _lastFlatRead = DateTime.MinValue;
            _orderFills.Clear();
            _childSign.Clear();
            _cachedNetPosition = 0;
            _breakevenApplied = false;
            _entryPrice = 0m;
            _beLastTouchBar = -1;
            _beLastTouchAt = DateTime.MinValue;
            DebugLog.W("468/POS", $"ATOMIC FLAT CLEANUP: {reason} → cleared fills/cache/childSign");
        }

        // --- Helper: ¿estado terminal? (no deben contarse como activos) ---
        private static bool IsTerminal(OrderStatus s)
            => s == OrderStatus.Filled
            || s == OrderStatus.Canceled;

        // --- Helper: ¿orden hija viva (TP/SL) con prefijo concreto? ---
        private bool IsLiveChild(Order o, string prefix)
        {
            if (o == null) return false;
            var comment = o.Comment ?? string.Empty;
            if (!comment.StartsWith(prefix)) return false;
            // Null-safe al leer Status() (algunos conectores pueden devolverlo nulo en microventanas)
            var status = o.Status();
            if (status != null && IsTerminal(status)) return false;
            if (status == null) { /* conservador: no lo trates como terminal */ }
            // Solo considerar activas las órdenes con State=Active (coherente con API ATAS)
            return o.State == OrderStates.Active;
        }

        // --- Helper: cuenta TPs activos ---
        private int CountActiveTPs()
        {
            try { return _liveOrders?.Count(o => IsLiveChild(o, "468TP:")) ?? 0; }
            catch { return 0; }
        }

        // --- Helper: cuenta SLs activos ---
        private int CountActiveSLs()
        {
            try { return _liveOrders?.Count(o => IsLiveChild(o, "468SL:")) ?? 0; }
            catch { return 0; }
        }

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

        [Category("Confluences"), DisplayName("EMA vs Wilder rule")]
        public EmaWilderRule EmaVsWilderMode { get; set; } = EmaWilderRule.Window;

        [Category("Confluences"), DisplayName("EMA vs Wilder pre-cross tolerance (ticks)")]
        public int EmaVsWilderPreTolTicks { get; set; } = 1;

        [Category("Confluences"), DisplayName("EMA vs Wilder: count equality as pass")]
        public bool EmaVsWilderAllowEquality { get; set; } = true;

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

        // --- Anti-flat window (para evitar cancelaciones por net=0 fantasma justo tras colgar brackets)
        [Category("Execution"), DisplayName("Anti-flat lock (ms)")]
        public int AntiFlatMs { get; set; } = 600;

        [Category("Execution"), DisplayName("Anti-flat mode")]
        public AntiFlatMode AntiFlatPolicy { get; set; } = AntiFlatMode.Hybrid;

        [Category("Execution"), DisplayName("Anti-flat bars (confirm flat after N bars)")]
        public int AntiFlatBars { get; set; } = 1;

        [Category("Execution"), DisplayName("Confirm flat reads (consecutive)")]
        public int ConfirmFlatReads { get; set; } = 3;

        [Category("Execution"), DisplayName("Reattach brackets if missing")]
        public bool ReattachIfMissing { get; set; } = true;

        // --- Advanced bracket controls ---
        [Category("Execution"), DisplayName("AutoCancel on TP/SL orders")]
        public bool EnableAutoCancel { get; set; } = false;

        [Category("Execution"), DisplayName("Enable bracket reconciliation")]
        public bool EnableReconciliation { get; set; } = true;

        // --- Risk/Timing ---
        [Category("Risk/Timing"), DisplayName("Enable cooldown after flat")]
        public bool EnableCooldown { get; set; } = true;

        [Category("Risk/Timing"), DisplayName("Cooldown bars after flat")]
        public int CooldownBars { get; set; } = 2;

        [Category("Risk/Timing"), DisplayName("Enable flat watchdog failsafe")]
        public bool EnableFlatWatchdog { get; set; } = true;

        // ====================== RISK MANAGEMENT PARAMETERS ======================

        // --- Position Sizing --- (HIDDEN: Will be migrated to external Risk Manager)
        [Browsable(false)] // Hidden: External RM will handle position sizing
        [Category("Risk Management/Position Sizing"), DisplayName("Position Sizing Mode")]
        public PositionSizingMode PositionSizingMode { get; set; } = PositionSizingMode.Manual;

        [Browsable(false)]
        [Category("Risk Management/Position Sizing"), DisplayName("Risk per trade (USD)")]
        public decimal RiskPerTradeUsd { get; set; } = 100.0m;

        [Browsable(false)]
        [Category("Risk Management/Position Sizing"), DisplayName("Risk % of account")]
        public decimal RiskPercentOfAccount { get; set; } = 0.5m;

        [Browsable(false)]
        [Category("Risk Management/Position Sizing"), DisplayName("Manual account equity override")]
        public decimal ManualAccountEquityOverride { get; set; } = 0.0m;

        [Browsable(false)]
        [Category("Risk Management/Position Sizing"), DisplayName("Tick value overrides (SYM=V)")]
        public string TickValueOverrides { get; set; } = "MNQ=0.5;NQ=5;MES=1.25;ES=12.5";

        [Browsable(false)]
        [Category("Risk Management/Position Sizing"), DisplayName("Enable detailed risk logging")]
        public bool EnableDetailedRiskLogging { get; set; } = false;

        // --- Breakeven ---
        [Category("Risk Management/Breakeven"), DisplayName("Breakeven mode")]
        public BreakevenMode BreakevenMode { get; set; } = BreakevenMode.Disabled;

        [Category("Risk Management/Breakeven"), DisplayName("Breakeven offset (ticks)")]
        public int BreakevenOffsetTicks { get; set; } = 4;

        [Category("Risk Management/Breakeven"), DisplayName("Trigger breakeven manually")]
        public bool TriggerBreakevenManually { get; set; } = false;

        [Category("Risk Management/Breakeven"), DisplayName("Trigger on TP1 touch/fill")]
        public bool TriggerOnTP1TouchFill { get; set; } = true;

        [Category("Risk Management/Breakeven"), DisplayName("Trigger on TP2 touch/fill")]
        public bool TriggerOnTP2TouchFill { get; set; } = false;

        [Category("Risk Management/Breakeven"), DisplayName("Trigger on TP3 touch/fill")]
        public bool TriggerOnTP3TouchFill { get; set; } = false;

        // --- Diagnostics (Read-only) ---
        [Category("Risk Management/Diagnostics"), DisplayName("Effective tick value (USD/tick)")]
        [ReadOnly(true)]
        public decimal EffectiveTickValue { get; private set; } = 0.5m;

        [Category("Risk Management/Diagnostics"), DisplayName("Effective tick size (points/tick)")]
        [ReadOnly(true)]
        public decimal EffectiveTickSize { get; private set; } = 0.25m;

        [Category("Risk Management/Diagnostics"), DisplayName("Effective account equity (USD)")]
        [ReadOnly(true)]
        public decimal EffectiveAccountEquity { get; private set; } = 10000.0m;

        // ====================== EXTERNAL RISK MANAGEMENT INTEGRATION ======================
        [Category("Risk Management/Integration"), DisplayName("External risk controls SL/Trail")]
        public bool ExternalRiskControlsStops { get; set; } = false;

        // TODO: Future integration points when connecting external RM:
        // 1. Position sizing calculations (delegate to external RM)
        // 2. SL placement and trailing logic (ActivateBreakEven early return)
        // 3. Risk-based quantity adjustments
        // 4. Portfolio-level risk limits and exposure checks
        // 5. Cross-strategy position coordination

        // ====================== INTERNAL STATE ======================
        private FourSixEightIndicator _ind;
        private Pending? _pending;           // captured at N (GL-cross close confirmed)
        private Guid _lastUid;               // last indicator UID observed
        private Guid _lastExecUid = Guid.Empty; // last UID executed (true anti-dup)

        // Candado de estado y tracking de órdenes para OnlyOnePosition
        private bool _tradeActive = false;             // true tras enviar la entrada hasta quedar plano y sin órdenes vivas
        private readonly List<Order> _liveOrders = new(); // órdenes creadas por ESTA estrategia y aún activas
        private int _attachInProgress = 0;                // guard de concurrencia para attach/reconcile
        private int _lastReconcileBar = -1;               // throttle de reconciliación (1x por barra)

        // Post-fill bracket control
        private int _targetQty = 0;          // qty solicitada en la entrada
        private int _lastSignalBar = -1;     // N de la señal que originó la entrada
        private int _entryDir = 0;           // +1 BUY / -1 SELL
        private bool _bracketsPlaced = false; // ya colgados los brackets
        private DateTime _bracketsAttachedAt = DateTime.MinValue;
        private int _antiFlatUntilBar = -1;  // bar until which anti-flat protection is active
        private int _flatStreak = 0;         // consecutive net==0 readings
        private DateTime _lastFlatRead = DateTime.MinValue;

        // Enhanced position tracking
        private int _cachedNetPosition = 0;  // cached position for latency issues
        private DateTime _lastPositionUpdate = DateTime.MinValue;
        private readonly Dictionary<string, int> _orderFills = new(); // track our fills
        private readonly Dictionary<string, int> _childSign = new(); // robust sign tracking for fills
        private DateTime _postEntryFlatBlockUntil = DateTime.MinValue; // suprime confirmación de flat tras ENTRY

        // Cooldown management
        private int _cooldownUntilBar = -1;   // bar index hasta el que no se permite re-entrada
        private int _lastFlatBar = -1;        // último bar en el que quedamos planos

        private struct Pending { public Guid Uid; public int BarId; public int Dir; }

        // Detectar primer tick de cada vela
        private int _lastSeenBar = -1;
        private int _lastLoggedBar = -1; // For OnCalculate log throttling
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

                // Attach indicator: search up the inheritance chain for non-public instance method
                System.Reflection.MethodInfo addInd = null;
                var t = this.GetType();
                while (t != null && addInd == null)
                {
                    addInd = t.GetMethod("AddIndicator",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.DeclaredOnly);
                    t = t.BaseType;
                }
                // Optional hard fallback if we know the base type name
                if (addInd == null)
                {
                    addInd = typeof(ChartStrategy).GetMethod(
                        "AddIndicator",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                }
                if (addInd != null)
                {
                    try
                    {
                        addInd.Invoke(this, new object[] { _ind });
                        DebugLog.Critical("468/STR", "INIT OK (Indicator attached via reflection)");
                    }
                    catch (Exception ex)
                    {
                        DebugLog.W("468/STR", "AttachIndicator invoke failed: " + ex.GetBaseException().Message);
                    }
                }
                else
                {
                    DebugLog.W("468/STR", "WARNING: Could not attach indicator (method not found in hierarchy)");
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

            // *** THROTTLED DEBUG: Only log first tick of each bar to prevent spam ***
            if (bar != _lastLoggedBar && IsFirstTickOf(bar))
            {
                _lastLoggedBar = bar;
                DebugLog.W("468/STR", $"OnCalculate: bar={bar} t={GetCandle(bar).Time:HH:mm:ss} pending={(_pending.HasValue ? "YES" : "NO")} tradeActive={_tradeActive}");
                // <<< PATCH 4 (opcional): STATE PING cada barra >>>
                DebugLog.W("468/STR", $"STATE PING: net={GetNetPosition()} activeOrders={CountActiveOrders()} " +
                                       $"antiFlatUntil={_antiFlatUntilBar} cooldownUntil={_cooldownUntilBar} " +
                                       $"brkPlaced={_bracketsPlaced}");
            }

            // --- DEBUG: Estado del pending ---
            if (_pending.HasValue)
            {
                DebugLog.W("468/STR", $"PENDING: bar={bar} pendingBar={_pending.Value.BarId} dir={(_pending.Value.Dir > 0 ? "BUY" : "SELL")} uid={_pending.Value.Uid.ToString().Substring(0, 8)} condition={bar > _pending.Value.BarId}");
            }

            // --- DEBUG: Check for signal availability and CAPTURE AT N ---
            // === SEÑALES: captura en N, ejecución en N+1 ===
            ProcessSignalLogic(bar);

            // --- BE TOUCH: comprobar si se ha tocado algún TP habilitado ---
            try
            {
                if (_tradeActive && _bracketsPlaced && (_entryDir != 0))
                    CheckBreakEvenTouch_OnCalculate(bar);
            }
            catch { /* best-effort */ }

            // --- Reconciliación controlada (1x por barra y fuera de anti-flat) ---
            if (EnableReconciliation && bar != _lastReconcileBar && IsFirstTickOf(bar))
            {
                _lastReconcileBar = bar;
                int netNowAbs = Math.Abs(GetNetPosition());
                bool timeOkR = (_bracketsAttachedAt != DateTime.MinValue) &&
                               (DateTime.UtcNow - _bracketsAttachedAt).TotalMilliseconds >= Math.Max(0, AntiFlatMs);
                bool barsOkR = (_antiFlatUntilBar < 0) || (bar > _antiFlatUntilBar);
                if (netNowAbs > 0 && timeOkR && barsOkR)
                {
                    try { ReconcileBracketsWithNet(); } catch { /* best-effort */ }
                }
            }

            // --- FAILSAFE: Flat watchdog to prevent stuck _tradeActive ---
            if (EnableFlatWatchdog && IsFirstTickOf(bar))
            {
                try
                {
                    int netWD = GetNetPosition();
                    bool timeOk = (_bracketsAttachedAt != DateTime.MinValue) &&
                                  (DateTime.UtcNow - _bracketsAttachedAt).TotalMilliseconds >= Math.Max(0, AntiFlatMs);
                    bool barsOk = (_antiFlatUntilBar < 0) || (bar > _antiFlatUntilBar);

                    if (_tradeActive && netWD == 0 && !HasAnyActiveOrders() && timeOk && barsOk)
                    {
                        _tradeActive = false;
                        _bracketsPlaced = false;
                        _orderFills.Clear();
                        _cachedNetPosition = 0;
                        _bracketsAttachedAt = DateTime.MinValue;
                        _antiFlatUntilBar = -1;
                        // Reset BreakEven state
                        _breakevenApplied = false;
                        _entryPrice = 0m;
                        _beLastTouchBar = -1;
                        _beLastTouchAt = DateTime.MinValue;
                        _flatStreak = 0;
                        _lastFlatRead = DateTime.MinValue;
                        DebugLog.W("468/ORD", "Trade lock RELEASED by watchdog (flat & no active orders)");
                    }
                }
                catch { /* best-effort */ }
            }

            // <<< PATCH 1: HEARTBEAT RELEASE (final) >>>
            // Libera sólo si TAMBIÉN NetByFills()==0 para evitar falsos planos tras parciales (TP1).
            if (_tradeActive && !HasAnyActiveOrders() && GetNetPosition() == 0 && NetByFills() == 0
                && DateTime.UtcNow >= _postEntryFlatBlockUntil)
            {
                DebugLog.W("468/ORD",
                    $"FLAT CONFIRMED (heartbeat): net=0 netByFills=0 tpActive={CountActiveTPs()} slActive={CountActiveSLs()}");
                _tradeActive = false;
                _bracketsPlaced = false;
                _orderFills.Clear();
                _cachedNetPosition = 0;
                _bracketsAttachedAt = DateTime.MinValue;
                _antiFlatUntilBar = -1;
                // Reset BreakEven state
                _breakevenApplied = false;
                _entryPrice = 0m;
                _flatStreak = 0;
                _lastFlatRead = DateTime.MinValue;
                _lastFlatBar = CurrentBar;
                if (EnableCooldown && CooldownBars > 0)
                {
                    _cooldownUntilBar = CurrentBar + Math.Max(1, CooldownBars);
                    DebugLog.W("468/STR", $"COOLDOWN armed until bar={_cooldownUntilBar} (heartbeat)");
                }
                DebugLog.W("468/ORD", "Trade lock RELEASED by heartbeat (flat & no active orders)");
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

                // Track order fills for enhanced position detection
                if (status == OrderStatus.Filled || status == OrderStatus.PartlyFilled)
                {
                    TrackOrderFill(order, "Filled");
                    // Capturar precio de entrada al primer fill de la ENTRY
                    if ((comment.StartsWith("468ENTRY:")))
                    {
                        try { UpdateEntryPriceFromOrder(order); } catch { }
                        _postEntryFlatBlockUntil = DateTime.UtcNow.AddMilliseconds(Math.Max(AntiFlatMs * 2, 800));
                        DebugLog.W("468/ORD", $"POST-ENTRY FLAT BLOCK armed for {Math.Max(AntiFlatMs * 2, 800)}ms");
                    }
                    // Disparar BE por FILL de TP si está configurado
                    try { CheckBreakEvenTrigger_OnOrderChanged(order, status); } catch { }

                    // Failsafe: si un SL se llena, cancela cualquier TP activo (evita LIMITs huérfanos)
                    if ((comment.StartsWith("468SL:")) && (status == OrderStatus.Filled))
                    {
                        int cancelled = 0;
                        try
                        {
                            foreach (var o in _liveOrders)
                            {
                                if (o == null) continue;
                                var st2 = o.Status();
                                if ((o.Comment?.StartsWith("468TP:") ?? false)
                                    && o.State == OrderStates.Active
                                    && st2 != OrderStatus.Filled
                                    && st2 != OrderStatus.Canceled)
                                {
                                    try { CancelOrder(o); cancelled++; } catch { }
                                }
                            }
                        }
                        catch { }
                        DebugLog.W("468/ORD", $"SL filled -> TP failsafe CANCEL ALL: {cancelled}");
                    }
                }
                else if (status == OrderStatus.Canceled)
                {
                    TrackOrderFill(order, "Canceled");
                }

                // Activar candado y colgar brackets post-fill (según net real)
                if ((comment?.StartsWith("468ENTRY:") ?? false)
                    && (status == OrderStatus.Placed || status == OrderStatus.PartlyFilled || status == OrderStatus.Filled))
                {
                    _tradeActive = true;
                    if (!_bracketsPlaced)
                    {
                        int net = Math.Abs(GetNetPosition());
                        DebugLog.W("468/STR", $"POST-FILL CHECK: net={net} _entryDir={_entryDir} _lastSignalBar={_lastSignalBar} status={status}");

                        // Fallbacks robustos si el portfolio aún no reflejó la posición (incluye parciales)
                        if (net == 0)
                        {
                            int filled = GetFilledQtyFromOrder(order);   // ← nueva función
                            if (filled > 0)
                            {
                                net = filled;
                                DebugLog.W("468/STR", $"FALLBACK: Using FilledQuantity={net}");
                            }
                            else if (status == OrderStatus.Filled)
                            {
                                net = (int)Math.Abs(order.QuantityToFill);
                                DebugLog.W("468/STR", $"FALLBACK: Using order.QuantityToFill={net}");
                            }
                        }

                        if (Math.Abs(net) > 0 && _entryDir != 0 && _lastSignalBar >= 0)
                        {
                            if (System.Threading.Interlocked.Exchange(ref _attachInProgress, 1) == 0)
                            {
                                try { BuildAndSubmitBracket(_entryDir, net, _lastSignalBar, CurrentBar); }
                                finally { _attachInProgress = 0; }
                            }
                            else
                            {
                                DebugLog.W("468/STR", "SKIP attach: another attach in progress");
                            }
                            _bracketsPlaced = true;
                            _bracketsAttachedAt = DateTime.UtcNow;
                            _antiFlatUntilBar = CurrentBar + Math.Max(0, AntiFlatBars);
                            _flatStreak = 0;

                            // --- STATE tras adjuntar brackets ---
                            DebugLog.W("468/STR",
                                $"STATE: tradeActive={_tradeActive} " +
                                $"dir={_entryDir} net={GetNetPosition()} netByFills={NetByFills()} " +
                                $"tpActive={CountActiveTPs()} slActive={CountActiveSLs()} " +
                                $"liveOrders={(_liveOrders?.Count ?? 0)} " +
                                $"signalBar={_lastSignalBar} antiFlatMs={AntiFlatMs} confirmFlatReads={ConfirmFlatReads}");
                            _lastFlatRead = DateTime.MinValue;
                            DebugLog.W("468/STR", $"BRACKETS ATTACHED (from net={net})");
                        }
                        else
                        {
                            DebugLog.W("468/STR", $"BRACKETS NOT ATTACHED: net={net} dir={_entryDir} bar={_lastSignalBar}");
                        }
                    }
                    else
                    {
                        DebugLog.W("468/STR", $"BRACKETS ALREADY PLACED: _bracketsPlaced=true");
                    }
                }

                if (!isActive)
                {
                    int removed = _liveOrders.RemoveAll(o => ReferenceEquals(o, order) || (o?.Comment == order?.Comment));
                    DebugLog.W("468/ORD", $"Removed {removed} from _liveOrders (now {_liveOrders.Count})");
                }

                // Failsafe tras cualquier cancelación manual de 468TP: o 468SL:
                if (status == OrderStatus.Canceled && (comment.StartsWith("468TP:") || comment.StartsWith("468SL:")))
                {
                    if (_tradeActive)
                    {
                        var netAfterCancel = GetNetPosition();

                        // Caso A: net=0 y solo quedan hijos -> cancelarlos y liberar
                        if (netAfterCancel == 0 && HasAnyActiveOrders())
                        {
                            DebugLog.W("468/ORD", "Manual cancel detected -> zombie children cleanup");
                            CancelAllLiveActiveOrders();
                        }

                        // Caso B: net=0 y ya no quedan órdenes -> libera candado
                        if (netAfterCancel == 0 && !HasAnyActiveOrders())
                            ReleaseTradeLock("manual cancel -> flat & no active orders");
                    }
                }

                // IMPORTANTE: No reconciliar en OnOrderChanged para evitar ráfagas/reentradas.
                // La reconciliación se hace en OnCalculate, 1x por barra, fuera de anti-flat.

                // Plano: confirma con nueva lógica híbrida
                int netNow = GetNetPosition();
                if (DateTime.UtcNow >= _postEntryFlatBlockUntil && FlatConfirmedNow(netNow))
                {
                    if (HasAnyActiveOrders())
                    {
                        DebugLog.W("468/ORD", "FLAT CONFIRMED: cancelling remaining children...");
                        CancelAllLiveActiveOrders();
                    }
                    if (!HasAnyActiveOrders())
                    {
                        // *** PHANTOM FIX: Limpieza atómica completa ***
                        AtomicFlatCleanup("flat confirmed & no active orders");
                        _lastFlatBar = CurrentBar;
                        if (EnableCooldown && CooldownBars > 0)
                        {
                            _cooldownUntilBar = CurrentBar + Math.Max(1, CooldownBars);
                            DebugLog.W("468/STR", $"COOLDOWN armed until bar={_cooldownUntilBar} (now={CurrentBar})");
                        }
                    }
                }
                else if (netNow == 0)
                {
                    DebugLog.W("468/ORD", $"ANTI-FLAT: net=0 detected but not confirmed yet (streak={_flatStreak}, policy={AntiFlatPolicy})");
                }

                // <<< PHANTOM FIX: RELEASE INCONDICIONAL AL FINAL DE OnOrderChanged >>>
                // *** CRÍTICO: Este es el lugar donde detectamos el net fantasma tras BE ***
                if (_tradeActive && netNow == 0 && !HasAnyActiveOrders() && NetByFills() == 0)
                {
                    DebugLog.W("468/ORD",
                        $"FLAT CONFIRMED (OnOrderChanged): net=0 netByFills=0 tpActive={CountActiveTPs()} slActive={CountActiveSLs()}");
                    // *** PHANTOM FIX: Limpieza atómica completa ***
                    AtomicFlatCleanup("OnOrderChanged final check");
                    _lastFlatBar = CurrentBar;
                    if (EnableCooldown && CooldownBars > 0)
                        _cooldownUntilBar = CurrentBar + Math.Max(1, CooldownBars);
                }

                // Self-heal eliminado aquí. El re-attach sólo se gestiona en OnCalculate (1x/bar, fuera de anti-flat).
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
                    if (System.Threading.Interlocked.Exchange(ref _attachInProgress, 1) == 0)
                    {
                        try { BuildAndSubmitBracket(_entryDir, net, _lastSignalBar, CurrentBar); }
                        finally { _attachInProgress = 0; }
                    }
                    else
                    {
                        DebugLog.W("468/STR", "SKIP attach (OnPositionChanged): another attach in progress");
                    }
                    _bracketsPlaced = true;
                    _bracketsAttachedAt = DateTime.UtcNow;
                    _antiFlatUntilBar = CurrentBar + Math.Max(0, AntiFlatBars);
                    _flatStreak = 0;

                    // --- STATE tras adjuntar brackets (OnPositionChanged) ---
                    DebugLog.W("468/STR",
                        $"STATE: tradeActive={_tradeActive} " +
                        $"dir={_entryDir} net={GetNetPosition()} netByFills={NetByFills()} " +
                        $"tpActive={CountActiveTPs()} slActive={CountActiveSLs()} " +
                        $"liveOrders={(_liveOrders?.Count ?? 0)} " +
                        $"signalBar={_lastSignalBar} antiFlatMs={AntiFlatMs} confirmFlatReads={ConfirmFlatReads}");
                    _lastFlatRead = DateTime.MinValue;
                    DebugLog.W("468/STR", $"BRACKETS ATTACHED (via OnPositionChanged, net={net})");
                }

                // Plano: usa nueva lógica híbrida
                if (DateTime.UtcNow >= _postEntryFlatBlockUntil && FlatConfirmedNow(net))
                {
                    if (HasAnyActiveOrders())
                    {
                        DebugLog.W("468/ORD", "FLAT CONFIRMED (pos): cancelling remaining children...");
                        CancelAllLiveActiveOrders();
                    }
                    if (!HasAnyActiveOrders())
                    {
                        _tradeActive = false;
                        _bracketsPlaced = false;
                        _bracketsAttachedAt = DateTime.MinValue;
                        _antiFlatUntilBar = -1;
                        _flatStreak = 0;
                        _lastFlatRead = DateTime.MinValue;
                        // Clear fill tracking cache when truly flat
                        _orderFills.Clear();
                        _cachedNetPosition = 0;
                        // Reset BreakEven state
                        _breakevenApplied = false;
                        _entryPrice = 0m;
                        _beLastTouchBar = -1;
                        _beLastTouchAt = DateTime.MinValue;
                        DebugLog.W("468/ORD", "Trade lock RELEASED by OnPositionChanged (flat confirmed & no active orders)");
                    }
                }
                else if (net == 0)
                {
                    DebugLog.W("468/ORD", $"ANTI-FLAT (pos): net=0 detected but not confirmed yet (streak={_flatStreak}, policy={AntiFlatPolicy})");
                }
            }
            catch { }
            base.OnPositionChanged(position);
        }

        // ====================== BRACKETS (ROBUST) ======================
        // (moved to Execution.cs)

        // (BuildBracketPrices moved to Execution.cs)

        // (SplitQtyForTPs moved to Execution.cs)

        // (GetElementAtOrDefault moved to Execution.cs)

        // ====================== ORDER WRAPPERS ======================
        // (SubmitMarket moved to Execution.cs)

        // (SubmitLimit moved to Execution.cs)

        // (SubmitStop moved to Execution.cs)

        private int GetFilledQtyFromOrder(object order)
        {
            try
            {
                foreach (var name in new[] { "Filled", "FilledQuantity", "Executed", "QtyFilled" })
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Math.Abs(Convert.ToDecimal(p.GetValue(order)));
                    if (v > 0) return (int)Math.Round(v);
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/POS", $"GetFilledQtyFromOrder failed: {ex.Message}");
            }
            DebugLog.W("468/POS", "GetNetPosition: returning 0 (no position found)");
            return 0;
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
            // Strategy 1: Try direct Portfolio access (fastest when it works)
            int portfolioPos = TryGetPositionFromPortfolio();
            if (portfolioPos != 0)
            {
                UpdatePositionCache(portfolioPos, "Portfolio");
                return portfolioPos;
            }

            // Strategy 2: Try Positions enumeration
            int positionsPos = TryGetPositionFromPositions();
            if (positionsPos != 0)
            {
                UpdatePositionCache(positionsPos, "Positions");
                return positionsPos;
            }

            // *** PHANTOM FIX: Si broker dice 0, solo usar fills si seguimos "en trade" ***
            bool live = HasAnyActiveOrders() || _tradeActive;
            if (live)
            {
                // Strategy 3: Use cached position with fill tracking (solo si hay actividad)
                int cachedPos = GetCachedPositionWithFills();
                if (cachedPos != 0)
                {
                    DebugLog.W("468/POS", $"GetNetPosition via Cache+Fills (live={live}): {cachedPos}");
                    return cachedPos;
                }

                // Strategy 4: Sticky cache ONLY while anti-flat protection is active
                if (_cachedNetPosition != 0 && (DateTime.UtcNow < _postEntryFlatBlockUntil || !FlatConfirmedNow(0)))
                {
                    DebugLog.W("468/POS", $"GetNetPosition via StickyCache (anti-flat active): {_cachedNetPosition}");
                    return _cachedNetPosition;
                }
            }

            // *** PHANTOM FIX: Si no hay actividad, sanear residuos ***
            if (!live && (_orderFills.Count > 0 || _cachedNetPosition != 0))
            {
                DebugLog.W("468/POS", $"PHANTOM CLEANUP: broker=0, live=false, clearing fills({_orderFills.Count}) and cache({_cachedNetPosition})");
                _orderFills.Clear();
                _cachedNetPosition = 0;
            }

            DebugLog.W("468/POS", $"GetNetPosition: flat confirmed (portfolio=0 positions=0 live={live})");
            return 0;
        }

        private int TryGetPositionFromPortfolio()
        {
            try
            {
                var portfolio = GetType().GetProperty("Portfolio", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this);
                var security  = GetType().GetProperty("Security",  BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this);

                if (portfolio == null || security == null)
                    return 0;

                var getPos = portfolio.GetType().GetMethod("GetPosition", new[] { security.GetType() });
                var pos = getPos?.Invoke(portfolio, new[] { security });

                if (pos == null)
                    return 0;

                var qProp = pos.GetType().GetProperty("NetQuantity")
                         ?? pos.GetType().GetProperty("NetPosition")
                         ?? pos.GetType().GetProperty("Quantity");

                if (qProp == null)
                    return 0;

                return Convert.ToInt32(Math.Truncate(Convert.ToDecimal(qProp.GetValue(pos))));
            }
            catch (Exception ex)
            {
                DebugLog.W("468/POS", $"Portfolio strategy failed: {ex.Message}");
                return 0;
            }
        }

        private int TryGetPositionFromPositions()
        {
            try
            {
                var positions = GetType().GetProperty("Positions", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this) as IEnumerable;
                var mySec = GetType().GetProperty("Security", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(this);

                if (positions == null || mySec == null)
                    return 0;

                foreach (var p in positions)
                {
                    var sec = p.GetType().GetProperty("Security")?.GetValue(p);
                    if (!Equals(sec, mySec)) continue;

                    var qProp = p.GetType().GetProperty("NetQuantity")
                             ?? p.GetType().GetProperty("NetPosition")
                             ?? p.GetType().GetProperty("Quantity");

                    if (qProp == null) continue;

                    var qty = Convert.ToInt32(Math.Truncate(Convert.ToDecimal(qProp.GetValue(p))));
                    if (qty != 0)
                        return qty;
                }
                return 0;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/POS", $"Positions strategy failed: {ex.Message}");
                return 0;
            }
        }

        private int GetCachedPositionWithFills()
        {
            try
            {
                // Calculate position based on our tracked fills
                int fillsSum = 0;
                foreach (var kvp in _orderFills)
                {
                    fillsSum += kvp.Value;
                }

                // Use fills if we have recent data
                if (fillsSum != 0 && _orderFills.Count > 0)
                {
                    return fillsSum;
                }

                return _cachedNetPosition;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/POS", $"Cache+Fills strategy failed: {ex.Message}");
                return _cachedNetPosition;
            }
        }

        private void UpdatePositionCache(int position, string source)
        {
            _cachedNetPosition = position;
            _lastPositionUpdate = DateTime.UtcNow;
            DebugLog.W("468/POS", $"Position cache updated: {position} (source: {source})");
        }

        private void TrackOrderFill(Order order, string action)
        {
            try
            {
                var c = order?.Comment ?? "";
                if (!(c.StartsWith("468ENTRY:") || c.StartsWith("468TP:") || c.StartsWith("468SL:")))
                    return;

                var orderId = order.Comment;

                // Use FilledQuantity/Executed first, fallback to QuantityToFill
                var qFilledProp = order.GetType().GetProperty("FilledQuantity")
                               ?? order.GetType().GetProperty("Executed")
                               ?? order.GetType().GetProperty("QtyFilled");
                decimal qFilled = 0m;
                if (qFilledProp != null)
                    qFilled = Convert.ToDecimal(qFilledProp.GetValue(order));
                else
                    qFilled = Convert.ToDecimal(order.QuantityToFill); // último recurso

                var qty = (int)Math.Abs(Math.Truncate(qFilled));

                // *** PHANTOM FIX: Usar registro determinista del signo ***
                int sign;
                if (!_childSign.TryGetValue(orderId, out sign))
                {
                    // Fallback: usa _entryDir si existe; por defecto considera que
                    // si no es entry, entonces cierra (-_entryDir).
                    int dir = _entryDir; // -1 short, +1 long
                    bool isEntry = c.StartsWith("468ENTRY:");
                    sign = isEntry ? dir : -dir;
                    DebugLog.W("468/POS", $"TrackOrderFill FALLBACK sign: {orderId} = {sign} (dir={dir} isEntry={isEntry})");
                }

                var netQty = qty * sign;

                if (action == "Filled")
                {
                    _orderFills[orderId] = netQty;
                    DebugLog.W("468/POS", $"Tracked fill: {orderId} = {netQty} (type: {c.Substring(0,8)})");
                    DebugLog.W("468/POS", $"FILL SNAPSHOT: netByFills={NetByFills()} | liveOrders={(_liveOrders?.Count ?? 0)}");
                    if (c.StartsWith("468ENTRY:"))
                    {
                        _postEntryFlatBlockUntil = DateTime.UtcNow.AddMilliseconds(Math.Max(AntiFlatMs * 2, 800));
                        DebugLog.W("468/ORD", $"POST-ENTRY FLAT BLOCK armed (TrackFill) for {Math.Max(AntiFlatMs * 2, 800)}ms");
                    }
                }
                else if (action == "Canceled")
                {
                    _orderFills.Remove(orderId);
                    _childSign.Remove(orderId); // también limpiar signo
                    DebugLog.W("468/POS", $"Removed fill tracking: {orderId}");
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/POS", $"TrackOrderFill failed: {ex.Message}");
            }
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

        private bool WithinAntiFlatWindow()
        {
            if (_bracketsAttachedAt == DateTime.MinValue) return false;
            var ms = Math.Max(0, AntiFlatMs);
            return (DateTime.UtcNow - _bracketsAttachedAt).TotalMilliseconds < ms;
        }

        private bool FlatConfirmedNow(int netNow)
        {
            // Nunca confirmes 'flat' si por fills queda posición
            if (NetByFills() != 0)
                return false;

            // 1) Actualiza racha de lecturas planas
            if (netNow == 0)
            {
                if (_lastFlatRead == DateTime.MinValue || (DateTime.UtcNow - _lastFlatRead).TotalMilliseconds >= 50)
                {
                    _flatStreak++;
                    _lastFlatRead = DateTime.UtcNow;
                }
            }
            else
            {
                _flatStreak = 0;
                _lastFlatRead = DateTime.MinValue;
            }

            // 2) Condiciones de tiempo/barras según política
            bool timeOk = _bracketsAttachedAt != DateTime.MinValue &&
                         (DateTime.UtcNow - _bracketsAttachedAt).TotalMilliseconds >= Math.Max(0, AntiFlatMs);
            bool barsOk = (AntiFlatBars <= 0) || (CurrentBar > _antiFlatUntilBar);

            bool policyOk = AntiFlatPolicy switch
            {
                AntiFlatMode.TimeOnly => timeOk,
                AntiFlatMode.BarsOnly => barsOk,
                _ => (timeOk && barsOk) // Hybrid
            };

            // Endurecer: mínimo 3 lecturas coherentes
            bool readsOk = _flatStreak >= Math.Max(3, ConfirmFlatReads);

            return (netNow == 0) && policyOk && readsOk;
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

        private bool CheckEmaVsWilderAtExec(int dir, int bar)
        {
            bool e8Has = TryGetSeries("EMA 8", bar, out var e8_ind);
            bool w8Has = TryGetSeries("Wilder 8", bar, out var w8_ind);

            // Si aún no están listos en el primer tick de N+1, usa N como proxy
            if (!e8Has && TryGetSeries("EMA 8", bar - 1, out var e8_prev)) { e8_ind = e8_prev; e8Has = true; }
            if (!w8Has && TryGetSeries("Wilder 8", bar - 1, out var w8_prev)) { w8_ind = w8_prev; w8Has = true; }

            // Si aún faltan series, calcula localmente con cierres (no "skip as OK")
            if (!e8Has) e8_ind = EmaFromCloses(8, bar);
            if (!w8Has) w8_ind = RmaFromCloses(8, bar);

            var diff = e8_ind - w8_ind;
            var oneTick = InternalTickSize;
            bool result;

            switch (EmaVsWilderMode)
            {
                case EmaWilderRule.Strict:     // BUY: EMA > W; SELL: EMA < W
                    result = dir > 0 ? (diff > 0m) : (diff < 0m);
                    break;
                case EmaWilderRule.Inclusive:  // BUY: EMA ≥ W; SELL: EMA ≤ W
                    result = dir > 0 ? (diff >= 0m) : (diff <= 0m);
                    break;
                case EmaWilderRule.Window:     // BUY: diff ≥ -tolPre ; SELL: diff ≤ +tolPre
                default:
                    var tolPre = Ticks(Math.Max(0, EmaVsWilderPreTolTicks));
                    if (EmaVsWilderAllowEquality)
                        result = dir > 0 ? (diff >= -tolPre) : (diff <= +tolPre);
                    else
                        result = dir > 0 ? (diff > -tolPre - 1e-9m * oneTick) // estrictamente mayor
                                        : (diff < +tolPre + 1e-9m * oneTick);
                    break;
            }

            // Log detallado con el nuevo sistema
            string modeStr = EmaVsWilderMode.ToString();
            string dirStr = dir > 0 ? "BUY" : "SELL";
            string diffStr = diff >= 0 ? $"+{diff:F5}" : $"{diff:F5}";
            string tolStr = EmaVsWilderMode == EmaWilderRule.Window ? $" tolPre={Ticks(Math.Max(0, EmaVsWilderPreTolTicks)):F5}" : "";
            string equalityStr = EmaVsWilderMode == EmaWilderRule.Window && !EmaVsWilderAllowEquality ? " (strict)" : "";

            DebugLog.W("468/STR", $"CONF#2 (EMA8 vs W8 @N+1) e8={e8_ind:F5}{(e8Has ? "[IND]" : "[LOCAL]")} w8={w8_ind:F5}{(w8Has ? "[IND]" : "[LOCAL]")} diff={diffStr} mode={modeStr}{tolStr}{equalityStr} {dirStr} -> {(result ? "OK" : "FAIL")}");

            return result;
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
            int netByFills = Math.Abs(NetByFills());
            DebugLog.W("468/STR", $"RECONCILE START: net={net} netByFills={netByFills}");

            if (net <= 0 && (DateTime.UtcNow < _postEntryFlatBlockUntil || !FlatConfirmedNow(0)))
            {
                DebugLog.W("468/STR", "RECONCILE SKIP: net=0 (not flat-confirmed) → protect children");
                return;
            }
            if (net < 0)
            {
                DebugLog.W("468/STR", "RECONCILE: clamping net from negative to 0");
                net = 0;
            }

            // === FREEZE MODE: mientras BE activo, mantener 1 solo TP (no recrear TP1/TP2/TP3) ===
            if (_breakevenApplied)
            {
                var beActiveTps = _liveOrders
                    .Where(o => o != null && (o.Comment?.StartsWith("468TP:") ?? false)
                        && o.State == OrderStates.Active && o.Status() != OrderStatus.Filled && o.Status() != OrderStatus.Canceled)
                    .ToList();
                var beActiveSls = _liveOrders
                    .Where(o => o != null && (o.Comment?.StartsWith("468SL:") ?? false)
                        && o.State == OrderStates.Active && o.Status() != OrderStatus.Filled && o.Status() != OrderStatus.Canceled)
                    .ToList();

                // 1) Elegir el TP "bueno": el más lejano en la dirección (o el último memorizado por BE)
                Order keepTp = null;
                if (beActiveTps.Count > 0)
                {
                    keepTp = (_entryDir > 0)
                        ? beActiveTps.OrderByDescending(o => SafeGetPrice(o)).First()
                        : beActiveTps.OrderBy(o => SafeGetPrice(o)).First();
                }

                // 2) Cancelar TPs extra (si quedaron más de uno por carreras)
                foreach (var tp in beActiveTps)
                    if (!object.ReferenceEquals(tp, keepTp))
                        try { CancelOrder(tp); } catch {}

                // 3) Asegurar que existe EXACTAMENTE 1 TP y su qty==net
                var coverSide = _entryDir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                string oco = null;
                if (beActiveSls.Count > 0) // intenta heredar el OCO del SL actual
                    try { oco = (string)beActiveSls[0].GetType().GetProperty("OCOGroup")?.GetValue(beActiveSls[0]); } catch {}
                if (string.IsNullOrEmpty(oco)) oco = Guid.NewGuid().ToString("N");

                if (keepTp == null && net > 0)
                {
                    // Si no hay TP vivo (por timing), crea uno en el TP memorizado por BE
                    var px = _beLastTpPrice;
                    if (px <= 0m)
                    {
                        // fallback prudente: un poco más allá del BE
                        px = _entryDir > 0 ? RoundToTick(GetCandle(CurrentBar).Close + Ticks(Math.Max(4, BreakevenOffsetTicks)))
                                           : RoundToTick(GetCandle(CurrentBar).Close - Ticks(Math.Max(4, BreakevenOffsetTicks)));
                    }
                    SubmitLimit(oco, coverSide, net, px);
                    DebugLog.W("468/STR", $"RECON(BE): recreated single TP qty={net} @ {px:F2}");
                }
                else if (keepTp != null)
                {
                    // Si hay TP, comprueba qty y ajusta si hace falta
                    int tpQty = 0; try { tpQty = (int)Math.Abs(keepTp.QuantityToFill); } catch {}
                    if (tpQty != net)
                    {
                        try { CancelOrder(keepTp); } catch {}
                        var px = SafeGetPrice(keepTp); if (px <= 0m) px = _beLastTpPrice;
                        SubmitLimit(oco, coverSide, net, px);
                        DebugLog.W("468/STR", $"RECON(BE): resized single TP from {tpQty} -> {net}");
                    }
                }

                // 4) Para el SL, conserva únicamente 1 SL = net (no tocar precio BE)
                int beSlQty = 0; foreach (var s in beActiveSls) { try { beSlQty += (int)Math.Abs(s.QuantityToFill); } catch {} }
                if (beSlQty != net)
                {
                    foreach (var s in beActiveSls) { try { CancelOrder(s); } catch {} }
                    // Nota: NO recalculamos precio; lo debe fijar BE. Si no hay SL, créalo en BE.
                    decimal bePx = CalculateBreakEvenPrice(_entryDir, _entryPrice, BreakevenOffsetTicks);
                    SubmitStop(oco, coverSide, net, bePx);
                    DebugLog.W("468/STR", $"RECON(BE): resized SL to qty={net} @ {bePx:F2}");
                }

                // Importante: durante BE no seguimos con la reconciliación "normal" (que replantea TP1/TP2/TP3)
                return;
            }

            // TPs activos de esta estrategia
            var tps = _liveOrders
                .Where(o => o != null && (o.Comment?.StartsWith("468TP:") ?? false)
                       && o.State == OrderStates.Active && o.Status() != OrderStatus.Filled && o.Status() != OrderStatus.Canceled)
                .ToList();
            DebugLog.W("468/STR", $"RECONCILE TPs: found {tps.Count} active TPs for net={net}");

            // Si hay más TPs que net -> cancelar sobrantes
            if (tps.Count > net)
            {
                DebugLog.W("468/STR", $"RECONCILE TPs: need to cancel {tps.Count - net} excess TPs");
                // dejar los más lejanos (más ambiciosos) para largos
                var toCancel = tps.OrderByDescending(o => o.Price).Skip(net).ToList();
                foreach (var o in toCancel)
                {
                    try
                    {
                        DebugLog.W("468/STR", $"RECONCILE CANCEL TP: price={o.Price:F2}");
                        CancelOrder(o);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.W("468/STR", $"RECONCILE CANCEL TP EX: {ex.Message}");
                    }
                }
            }
            else if (tps.Count < net)
            {
                DebugLog.W("468/STR", $"RECONCILE TPs: have {tps.Count} TPs but net={net} (TP deficit, normal after partial fills)");
            }

            // Stops activos
            var sls = _liveOrders
                .Where(o => o != null && (o.Comment?.StartsWith("468SL:") ?? false)
                       && o.State == OrderStates.Active && o.Status() != OrderStatus.Filled && o.Status() != OrderStatus.Canceled)
                .ToList();
            int slQty = 0;
            foreach (var s in sls) { try { slQty += (int)s.QuantityToFill; } catch { } }
            DebugLog.W("468/STR", $"RECONCILE SLs: found {sls.Count} SL orders with total qty={slQty} (need={net})");

            // Si la suma de SL != net -> simplificar a un único SL = net
            if (slQty != net)
            {
                DebugLog.W("468/STR", $"RECONCILE SL QTY MISMATCH: have={slQty} need={net} -> rebuilding SL");
                foreach (var s in sls)
                {
                    try
                    {
                        DebugLog.W("468/STR", $"RECONCILE CANCEL SL: price={s.TriggerPrice:F2} qty={(int)s.QuantityToFill}");
                        CancelOrder(s);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.W("468/STR", $"RECONCILE CANCEL SL EX: {ex.Message}");
                    }
                }
                if (Math.Abs(net) > 0 && _lastSignalBar >= 0 && _entryDir != 0)
                {
                    var (slPx, _) = BuildBracketPrices(_entryDir, _lastSignalBar, CurrentBar);
                    var coverSide = _entryDir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                    var oco = Guid.NewGuid().ToString();
                    DebugLog.W("468/STR", $"RECONCILE REBUILD SL: {coverSide} qty={net} @{slPx:F2} oco={oco.Substring(0, 6)}");
                    SubmitStop(oco, coverSide, net, slPx);
                }
                else
                {
                    DebugLog.W("468/STR", $"RECONCILE SL SKIP: net={net} lastSignalBar={_lastSignalBar} entryDir={_entryDir}");
                }
            }
            else
            {
                DebugLog.W("468/STR", "RECONCILE SL QTY OK: no SL quantity adjustment needed");
            }
            DebugLog.W("468/STR", $"RECONCILED: net={net} netByFills={NetByFills()} tpActive={tps.Count} slActive={sls.Count}");
        }

        // === Helper: ReleaseTradeLock (botón de pánico) ===
        private void ReleaseTradeLock(string reason)
        {
            _tradeActive = false;
            _bracketsPlaced = false;
            _bracketsAttachedAt = DateTime.MinValue;
            _antiFlatUntilBar = -1;
            _flatStreak = 0;
            _lastFlatRead = DateTime.MinValue;
            _orderFills.Clear();
            _cachedNetPosition = 0;
            _breakevenApplied = false;
            _entryPrice = 0m;
            _beLastTouchBar = -1;
            _beLastTouchAt = DateTime.MinValue;
            if (EnableCooldown && CooldownBars > 0)
                _cooldownUntilBar = CurrentBar + Math.Max(1, CooldownBars);
            DebugLog.W("468/ORD", $"Trade lock RELEASED ({reason})");
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