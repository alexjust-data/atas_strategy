using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ATAS.Strategies.Chart;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Indicators;
using MyAtas.Shared;

namespace MyAtas.Strategies
{
    public enum EmaWilderRule { Strict, Inclusive, Window }
    public enum AntiFlatMode { TimeOnly, BarsOnly, Hybrid }
    public enum SizingMode { Manual = 0, FixedRiskUSD = 1, PercentOfAccount = 2 }
    public enum BreakEvenMode { Off = 0, Manual = 1, OnTPFill = 2, OnTPTouch = 3 }

    [DisplayName("468 – Simple Strategy (GL close + 2 confluences) - FIXED")]
    public class FourSixEightSimpleStrategy : ChartStrategy
    {
        // ====================== USER PARAMETERS ======================
        [Category("General"), DisplayName("Quantity")]
        public int Quantity { get; set; } = 1;

        // ====================== RISK / POSITION SIZING (NEW) ======================
        [Category("Risk/Position Sizing"), DisplayName("Position Sizing Mode")]
        public SizingMode PositionSizingMode { get; set; } = SizingMode.Manual;

        [Category("Risk/Position Sizing"), DisplayName("Risk per trade (USD)")]
        public decimal RiskPerTradeUsd { get; set; } = 50m;

        [Category("Risk/Position Sizing"), DisplayName("Risk % of account")]
        public decimal RiskPercentOfAccount { get; set; } = 0.5m;

        [Category("Risk/Position Sizing"), DisplayName("Manual account equity override")]
        public decimal ManualAccountEquity { get; set; } = 0m;

        [Category("Risk/Position Sizing"), DisplayName("Tick value overrides (SYM=VAL;SYM=VAL or SYM,VAL;...)")]
        public string TickValueOverrides { get; set; } = "MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10";

        [Category("Risk/Position Sizing"), DisplayName("Skip trade if underfunded")]
        public bool SkipIfUnderfunded { get; set; } = true;

        [Category("Risk/Position Sizing"), DisplayName("Min qty if underfunded")]
        public int MinQtyIfUnderfunded { get; set; } = 1;

        [Category("Risk/Position Sizing"), DisplayName("Enable detailed risk logging")]
        public bool EnableRiskLogging { get; set; } = true;

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

        // ====================== BREAKEVEN ======================
        [Category("Breakeven"), DisplayName("Breakeven mode")]
        public BreakEvenMode BreakevenMode { get; set; } = BreakEvenMode.Off;

        [Category("Breakeven"), DisplayName("Breakeven offset (ticks)")]
        public int BreakevenOffsetTicks { get; set; } = 1;

        [Category("Breakeven"), DisplayName("Trigger breakeven manually")]
        public bool TriggerBreakevenManually { get; set; } = false;

        [Category("Breakeven"), DisplayName("Trigger on TP1 touch/fill")]
        public bool BreakevenOnTP1 { get; set; } = false;

        [Category("Breakeven"), DisplayName("Trigger on TP2 touch/fill")]
        public bool BreakevenOnTP2 { get; set; } = false;

        [Category("Breakeven"), DisplayName("Trigger on TP3 touch/fill")]
        public bool BreakevenOnTP3 { get; set; } = true;

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

        // ====================== INTERNAL STATE ======================
        private FourSixEightIndicator _ind;

        // ====================== RISK MANAGEMENT STATE ======================
        private decimal _cachedTickValue = 0m;
        private DateTime _lastTickValueUpdate = DateTime.MinValue;
        private decimal _cachedAccountEquity = 0m;
        private DateTime _lastEquityUpdate = DateTime.MinValue;
        private readonly Dictionary<string, decimal> _tickValueCache = new();
        private string _cachedSymbol = "";
        private bool _warnedTickFallback = false;
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

        // ==== Diagnostics (último cálculo auto-qty) ====
        private int _lastAutoQty = 0;
        private int _lastStopTicks = 0;
        private decimal _lastRiskPerContractUsd = 0m;
        private decimal _lastTickValueUsed = 0m;
        private decimal _lastRiskInputUsd = 0m;
        private bool _diagEchoLoggedInit = false;
        private bool _refreshDiagnostics = false;

        // ==== Breakeven state ====
        private bool _breakevenApplied = false;
        private decimal _entryPrice = 0m;
        private bool _lastTriggerManualState = false;

        [Category("Risk/Diagnostics"), DisplayName("Effective tick value (USD/tick)"), ReadOnly(true)]
        public decimal EffectiveTickValueUsd => GetTickValue();

        [Category("Risk/Diagnostics"), DisplayName("Effective tick size (points/tick)"), ReadOnly(true)]
        public decimal EffectiveTickSize => InternalTickSize;

        [Category("Risk/Diagnostics"), DisplayName("Effective account equity (USD)"), ReadOnly(true)]
        public decimal EffectiveAccountEquityUsd => GetAccountEquity();

        [Category("Risk/Diagnostics"), DisplayName("Last auto qty (contracts)"), ReadOnly(true)]
        public int LastAutoQty => _lastAutoQty;

        [Category("Risk/Diagnostics"), DisplayName("Last risk/contract (USD)"), ReadOnly(true)]
        public decimal LastRiskPerContractUsd => _lastRiskPerContractUsd;

        [Category("Risk/Diagnostics"), DisplayName("Last stop distance (ticks)"), ReadOnly(true)]
        public int LastStopDistanceTicks => _lastStopTicks;

        [Category("Risk/Diagnostics"), DisplayName("Last risk input (USD)"), ReadOnly(true)]
        public decimal LastRiskInputUsd => _lastRiskInputUsd;

        // "Botón" de refresco: al marcar True en la UI, loguea y se auto-resetea a False.
        [Category("Risk/Diagnostics"), DisplayName("Refresh diagnostics (log echo)")]
        [Description("Marca True para imprimir en el log los valores efectivos (tick value, equity, último auto-qty...). Se auto-resetea a False.")]
        public bool RefreshDiagnostics
        {
            get => _refreshDiagnostics;
            set
            {
                if (value)
                {
                    _refreshDiagnostics = false; // auto-reset
                    try { EchoDiagnostics("manual-refresh"); } catch { /* ignore */ }
                }
            }
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
                // Echo inicial de diagnóstico, una sola vez
                if (!_diagEchoLoggedInit)
                {
                    _diagEchoLoggedInit = true;
                    try { EchoDiagnostics("init"); } catch { /* ignore */ }
                }
            }

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
                int qty = CalculatePositionSize(dir, s.BarId);

                if (qty <= 0)
                {
                    _pending = null;
                    DebugLog.W("468/STR", "ABORT ENTRY: Underfunded (auto-qty=0)");
                    return;
                }

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

                // --- Confluencia #2: EMA8 vs Wilder8 en N+1, con reglas granulares
                if (RequireEmaVsWilder)
                {
                    bool emaOk = CheckEmaVsWilderAtExec(dir, bar);
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
                        DebugLog.W("468/STR", "RETRY NEXT TICK: keeping pending for re-check after zombie cancel");
                        // Conservar señal pendiente para reevaluación en próximo tick/bar
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

                    DebugLog.Critical("468/STR", $"ENTRY sent at N+1 bar={bar} (signal N={s.BarId}) dir={(dir>0?"BUY":"SELL")} qty={qty} - brackets will attach post-fill");
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
                        _flatStreak = 0;
                        _lastFlatRead = DateTime.MinValue;
                        DebugLog.W("468/ORD", "Trade lock RELEASED by watchdog (flat & no active orders)");
                    }
                }
                catch { /* best-effort */ }
            }

            // <<< PATCH 1: HEARTBEAT RELEASE (final, sin depender de AntiFlatMs/bars) >>>
            // Si por cualquier razón no ha actuado el watchdog o los eventos, libera aquí.
            if (_tradeActive && !HasAnyActiveOrders() && GetNetPosition() == 0)
            {
                _tradeActive = false;
                _bracketsPlaced = false;
                _orderFills.Clear();
                _cachedNetPosition = 0;
                _bracketsAttachedAt = DateTime.MinValue;
                _antiFlatUntilBar = -1;
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

            // ================ BREAKEVEN CHECK ================
            // Check breakeven triggers on each calculation (for manual/touch modes)
            CheckBreakevenTriggers();
            // =================================================
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

                // IMPORTANTE: No reconciliar en OnOrderChanged para evitar ráfagas/reentradas.
                // La reconciliación se hace en OnCalculate, 1x por barra, fuera de anti-flat.

                // Plano: confirma con nueva lógica híbrida
                int netNow = GetNetPosition();
                if (FlatConfirmedNow(netNow))
                {
                    if (HasAnyActiveOrders())
                    {
                        DebugLog.W("468/ORD", "FLAT CONFIRMED: cancelling remaining children...");
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
                        _lastFlatBar = CurrentBar;
                        if (EnableCooldown && CooldownBars > 0)
                        {
                            _cooldownUntilBar = CurrentBar + Math.Max(1, CooldownBars);
                            DebugLog.W("468/STR", $"COOLDOWN armed until bar={_cooldownUntilBar} (now={CurrentBar})");
                        }
                        // Clear fill tracking cache when truly flat
                        _orderFills.Clear();
                        _cachedNetPosition = 0;
                        DebugLog.W("468/ORD", "Trade lock RELEASED (flat confirmed & no active orders)");
                    }
                }
                else if (netNow == 0)
                {
                    DebugLog.W("468/ORD", $"ANTI-FLAT: net=0 detected but not confirmed yet (streak={_flatStreak}, policy={AntiFlatPolicy})");
                }

                // <<< PATCH 2: RELEASE INCONDICIONAL AL FINAL DE OnOrderChanged >>>
                // Cubre carreras donde FlatConfirmedNow() aún sea false pero ya no hay órdenes activas.
                if (_tradeActive && netNow == 0 && !HasAnyActiveOrders())
                {
                    _tradeActive = false;
                    _bracketsPlaced = false;
                    _bracketsAttachedAt = DateTime.MinValue;
                    _antiFlatUntilBar = -1;
                    _flatStreak = 0;
                    _lastFlatRead = DateTime.MinValue;
                    _orderFills.Clear();
                    _cachedNetPosition = 0;
                    _lastFlatBar = CurrentBar;
                    if (EnableCooldown && CooldownBars > 0)
                        _cooldownUntilBar = CurrentBar + Math.Max(1, CooldownBars);
                    DebugLog.W("468/ORD", "Trade lock RELEASED by OnOrderChanged (final)");
                }

                // ================ BREAKEVEN TRIGGER LOGIC ================

                // Track entry price for breakeven calculations
                if ((comment?.StartsWith("468ENTRY:") ?? false) && status == OrderStatus.Filled)
                {
                    _entryPrice = order.Price;
                    DebugLog.W("468/BREAKEVEN", $"Entry price tracked: {_entryPrice:F2}");
                    ResetBreakevenState(); // Reset state for new position
                }

                // Check for TP fills to trigger breakeven
                if (BreakevenMode == BreakEvenMode.OnTPFill && !_breakevenApplied)
                {
                    // Check if this was a TP order fill and if it's one we want to trigger on
                    if ((comment?.StartsWith("468TP") ?? false) && status == OrderStatus.Filled)
                    {
                        bool shouldTrigger = false;
                        string tpType = "";

                        // Check which specific TP filled and if it's enabled for breakeven
                        if (comment.Contains("TP1") && BreakevenOnTP1)
                        {
                            shouldTrigger = true;
                            tpType = "TP1";
                        }
                        else if (comment.Contains("TP2") && BreakevenOnTP2)
                        {
                            shouldTrigger = true;
                            tpType = "TP2";
                        }
                        else if (comment.Contains("TP3") && BreakevenOnTP3)
                        {
                            shouldTrigger = true;
                            tpType = "TP3";
                        }

                        if (shouldTrigger)
                        {
                            DebugLog.W("468/BREAKEVEN", $"{tpType} fill detected ({comment}), triggering breakeven");
                            TryMoveStopToBreakeven();
                        }
                        else
                        {
                            DebugLog.W("468/BREAKEVEN", $"TP fill detected ({comment}) but not configured for breakeven trigger");
                        }
                    }
                }

                // Reset breakeven state when position is closed
                if (netNow == 0 && _breakevenApplied)
                {
                    ResetBreakevenState();
                    DebugLog.W("468/BREAKEVEN", "Position closed, breakeven state reset");
                }

                // =======================================================

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
                    _lastFlatRead = DateTime.MinValue;
                    DebugLog.W("468/STR", $"BRACKETS ATTACHED (via OnPositionChanged, net={net})");
                }

                // Plano: usa nueva lógica híbrida
                if (FlatConfirmedNow(net))
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
        private void BuildAndSubmitBracket(int dir, int totalQty, int signalBar, int execBar)
        {
            if (totalQty <= 0) return;

            var (slPx, tpList) = BuildBracketPrices(dir, signalBar, execBar); // tpList respeta EnableTP1/2/3
            var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;

            int enabled = tpList.Count;
            if (enabled <= 0)
            {
                // Sin TPs activos → SL único por la qty completa
                SubmitStop(null, coverSide, totalQty, slPx);
                DebugLog.W("468/STR", $"BRACKETS: SL-only {totalQty} @ {slPx:F2} (no TPs enabled)");
                return;
            }

            // Reparto de cantidad entre TPs habilitados (p.ej., 3→[2,1] si enabled=2)
            var qtySplit = SplitQtyForTPs(totalQty, enabled); // ya existe en tu código

            for (int i = 0; i < enabled; i++)
            {
                int legQty = Math.Max(1, qtySplit[i]);
                var oco = Guid.NewGuid().ToString("N");
                SubmitStop(oco, coverSide, legQty, slPx);
                SubmitLimit(oco, coverSide, legQty, tpList[i]);
            }

            DebugLog.W("468/STR", $"BRACKETS: SL={slPx:F2} | TPs={string.Join(",", tpList.Select(x=>x.ToString("F2")))} | Split=[{string.Join(",", qtySplit)}] | Total={totalQty}");
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
                AutoCancel = EnableAutoCancel,
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
                AutoCancel = EnableAutoCancel,
                IsAttached = true,
                Comment = $"468SL:{DateTime.UtcNow:HHmmss}:{(oco!=null?oco.Substring(0,Math.Min(6,oco.Length)):"nooco")}"
            };
            OpenOrder(order);
            _liveOrders.Add(order);
            DebugLog.W("468/ORD", $"STOP submitted: {side} {qty} @{triggerPx:F2} OCO={(oco??"none")}");
        }

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

        // ===== Log de diagnóstico centralizado =====
        private void EchoDiagnostics(string reason)
        {
            string sym = GetSymbolCode();
            decimal tickSize = InternalTickSize;
            decimal tickVal = GetTickValue();
            decimal eqAuto = AutoDetectAccountEquity();
            decimal eqEff = eqAuto > 0m ? eqAuto : ManualAccountEquity;
            string eqSrc = (eqAuto > 0m) ? "auto" : "override";

            DebugLog.W("468/RISK",
                $"DIAG [{reason}] sym={sym} tickSize={tickSize} tickVal={tickVal:F2}USD/t " +
                $"equity({eqSrc})={eqEff:F2}USD lastAutoQty={_lastAutoQty} " +
                $"stopTicks={_lastStopTicks} risk/ct={_lastRiskPerContractUsd:F2} " +
                $"riskInput={_lastRiskInputUsd:F2}");
        }

        private string GetSymbolCode()
        {
            try
            {
                var security = GetType().GetProperty("Security", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);
                if (security == null) return "UNKNOWN";

                // Try multiple property names for symbol
                foreach (var propName in new[] { "Symbol", "Name", "Code", "Ticker" })
                {
                    var prop = security.GetType().GetProperty(propName);
                    if (prop != null)
                    {
                        var value = prop.GetValue(security)?.ToString();
                        if (!string.IsNullOrEmpty(value)) return value;
                    }
                }
                return "UNKNOWN";
            }
            catch
            {
                return "UNKNOWN";
            }
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

            // Strategy 3: Use our cached position with fill tracking
            int cachedPos = GetCachedPositionWithFills();
            if (cachedPos != 0)
            {
                DebugLog.W("468/POS", $"GetNetPosition via Cache+Fills: {cachedPos}");
                return cachedPos;
            }

            // Strategy 4: Sticky cache ONLY while anti-flat protection is active
            if (_cachedNetPosition != 0 && !FlatConfirmedNow(0))
            {
                DebugLog.W("468/POS", $"GetNetPosition via StickyCache (anti-flat active): {_cachedNetPosition}");
                return _cachedNetPosition;
            }

            // All strategies failed
            DebugLog.W("468/POS", "GetNetPosition: all strategies failed, returning 0");
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

                // Determine sign based on direction and order type
                var direction = order.Direction;
                int sign = 0;

                // FIX: For ALL orders (ENTRY, TP, SL), use consistent sign logic
                // Buy = +1, Sell = -1 (TP/SL do NOT reverse, they reduce position)
                sign = (direction.ToString().Contains("Buy")) ? 1 : -1;

                var netQty = qty * sign;

                if (action == "Filled")
                {
                    _orderFills[orderId] = netQty;
                    DebugLog.W("468/POS", $"Tracked fill: {orderId} = {netQty} (type: {c.Substring(0,8)})");
                }
                else if (action == "Canceled")
                {
                    _orderFills.Remove(orderId);
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

            // 3) Lecturas consistentes
            bool readsOk = _flatStreak >= Math.Max(1, ConfirmFlatReads);

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
            if (net <= 0 && !FlatConfirmedNow(0))
            {
                DebugLog.W("468/STR", "RECONCILE SKIP: net=0 (not flat-confirmed) → protect children");
                return;
            }
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
                if (Math.Abs(net) > 0 && _lastSignalBar >= 0 && _entryDir != 0)
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

        // ====================== RISK MANAGEMENT / POSITION SIZING ======================
        private int CalculatePositionSize(int dir, int signalBar)
        {
            try
            {
                if (PositionSizingMode == SizingMode.Manual)
                {
                    int qty = Math.Max(1, Quantity);

                    // Actualizar diagnósticos aún en modo manual para mostrar el valor efectivo
                    decimal slDistancePtsManual = CalculateStopLossDistance(dir, signalBar);
                    int stopTicksManual = (int)Math.Ceiling(slDistancePtsManual / InternalTickSize); // ticks (redondeo conservador)
                    decimal tickValueManual = GetTickValue();
                    decimal riskPerContractManual = stopTicksManual * tickValueManual; // FIXED: usar ticks, no puntos

                    _lastAutoQty = qty;
                    _lastStopTicks = stopTicksManual;
                    _lastRiskPerContractUsd = riskPerContractManual;
                    _lastTickValueUsed = tickValueManual;
                    _lastRiskInputUsd = riskPerContractManual * qty; // riesgo total con qty manual

                    if (EnableRiskLogging)
                    {
                        DebugLog.W("468/RISK", $"Position sizing: Manual mode");
                        DebugLog.W("468/RISK", $"AUTOQTY: {qty} contracts (manual override)");
                    }
                    return qty;
                }

                // Calculate SL distance for risk calculation - FIXED: convert points to ticks
                decimal slDistancePts = CalculateStopLossDistance(dir, signalBar);
                if (slDistancePts <= 0)
                {
                    DebugLog.W("468/RISK", "WARNING: SL distance <= 0, falling back to manual quantity");
                    return Math.Max(1, Quantity);
                }

                int stopTicks = (int)Math.Ceiling(slDistancePts / InternalTickSize); // ticks (redondeo conservador)
                decimal tickValue = GetTickValue();
                if (tickValue <= 0)
                {
                    DebugLog.W("468/RISK", "WARNING: tick value <= 0, falling back to manual quantity");
                    return Math.Max(1, Quantity);
                }

                decimal riskPerContract = stopTicks * tickValue; // FIXED: usar ticks, no puntos
                int calculatedQty;

                decimal riskUsd;
                if (PositionSizingMode == SizingMode.FixedRiskUSD)
                {
                    riskUsd = RiskPerTradeUsd;
                    calculatedQty = (int)Math.Floor(RiskPerTradeUsd / riskPerContract);
                    if (EnableRiskLogging)
                    {
                        DebugLog.W("468/RISK", $"FixedRiskUSD: target=${RiskPerTradeUsd:F2}, slDistPts={slDistancePts:F4} (~{stopTicks}t @{InternalTickSize:F4}/t), tickVal=${tickValue:F2}/t, riskPerContract=${riskPerContract:F2}");
                        DebugLog.W("468/RISK", $"AUTOQTY: {calculatedQty} contracts (${RiskPerTradeUsd:F2} risk / ${riskPerContract:F2} per contract)");
                    }
                }
                else if (PositionSizingMode == SizingMode.PercentOfAccount)
                {
                    decimal accountEquity = GetAccountEquity();
                    if (accountEquity <= 0)
                    {
                        DebugLog.W("468/RISK", "WARNING: account equity <= 0, falling back to manual quantity");
                        return Math.Max(1, Quantity);
                    }

                    decimal targetRisk = accountEquity * (RiskPercentOfAccount / 100m);
                    riskUsd = targetRisk;
                    calculatedQty = (int)Math.Floor(targetRisk / riskPerContract);
                    if (EnableRiskLogging)
                    {
                        DebugLog.W("468/RISK", $"PercentOfAccount: equity=${accountEquity:F2}, risk%={RiskPercentOfAccount:F2}%, targetRisk=${targetRisk:F2}");
                        DebugLog.W("468/RISK", $"slDistPts={slDistancePts:F4} (~{stopTicks}t @{InternalTickSize:F4}/t), tickVal=${tickValue:F2}/t, riskPerContract=${riskPerContract:F2}");
                        DebugLog.W("468/RISK", $"AUTOQTY: {calculatedQty} contracts (${targetRisk:F2} risk / ${riskPerContract:F2} per contract)");
                    }
                }
                else
                {
                    DebugLog.W("468/RISK", "Unknown sizing mode, falling back to manual");
                    return Math.Max(1, Quantity);
                }

                if (calculatedQty <= 0)
                {
                    if (SkipIfUnderfunded)
                    {
                        // guardar diagnóstico para UI
                        _lastAutoQty = 0;
                        _lastStopTicks = stopTicks; // FIXED: usar ticks calculados correctamente
                        _lastRiskPerContractUsd = riskPerContract;
                        _lastTickValueUsed = tickValue;
                        _lastRiskInputUsd = riskUsd;

                        if (EnableRiskLogging)
                            DebugLog.W("468/RISK", $"ABORT ENTRY: Underfunded (risk/ct=${riskPerContract:F2} > target=${riskUsd:F2})");

                        return 0; // ← señal de aborto limpio
                    }
                    calculatedQty = Math.Max(1, MinQtyIfUnderfunded);
                }

                // guardar diagnóstico para UI
                _lastAutoQty = calculatedQty;
                _lastStopTicks = stopTicks; // FIXED: usar ticks calculados correctamente
                _lastRiskPerContractUsd = riskPerContract;
                _lastTickValueUsed = tickValue;
                _lastRiskInputUsd = riskUsd;

                if (EnableRiskLogging)
                    DebugLog.W("468/RISK", $"Final position size: {calculatedQty} contracts (mode: {PositionSizingMode})");

                return calculatedQty;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"Position sizing calculation failed: {ex.Message}, falling back to manual quantity");
                return Math.Max(1, Quantity);
            }
        }

        private decimal CalculateStopLossDistance(int dir, int signalBar)
        {
            try
            {
                // Use the same logic as BuildBracketPrices but only calculate SL distance
                var refCandle = UseSignalCandleSL ? GetCandle(signalBar) : GetCandle(signalBar + 1);

                decimal sl = dir > 0 ? refCandle.Low - Ticks(Math.Max(0, StopOffsetTicks))
                                     : refCandle.High + Ticks(Math.Max(0, StopOffsetTicks));

                // Entry reference (same logic as BuildBracketPrices)
                decimal entryPx;
                try
                {
                    int execBar = signalBar + 1;
                    if (execBar <= CurrentBar)
                        entryPx = GetCandle(execBar).Open;
                    else
                        entryPx = GetCandle(signalBar).Close; // fallback
                }
                catch
                {
                    entryPx = GetCandle(signalBar).Close;
                }

                if (entryPx <= 0) entryPx = GetCandle(signalBar).Close;

                decimal distance = Math.Abs(entryPx - sl);
                return distance;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"SL distance calculation failed: {ex.Message}");
                return 0m;
            }
        }

        // ================== BREAKEVEN HELPERS ==================

        private void CheckBreakevenTriggers()
        {
            if (BreakevenMode == BreakEvenMode.Off) return;
            if (_breakevenApplied) return; // Already applied
            if (GetNetPosition() == 0) return; // No position

            bool shouldTrigger = false;

            switch (BreakevenMode)
            {
                case BreakEvenMode.Manual:
                    shouldTrigger = TriggerBreakevenManually && !_lastTriggerManualState;
                    break;

                case BreakEvenMode.OnTPFill:
                    // This will be triggered from OnOrderChanged when TP gets filled
                    break;

                case BreakEvenMode.OnTPTouch:
                    shouldTrigger = HasAnyTPBeenTouched();
                    break;
            }

            if (shouldTrigger)
            {
                TryMoveStopToBreakeven();
            }

            // Reset manual trigger
            if (TriggerBreakevenManually && !_lastTriggerManualState)
            {
                _lastTriggerManualState = true;
                // Auto-reset the boolean after triggering
                Task.Run(async () =>
                {
                    await Task.Delay(100);
                    TriggerBreakevenManually = false;
                    _lastTriggerManualState = false;
                });
            }
        }

        private bool HasAnyTPBeenTouched()
        {
            if (GetNetPosition() == 0 || _entryPrice <= 0) return false;

            try
            {
                decimal currentPrice = GetCandle(CurrentBar).Close;
                bool isLong = GetNetPosition() > 0;

                // Calculate TP levels based on R multiples
                decimal stopDistance = Math.Abs(_entryPrice - GetCurrentStopPrice());

                // Check only the TPs that are enabled AND selected for breakeven
                if (EnableTP1 && BreakevenOnTP1)
                {
                    decimal tp1Level = isLong ? _entryPrice + (stopDistance * TP1_R) : _entryPrice - (stopDistance * TP1_R);
                    if ((isLong && currentPrice >= tp1Level) || (!isLong && currentPrice <= tp1Level))
                    {
                        DebugLog.W("468/BREAKEVEN", $"TP1 touched at {currentPrice:F2} (target: {tp1Level:F2})");
                        return true;
                    }
                }

                if (EnableTP2 && BreakevenOnTP2)
                {
                    decimal tp2Level = isLong ? _entryPrice + (stopDistance * TP2_R) : _entryPrice - (stopDistance * TP2_R);
                    if ((isLong && currentPrice >= tp2Level) || (!isLong && currentPrice <= tp2Level))
                    {
                        DebugLog.W("468/BREAKEVEN", $"TP2 touched at {currentPrice:F2} (target: {tp2Level:F2})");
                        return true;
                    }
                }

                if (EnableTP3 && BreakevenOnTP3)
                {
                    decimal tp3Level = isLong ? _entryPrice + (stopDistance * TP3_R) : _entryPrice - (stopDistance * TP3_R);
                    if ((isLong && currentPrice >= tp3Level) || (!isLong && currentPrice <= tp3Level))
                    {
                        DebugLog.W("468/BREAKEVEN", $"TP3 touched at {currentPrice:F2} (target: {tp3Level:F2})");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/BREAKEVEN", $"Error checking TP touch: {ex.Message}");
            }

            return false;
        }

        private decimal GetCurrentStopPrice()
        {
            // Find the current stop loss order
            foreach (var order in Orders.Where(o => o.State == OrderStates.Active && o.Type == OrderTypes.Stop))
            {
                return order.Price;
            }
            return 0m;
        }

        private void TryMoveStopToBreakeven()
        {
            if (GetNetPosition() == 0 || _entryPrice <= 0 || _breakevenApplied) return;

            try
            {
                bool isLong = GetNetPosition() > 0;
                decimal breakevenPrice = _entryPrice + (isLong ? Ticks(BreakevenOffsetTicks) : -Ticks(BreakevenOffsetTicks));

                // Find and modify current stop orders
                var stopOrders = Orders.Where(o => o.State == OrderStates.Active && o.Type == OrderTypes.Stop).ToList();

                foreach (var stopOrder in stopOrders)
                {
                    // Check if the new price is better than current (closer to entry)
                    bool shouldMove = isLong ? breakevenPrice > stopOrder.Price : breakevenPrice < stopOrder.Price;

                    if (shouldMove)
                    {
                        try
                        {
                            // Cancel old stop and create new one at breakeven price
                            CancelOrder(stopOrder);

                            var newStop = new Order
                            {
                                Type = OrderTypes.Stop,
                                Direction = isLong ? OrderDirections.Sell : OrderDirections.Buy,
                                Price = breakevenPrice,
                                QuantityToFill = stopOrder.QuantityToFill,
                                IsAttached = true,
                                Comment = $"468SL:BE{DateTime.UtcNow:HHmmss}"
                            };
                            OpenOrder(newStop);
                            _liveOrders.Add(newStop);

                            DebugLog.W("468/BREAKEVEN", $"Moving SL to breakeven: {stopOrder.Price:F2} -> {breakevenPrice:F2} (offset: {BreakevenOffsetTicks} ticks)");
                        }
                        catch (Exception modEx)
                        {
                            DebugLog.W("468/BREAKEVEN", $"Error modifying stop order: {modEx.Message}");
                        }
                    }
                }

                _breakevenApplied = true;
                DebugLog.W("468/BREAKEVEN", $"Breakeven applied at {breakevenPrice:F2} with {BreakevenOffsetTicks} ticks offset");
            }
            catch (Exception ex)
            {
                DebugLog.W("468/BREAKEVEN", $"Error moving stop to breakeven: {ex.Message}");
            }
        }

        private void ResetBreakevenState()
        {
            _breakevenApplied = false;
            _entryPrice = 0m;
            _lastTriggerManualState = false;
        }

        // ========================================================

        private decimal GetTickValue()
        {
            try
            {
                string symbol = GetCurrentSymbol();
                if (string.IsNullOrEmpty(symbol))
                {
                    DebugLog.W("468/RISK", "Could not determine current symbol");
                    return 5.0m;
                }

                // Check cache first (valid for 5 minutes and same symbol)
                if (_cachedTickValue > 0 && _cachedSymbol == symbol && (DateTime.UtcNow - _lastTickValueUpdate).TotalMinutes < 5)
                {
                    return _cachedTickValue;
                }

                // a) Read override if it exists
                decimal overrideVal = GetTickValueFromOverrides(symbol); // 0 if not found

                // b) Try auto-detection via reflection
                decimal autoVal = AutoDetectTickValue();

                // c) Check for mismatch between override and auto-detected
                if (overrideVal > 0m && autoVal > 0m && Math.Abs(overrideVal - autoVal) > 0.01m)
                {
                    DebugLog.W("468/RISK", $"TICK-VALUE MISMATCH for {symbol}: override={overrideVal:F2} vs auto={autoVal:F2} (using override)");
                }

                // d) Precedence: override > auto > fallback
                decimal finalValue;
                string source;

                if (overrideVal > 0m)
                {
                    finalValue = overrideVal;
                    source = "override";
                    DebugLog.W("468/RISK", $"TICK-VALUE: override for {symbol} -> {overrideVal:F2} USD/tick");
                }
                else if (autoVal > 0m)
                {
                    finalValue = autoVal;
                    source = "auto-detected";
                    DebugLog.W("468/RISK", $"TICK-VALUE: auto-detected for {symbol} -> {autoVal:F2} USD/tick");
                }
                else
                {
                    finalValue = GetFallbackTickValue(symbol);
                    source = "fallback";

                    // Critical warning for fallback (only once per symbol)
                    if (!_warnedTickFallback || _cachedSymbol != symbol)
                    {
                        DebugLog.Critical("468/RISK", $"TICK-VALUE: using FALLBACK {finalValue:F2} USD/tick for {symbol} (configure overrides or enable auto-detect)");
                        _warnedTickFallback = true;
                    }
                }

                // Update cache
                _cachedTickValue = finalValue;
                _cachedSymbol = symbol;
                _lastTickValueUpdate = DateTime.UtcNow;

                return finalValue;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"GetTickValue failed: {ex.Message}");
                return 5.0m; // conservative fallback
            }
        }

        private string GetCurrentSymbol()
        {
            try
            {
                var security = GetType().GetProperty("Security", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);
                if (security == null) return "";

                // Try multiple property names for symbol
                foreach (var propName in new[] { "Symbol", "Name", "Code", "Ticker" })
                {
                    var prop = security.GetType().GetProperty(propName);
                    if (prop != null)
                    {
                        var value = prop.GetValue(security)?.ToString();
                        if (!string.IsNullOrEmpty(value)) return value;
                    }
                }
                return "";
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"GetCurrentSymbol failed: {ex.Message}");
                return "";
            }
        }

        private decimal GetTickValueFromOverrides(string symbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TickValueOverrides) || string.IsNullOrWhiteSpace(symbol))
                    return 0m;

                if (_tickValueCache.TryGetValue(symbol, out var cached))
                    return cached;

                var entries = TickValueOverrides.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in entries)
                {
                    // Acepta "SYM=VAL" o "SYM,VAL"
                    var parts = entry.Split(new[] { '=', ',' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var valStr = parts[1].Trim();

                    if (!key.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (decimal.TryParse(valStr, System.Globalization.NumberStyles.Any,
                                         System.Globalization.CultureInfo.InvariantCulture, out var tickValue)
                        && tickValue > 0m)
                    {
                        _tickValueCache[symbol] = tickValue;
                        return tickValue;
                    }
                }
                return 0m;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"GetTickValueFromOverrides failed: {ex.Message}");
                return 0m;
            }
        }

        private decimal AutoDetectTickValue()
        {
            try
            {
                var security = GetType().GetProperty("Security", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);
                if (security == null) return 0m;

                // Try common property names for tick value
                foreach (var propName in new[] { "MinStepPrice", "TickValue", "PointValue", "ContractSize", "MultiplierValue", "Multiplier" })
                {
                    var prop = security.GetType().GetProperty(propName);
                    if (prop != null)
                    {
                        var value = prop.GetValue(security);
                        if (value != null && decimal.TryParse(value.ToString(), out decimal tickValue) && tickValue > 0)
                        {
                            if (EnableRiskLogging)
                                DebugLog.W("468/RISK", $"Auto-detected tick value via {propName}: {tickValue}");
                            return tickValue;
                        }
                    }
                }

                // Try nested instrument info
                var instProp = GetType().GetProperty("InstrumentInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var instVal = instProp?.GetValue(this);
                if (instVal != null)
                {
                    foreach (var propName in new[] { "MinStepPrice", "TickValue", "PointValue", "ContractSize" })
                    {
                        var prop = instVal.GetType().GetProperty(propName);
                        if (prop != null)
                        {
                            var value = prop.GetValue(instVal);
                            if (value != null && decimal.TryParse(value.ToString(), out decimal tickValue) && tickValue > 0)
                            {
                                if (EnableRiskLogging)
                                    DebugLog.W("468/RISK", $"Auto-detected tick value via InstrumentInfo.{propName}: {tickValue}");
                                return tickValue;
                            }
                        }
                    }
                }

                return 0m;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"AutoDetectTickValue failed: {ex.Message}");
                return 0m;
            }
        }

        private decimal GetFallbackTickValue(string symbol)
        {
            // Common US futures tick values
            var fallbacks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                // E-mini futures
                { "ES", 12.50m },    { "MES", 1.25m },
                { "NQ", 5.0m },      { "MNQ", 0.50m },
                { "YM", 5.0m },      { "MYM", 0.50m },
                { "RTY", 5.0m },     { "M2K", 0.50m },

                // Energy
                { "CL", 10.0m },     { "QM", 1.0m },
                { "NG", 10.0m },     { "QG", 2.50m },

                // Metals
                { "GC", 10.0m },     { "MGC", 1.0m },
                { "SI", 25.0m },     { "SIL", 2.50m },

                // Bonds
                { "ZB", 31.25m },    { "ZN", 15.625m },
                { "ZF", 7.8125m },   { "ZT", 3.90625m },

                // FX
                { "6E", 6.25m },     { "M6E", 0.625m },
                { "6B", 6.25m },     { "M6B", 0.625m },
                { "6J", 6.25m },     { "6A", 5.0m }
            };

            if (fallbacks.TryGetValue(symbol, out decimal value))
                return value;

            return 5.0m; // Conservative default
        }

        private decimal GetAccountEquity()
        {
            try
            {
                // Check cache first (valid for 1 minute)
                if (_cachedAccountEquity > 0 && (DateTime.UtcNow - _lastEquityUpdate).TotalMinutes < 1)
                {
                    return _cachedAccountEquity;
                }

                // a) Try auto-detection via Portfolio first
                decimal eqAuto = AutoDetectAccountEquity();
                decimal eqOverride = Math.Max(0m, ManualAccountEquity);

                // b) Check for mismatch between auto and override (>2% difference)
                if (eqAuto > 0m && eqOverride > 0m)
                {
                    decimal rel = Math.Abs(eqAuto - eqOverride) / Math.Max(eqAuto, 1m);
                    if (rel > 0.02m) // >2% difference
                    {
                        DebugLog.W("468/RISK", $"ACCOUNT EQUITY MISMATCH: auto={eqAuto:F2} vs override={eqOverride:F2} (using auto)");
                    }
                }

                // c) Precedence: auto > override > fail
                decimal finalEquity;
                if (eqAuto > 0m)
                {
                    finalEquity = eqAuto;
                    if (EnableRiskLogging)
                        DebugLog.W("468/RISK", $"ACCOUNT EQUITY: auto-detected = ${eqAuto:F2} USD");
                }
                else if (eqOverride > 0m)
                {
                    finalEquity = eqOverride;
                    DebugLog.W("468/RISK", $"ACCOUNT EQUITY: using manual override = ${eqOverride:F2} USD (auto-detect failed)");
                }
                else
                {
                    DebugLog.W("468/RISK", "Could not detect account equity - set ManualAccountEquity parameter");
                    return 0m;
                }

                // Update cache
                _cachedAccountEquity = finalEquity;
                _lastEquityUpdate = DateTime.UtcNow;
                return finalEquity;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"GetAccountEquity failed: {ex.Message}");
                return 0m;
            }
        }

        private decimal AutoDetectAccountEquity()
        {
            try
            {
                var portfolio = GetType().GetProperty("Portfolio", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);
                if (portfolio == null) return 0m;

                // Try common property names for equity/balance
                foreach (var propName in new[] { "Equity", "Balance", "TotalEquity", "NetLiquidation", "TotalBalance", "AccountValue" })
                {
                    var prop = portfolio.GetType().GetProperty(propName);
                    if (prop != null)
                    {
                        var value = prop.GetValue(portfolio);
                        if (value != null && decimal.TryParse(value.ToString(), out decimal equity) && equity > 0)
                        {
                            if (EnableRiskLogging)
                                DebugLog.W("468/RISK", $"Auto-detected equity via Portfolio.{propName}: ${equity:F2}");
                            return equity;
                        }
                    }
                }

                // Try getting all positions and summing their values
                var getPositions = portfolio.GetType().GetMethod("GetPositions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getPositions != null)
                {
                    var positions = getPositions.Invoke(portfolio, null) as IEnumerable;
                    if (positions != null)
                    {
                        decimal totalValue = 0m;
                        foreach (var pos in positions)
                        {
                            // Try to get market value or unrealized PnL
                            foreach (var propName in new[] { "MarketValue", "UnrealizedPnL", "Equity", "Value" })
                            {
                                var prop = pos.GetType().GetProperty(propName);
                                if (prop != null)
                                {
                                    var value = prop.GetValue(pos);
                                    if (value != null && decimal.TryParse(value.ToString(), out decimal posValue))
                                    {
                                        totalValue += posValue;
                                        break;
                                    }
                                }
                            }
                        }
                        if (totalValue > 0)
                        {
                            if (EnableRiskLogging)
                                DebugLog.W("468/RISK", $"Auto-detected equity via positions sum: ${totalValue:F2}");
                            return totalValue;
                        }
                    }
                }

                return 0m;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"AutoDetectAccountEquity failed: {ex.Message}");
                return 0m;
            }
        }
    }
}