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

        // ====================== CAPA 0 — FLAGS RISK MANAGEMENT ======================
        [System.ComponentModel.DisplayName("Enable Risk Management")]
        [System.ComponentModel.Category("Risk Management")]
        [System.ComponentModel.Description("Activa el subsistema de Risk Management. OFF por defecto.")]
        public bool EnableRiskManagement { get; set; } = false;

        [System.ComponentModel.DisplayName("Risk Dry-Run (no effect)")]
        [System.ComponentModel.Category("Risk Management")]
        [System.ComponentModel.Description("Cuando RM está activo, ejecuta cálculos y logs sin afectar órdenes. ON por defecto.")]
        public bool RiskDryRun { get; set; } = true;

        // Internos efectivos (se usarán para gating/log en capas siguientes)
        private bool RMEnabled => EnableRiskManagement;
        private bool RMDryRunEffective => !EnableRiskManagement || RiskDryRun;
        // ============================================================================

        // ====================== Helpers de logging con gating =======================
        //  - RiskLog/CalcLog solo escriben si EnableRiskManagement==true
        //  - Para INIT de Capa 0 usamos DebugLog.W directo (arriba) para la evidencia
        private void RiskLog(string tag, string message)
        {
            // Solo gateamos tags 468/RISK aquí
            if (tag == "468/RISK")
            {
                if (!RMEnabled) return;
            }
            DebugLog.W(tag, message);
        }

        private void CalcLog(string tag, string message)
        {
            // Solo gateamos tags 468/CALC aquí
            if (tag == "468/CALC")
            {
                if (!RMEnabled) return;
            }
            DebugLog.W(tag, message);
        }
        // ============================================================================

        // ====================== RISK MANAGEMENT PARAMETERS ======================

        // --- Position Sizing ---
        [Category("Risk Management/Position Sizing"), DisplayName("Position Sizing Mode")]
        public PositionSizingMode PositionSizingMode { get; set; } = PositionSizingMode.Manual;

        [Category("Risk Management/Position Sizing"), DisplayName("Risk per trade (USD)")]
        public decimal RiskPerTradeUsd { get; set; } = 100.0m;

        [Category("Risk Management/Position Sizing"), DisplayName("Risk % of account")]
        public decimal RiskPercentOfAccount { get; set; } = 0.5m;

        [Category("Risk Management/Position Sizing"), DisplayName("Manual account equity override")]
        public decimal ManualAccountEquityOverride { get; set; } = 0.0m;

        [Category("Risk Management/Position Sizing"), DisplayName("Tick value overrides (SYM=V)")]
        public string TickValueOverrides { get; set; } = "MNQ=0.5;NQ=5;MES=1.25;ES=12.5";

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

        [Category("Risk Management/Diagnostics"), DisplayName("Last auto qty")]
        [ReadOnly(true)]
        public int LastAutoQty { get; private set; } = 0;

        [Category("Risk Management/Diagnostics"), DisplayName("Last risk per contract")]
        [ReadOnly(true)]
        public decimal LastRiskPerContract { get; private set; } = 0.0m;

        [Category("Risk Management/Diagnostics"), DisplayName("Last stop distance (ticks)")]
        [ReadOnly(true)]
        public decimal LastStopDistance { get; private set; } = 0.0m;

        [Category("Risk Management/Diagnostics"), DisplayName("Last risk input")]
        [ReadOnly(true)]
        public decimal LastRiskInput { get; private set; } = 0.0m;

        // --- Underfunded Protection ---
        [Category("Risk Management/Position Sizing"), DisplayName("Skip trade if underfunded")]
        public bool SkipIfUnderfunded { get; set; } = true;

        [Category("Risk Management/Position Sizing"), DisplayName("Min qty if underfunded")]
        public int MinQtyIfUnderfunded { get; set; } = 1;

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

                // Initialize risk management diagnostics
                UpdateDiagnostics();

                // ======= CAPA 0: Log único de INIT para evidenciar estado de flags =======
                // Requisito: que solo aparezca INIT de RISK si no tocamos nada más.
                DebugLog.W("468/RISK",
                    $"INIT flags EnableRiskManagement={EnableRiskManagement} RiskDryRun={RiskDryRun} " +
                    $"effectiveDryRun={RMDryRunEffective}");
                // =========================================================================

                // ========== Capa 1: INIT de símbolo (no cambia comportamiento, solo evidencia) ==========
                var sym = GetEffectiveSecurityCode();
                var qc  = Security?.QuoteCurrency ?? "USD";
                RiskLog("468/RISK", $"INIT SYMBOL source={_cachedSymbolSource ?? "unknown"} value={sym} qc={qc}");
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

            // --- Risk Management Pulse Logging & Calculation Engine Exercise ---
            try
            {
                // ===== Capa 2: Dry-run del motor de cálculo con throttle (no altera órdenes) =====
                // Requisitos de ejecución:
                //  - RM habilitado (OFF = baseline sin cambios)
                //  - Primer tick de la vela
                //  - Ha pasado el cooldown O han cambiado inputs clave O es cada N velas
                if (RMEnabled)
                {
                    bool isFirstTick = IsFirstTickOf(bar);
                    if (isFirstTick && bar > 0)
                    {
                        // Refrescar diagnósticos antes de calcular
                        UpdateDiagnostics();

                        string inputsHash = ComputeCalcInputsHash();
                        bool inputsChanged = !string.Equals(inputsHash, _lastCalcInputsHash, StringComparison.Ordinal);
                        bool barInterval   = (bar % _calcEveryNBars) == 0;
                        bool cooldownOk    = (DateTime.UtcNow - _lastCalcUtc) >= _calcCooldown;

                        if (cooldownOk || inputsChanged || barInterval)
                        {
                            try
                            {
                                var qty = CalculateQuantity(); // dry-run: no toca Quantity real

                                var sym = GetEffectiveSecurityCode();
                                var qc  = Security?.QuoteCurrency ?? "USD";
                                var mode = PositionSizingMode;

                                // Snapshot siempre que haya cálculo (para auditoría)
                                CalcLog("468/CALC",
                                    $"SNAPSHOT [{sym}] mode={mode} qty={qty} slTicks={LastStopDistance:F1} " +
                                    $"rpc={LastRiskPerContract:F2}{qc} equity={EffectiveAccountEquity:F2}USD " +
                                    $"tickValue={EffectiveTickValue:F2}{qc}/t");

                                // Pulso mínimo cuando el log detallado está OFF (ritmo visible sin inundar)
                                if (!EnableDetailedRiskLogging)
                                {
                                    CalcLog("468/CALC", $"PULSE sym={sym} bar={bar} mode={mode} qty={qty}");
                                }

                                _lastCalcInputsHash = inputsHash;
                                _lastCalcUtc = DateTime.UtcNow;
                                _lastCalcBar = bar;
                            }
                            catch (Exception ex)
                            {
                                CalcLog("468/CALC", $"Engine dry-run error at bar={bar}: {ex.Message}");
                            }
                        }
                    }
                }
                // ===============================================================================

                // Periodic diagnostic refresh for non-RM mode (every 20 bars when RM is OFF)
                if (!RMEnabled && IsFirstTickOf(bar))
                {
                    if (_lastDiagnosticRefreshBar == -1 || (bar - _lastDiagnosticRefreshBar) >= 20)
                    {
                        UpdateDiagnostics();
                        var securityCode = Security?.Code ?? "UNKNOWN";
                        var tickValue = GetEffectiveTickValue();
                        var equity = GetEffectiveAccountEquity();
                        DebugLog.W("468/RISK", $"REFRESH sym={securityCode} bar={bar} tickValue={tickValue:F2} equity={equity:F2}");
                        _lastDiagnosticRefreshBar = bar;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"Pulse logging error: {ex.Message}");
            }

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

            // --- NOTA: PASO 3 calculation now handled by Capa 2 throttling system above ---

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

        // ====================== RISK MANAGEMENT AUTO-DETECTION ======================

        /// <summary>
        /// Gets effective tick value for position sizing calculations
        /// Priority: ATAS auto-detection → CSV overrides → fallback
        /// </summary>
        private decimal GetEffectiveTickValue()
        {
            try
            {
                // Nota (Capa 2): este valor solo se usa para diagnóstico y cálculo dry-run,
                // no modifica la Quantity real ni el envío de órdenes.

                // Priority 1: Security.TickCost (ATAS)
                var qc = Security?.QuoteCurrency ?? "USD";
                var secTickCost = (Security != null ? Security.TickCost : 0m);
                if (secTickCost > 0)
                {
                    if (EnableDetailedRiskLogging)
                        RiskLog("468/RISK", $"TICK-VALUE auto-detected: {secTickCost:F2}{qc}/tick via Security.TickCost");
                    return secTickCost;
                }

                // Priority 2: CSV overrides
                var code = GetEffectiveSecurityCode();
                var overrides = ParseTickValueOverrides();
                if (!string.IsNullOrWhiteSpace(code) && overrides.TryGetValue(code, out var ov))
                {
                    if (EnableDetailedRiskLogging)
                        RiskLog("468/RISK", $"TICK-VALUE override: {ov:F2}{qc}/tick for {code}");
                    return ov;
                }

                // Priority 3: Fallback
                if (EnableDetailedRiskLogging)
                    RiskLog("468/RISK", $"TICK-VALUE fallback: 0.5{qc}/tick (no detection/override for {code})");
                return 0.5m;
            }
            catch (Exception ex)
            {
                RiskLog("468/RISK", $"TICK-VALUE error: {ex.Message} -> fallback 0.5");
                return 0.5m;
            }
        }

        /// <summary>
        /// Gets effective account equity for percentage-based position sizing
        /// Priority: Manual override → Portfolio BalanceAvailable → Portfolio Balance → fallback
        /// </summary>
        private decimal GetEffectiveAccountEquity()
        {
            try
            {
                // Priority 1: Manual override if specified
                if (ManualAccountEquityOverride > 0)
                {
                    if (EnableDetailedRiskLogging)
                        DebugLog.W("468/RISK", $"ACCOUNT EQUITY manual override: {ManualAccountEquityOverride:F2} USD");
                    return ManualAccountEquityOverride;
                }

                // Priority 2: Auto-detection via Portfolio.BalanceAvailable (version-agnostic)
                var ba = Portfolio?.BalanceAvailable ?? 0m; // Works for both decimal? and decimal
                if (ba > 0)
                {
                    var detected = ba;
                    if (EnableDetailedRiskLogging)
                        DebugLog.W("468/RISK", $"ACCOUNT EQUITY auto-detected: {detected:F2} USD via Portfolio.BalanceAvailable");
                    return detected;
                }

                // Priority 3: Fallback to Portfolio.Balance (direct decimal)
                if (Portfolio?.Balance > 0)
                {
                    var detected = Portfolio.Balance;
                    if (EnableDetailedRiskLogging)
                        DebugLog.W("468/RISK", $"ACCOUNT EQUITY auto-detected: {detected:F2} USD via Portfolio.Balance");
                    return detected;
                }

                // Priority 4: Fallback value with warning
                if (EnableDetailedRiskLogging)
                    DebugLog.W("468/RISK", $"ACCOUNT EQUITY fallback: 10000.0 USD (no detection/override)");
                return 10000.0m;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"ACCOUNT EQUITY error: {ex.Message} → fallback 10000.0");
                return 10000.0m;
            }
        }

        /// <summary>
        /// Parses tick value overrides from CSV string format
        /// Format: "MNQ=0.5;NQ=5;MES=1.25;ES=12.5"
        /// </summary>
        private Dictionary<string, decimal> ParseTickValueOverrides()
        {
            var result = new Dictionary<string, decimal>();

            try
            {
                if (string.IsNullOrWhiteSpace(TickValueOverrides))
                    return result;

                var pairs = TickValueOverrides.Split(';');
                foreach (var pair in pairs)
                {
                    if (string.IsNullOrWhiteSpace(pair)) continue;

                    var parts = pair.Split('=');
                    if (parts.Length == 2)
                    {
                        var symbol = parts[0].Trim().ToUpper();
                        if (decimal.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
                        {
                            result[symbol] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"ParseTickValueOverrides error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Updates diagnostic properties with current detected values
        /// Called periodically to refresh UI diagnostics
        /// </summary>
        private void UpdateDiagnostics()
        {
            try
            {
                EffectiveTickValue = GetEffectiveTickValue();
                EffectiveAccountEquity = GetEffectiveAccountEquity();

                // TickSize priority: InstrumentInfo.TickSize -> Security.TickSize -> fallback
                decimal infoTickSize = 0m;
                try
                {
                    var ii = GetType().GetProperty("InstrumentInfo",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic)?.GetValue(this);
                    if (ii != null)
                    {
                        var pTs = ii.GetType().GetProperty("TickSize");
                        infoTickSize = (pTs?.GetValue(ii) as decimal?) ?? 0m;
                    }
                }
                catch { /* ignore */ }

                var secTickSize = (Security != null ? Security.TickSize : 0m);
                EffectiveTickSize = infoTickSize > 0 ? infoTickSize :
                                    secTickSize  > 0 ? secTickSize  :
                                    0.25m;

                if (EnableDetailedRiskLogging)
                {
                    RiskLog("468/RISK", $"DIAG tickValue={EffectiveTickValue:F2}{Security?.QuoteCurrency ?? "USD"}/t " +
                                          $"tickSize={EffectiveTickSize:F4}pts/t " +
                                          $"equity={EffectiveAccountEquity:F2}USD");
                }
            }
            catch (Exception ex)
            {
                RiskLog("468/RISK", $"UpdateDiagnostics error: {ex.Message}");
            }
        }

        // ====================== PASO 3: CALCULATION ENGINE ======================

        // Throttling state for calculation calls
        private string _lastCalcInputsHash = "";
        private DateTime _lastCalcLogTime = DateTime.MinValue;

        // Pulse logging for when EnableDetailedRiskLogging is false
        private DateTime _lastPulseLogTime = DateTime.MinValue;
        private int _lastDiagnosticRefreshBar = -1;

        /// <summary>
        /// Centralized security symbol detection with InstrumentInfo priority and logging
        /// </summary>
        private string _cachedSecurityCode;
        private string _cachedSymbolSource; // Capa 1: cachear también la fuente del símbolo

        // ====================== Capa 2 — throttle del motor de cálculo ======================
        private DateTime _lastCalcUtc = DateTime.MinValue;
        private int _lastCalcBar = -1;
        // _lastCalcInputsHash ya definido arriba en PASO 3
        private static readonly TimeSpan _calcCooldown = TimeSpan.FromSeconds(30);
        private const int _calcEveryNBars = 10; // dispara cada N velas (además de por cambio de inputs)
        // ===============================================================================
        private string GetEffectiveSecurityCode()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_cachedSecurityCode))
                    return _cachedSecurityCode;

                // Priority 1: InstrumentInfo.Instrument (modern ATAS API)
                var ii = GetType().GetProperty("InstrumentInfo",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)?.GetValue(this);
                if (ii != null)
                {
                    var p = ii.GetType().GetProperty("Instrument");
                    var name = p?.GetValue(ii) as string;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        if (EnableDetailedRiskLogging)
                            DebugLog.W("468/RISK", $"SYMBOL source=InstrumentInfo.Instrument value={name}");
                        _cachedSecurityCode = name.Trim().ToUpperInvariant();
                        _cachedSymbolSource = "InstrumentInfo.Instrument";
                        return _cachedSecurityCode;
                    }
                }

                // Priority 2: Security.Instrument
                if (!string.IsNullOrWhiteSpace(Security?.Instrument))
                {
                    if (EnableDetailedRiskLogging)
                        DebugLog.W("468/RISK", $"SYMBOL source=Security.Instrument value={Security.Instrument}");
                    _cachedSecurityCode = Security.Instrument.Trim().ToUpperInvariant();
                    _cachedSymbolSource = "Security.Instrument";
                    return _cachedSecurityCode;
                }
                // Priority 3: Security.Code
                if (!string.IsNullOrWhiteSpace(Security?.Code))
                {
                    if (EnableDetailedRiskLogging)
                        DebugLog.W("468/RISK", $"SYMBOL source=Security.Code value={Security.Code}");
                    _cachedSecurityCode = Security.Code.Trim().ToUpperInvariant();
                    _cachedSymbolSource = "Security.Code";
                    return _cachedSecurityCode;
                }
                // Priority 4: Base Instrument (obsoleto pero útil como fallback)
                if (!string.IsNullOrWhiteSpace(Instrument))
                {
                    if (EnableDetailedRiskLogging)
                        DebugLog.W("468/RISK", $"SYMBOL source=Instrument value={Instrument}");
                    _cachedSecurityCode = Instrument.Trim().ToUpperInvariant();
                    _cachedSymbolSource = "Instrument";
                    return _cachedSecurityCode;
                }

                DebugLog.W("468/RISK", "SYMBOL source=fallback value=UNKNOWN (no detection)");
                _cachedSecurityCode = "UNKNOWN";
                _cachedSymbolSource = "fallback";
                return _cachedSecurityCode;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/RISK", $"SYMBOL error: {ex.Message} -> fallback=UNKNOWN");
                _cachedSecurityCode = "UNKNOWN";
                _cachedSymbolSource = "error";
                return _cachedSecurityCode;
            }
        }

        /// <summary>
        /// PASO 3: CALCULATION ENGINE - Calculates position quantity based on selected mode
        /// NOTE: This function is pure calculation - does NOT affect actual trading until PASO 4
        /// Returns calculated quantity but actual trades still use original Quantity property
        /// Includes lot step quantization and instrument-based limits
        /// </summary>
        private int CalculateQuantity()
        {
            try
            {
                var mode = PositionSizingMode;
                var tickValue = GetEffectiveTickValue();
                var tickSize = EffectiveTickSize;
                var accountEquity = GetEffectiveAccountEquity();
                var slDistanceInTicks = GetStopLossDistanceInTicks();

                // Throttle logging - only log when inputs change significantly
                string currentInputsHash = $"{mode}|{tickValue:F2}|{slDistanceInTicks:F1}|{RiskPerTradeUsd:F0}|{RiskPercentOfAccount:F1}|{accountEquity:F0}";
                bool shouldLog = EnableDetailedRiskLogging &&
                               (currentInputsHash != _lastCalcInputsHash ||
                                (DateTime.UtcNow - _lastCalcLogTime).TotalSeconds > 30);

                if (shouldLog)
                {
                    var quoteCurrency = Security?.QuoteCurrency ?? "USD";
                    var accountCurrency = "USD"; // Portfolio balance is typically in account base currency
                    DebugLog.W("468/CALC", $"Starting calculation: mode={mode} " +
                                          $"tickValue={tickValue:F2}{quoteCurrency}/t tickSize={tickSize:F4}pts/t " +
                                          $"SLticks={slDistanceInTicks:F1} equity={accountEquity:F2}{accountCurrency}");
                    _lastCalcInputsHash = currentInputsHash;
                    _lastCalcLogTime = DateTime.UtcNow;
                }

                decimal rawQuantity = 0;

                switch (mode)
                {
                    case PositionSizingMode.Manual:
                        rawQuantity = CalculateQuantityManual(shouldLog);
                        break;

                    case PositionSizingMode.FixedRiskUSD:
                        rawQuantity = CalculateQuantityForFixedRisk(slDistanceInTicks, tickValue, shouldLog);
                        break;

                    case PositionSizingMode.PercentOfAccount:
                        rawQuantity = CalculateQuantityForPercentRisk(slDistanceInTicks, tickValue, accountEquity, shouldLog);
                        break;

                    default:
                        CalcLog("468/CALC", $"Unknown PositionSizingMode: {mode}, defaulting to Manual");
                        rawQuantity = Quantity;
                        break;
                }

                // Apply lot step quantization and instrument limits
                int finalQuantity = QuantizeToLotStep(rawQuantity, shouldLog);

                // Update diagnostic properties with calculation results
                LastAutoQty = finalQuantity;
                LastRiskPerContract = slDistanceInTicks * tickValue;
                LastStopDistance = slDistanceInTicks;
                LastRiskInput = mode == PositionSizingMode.FixedRiskUSD ? RiskPerTradeUsd :
                               mode == PositionSizingMode.PercentOfAccount ? accountEquity * (RiskPercentOfAccount / 100m) : 0;

                if (shouldLog)
                {
                    if (finalQuantity == 0)
                    {
                        DebugLog.W("468/CALC", $"ABORT_UNDERFUNDED: qty=0 -> entry would be skipped");
                    }
                    else
                    {
                        CalcLog("468/CALC", $"FINAL calculation result: qty={finalQuantity} " +
                                              $"(NOTE: actual trades use Quantity={Quantity} until PASO 4)");
                    }
                }

                // SNAPSHOT compacto (siempre útil para auditoría)
                try
                {
                    var sym = GetEffectiveSecurityCode();
                    var qc = Security?.QuoteCurrency ?? "USD";
                    var bars = CurrentBar; // Current bar as reference
                    CalcLog("468/CALC",
                        $"SNAPSHOT sym={sym} bars={bars} mode={mode} qty={finalQuantity} lastAutoQty={LastAutoQty} " +
                        $"slTicks={LastStopDistance:F1} rpc={LastRiskPerContract:F2}{qc} " +
                        $"equity={EffectiveAccountEquity:F2}USD tickValue={EffectiveTickValue:F2}{qc}/t " +
                        $"tickSize={EffectiveTickSize:F2}pts/t DRYRUN=ON");
                }
                catch { /* no romper cálculo por logging */ }

                return finalQuantity;
            }
            catch (Exception ex)
            {
                CalcLog("468/CALC", $"CalculateQuantity error: {ex.Message}, falling back to manual Quantity={Quantity}");
                return Quantity; // Safe fallback to manual quantity
            }
        }

        /// <summary>
        /// Calculate quantity for Manual mode - uses original Quantity with risk diagnostics
        /// </summary>
        private decimal CalculateQuantityManual(bool shouldLog)
        {
            decimal manualQty = Quantity;

            if (shouldLog)
            {
                decimal slTicks = GetStopLossDistanceInTicks();
                decimal tickValue = GetEffectiveTickValue();
                decimal riskPerContract = slTicks * tickValue;
                decimal totalRisk = manualQty * riskPerContract;
                var quoteCurrency = Security?.QuoteCurrency ?? "USD";

                CalcLog("468/CALC", $"MANUAL mode: qty={manualQty} (user-defined) " +
                                      $"riskPerContract={riskPerContract:F2}{quoteCurrency} " +
                                      $"totalRisk={totalRisk:F2}{quoteCurrency}");
            }

            return manualQty;
        }

        /// <summary>
        /// Calculate quantity for Fixed Risk USD mode with underfunded protection
        /// All math kept in ticks, converted to money only for risk calculation
        /// </summary>
        private decimal CalculateQuantityForFixedRisk(decimal slDistanceInTicks, decimal tickValue, bool shouldLog)
        {
            if (slDistanceInTicks <= 0 || tickValue <= 0 || RiskPerTradeUsd <= 0)
            {
                if (shouldLog)
                    DebugLog.W("468/CALC", $"Invalid inputs for FixedRisk: SL={slDistanceInTicks}t tickVal={tickValue} risk={RiskPerTradeUsd}");
                return Quantity; // Fallback to manual
            }

            // All risk calculation in quote currency (TickCost units)
            decimal riskPerContract = slDistanceInTicks * tickValue; // In quote currency
            decimal targetRisk = RiskPerTradeUsd; // Assumes USD = quote currency for now
            var quoteCurrency = Security?.QuoteCurrency ?? "USD";

            if (riskPerContract > targetRisk)
            {
                // UNDERFUNDED scenario: risk per contract exceeds target risk
                if (SkipIfUnderfunded)
                {
                    if (shouldLog)
                        DebugLog.W("468/CALC", $"ABORT_UNDERFUNDED: riskPerContract={riskPerContract:F2}{quoteCurrency} > " +
                                              $"targetRisk={targetRisk:F2}{quoteCurrency} -> qty=0 (entry will be skipped)");
                    return 0; // Return 0 to signal ABORT
                }
                else
                {
                    decimal minQty = MinQtyIfUnderfunded;
                    decimal actualRisk = riskPerContract * minQty;
                    if (shouldLog)
                        DebugLog.W("468/CALC", $"WARNING_UNDERFUNDED: forcing minQty={minQty} " +
                                              $"(actualRisk={actualRisk:F2}{quoteCurrency} > targetRisk={targetRisk:F2}{quoteCurrency})");
                    return minQty;
                }
            }

            decimal calculatedQty = Math.Floor(targetRisk / riskPerContract);

            if (shouldLog)
            {
                decimal actualRisk = calculatedQty * riskPerContract;
                DebugLog.W("468/CALC", $"FixedRiskUSD: targetRisk={targetRisk:F2}{quoteCurrency} " +
                                      $"riskPerContract={riskPerContract:F2}{quoteCurrency} " +
                                      $"-> rawQty={calculatedQty} actualRisk={actualRisk:F2}{quoteCurrency}");
            }

            return calculatedQty;
        }

        /// <summary>
        /// Calculate quantity for Percent of Account mode with underfunded protection
        /// Account equity is in account currency, risk calculation in quote currency
        /// </summary>
        private decimal CalculateQuantityForPercentRisk(decimal slDistanceInTicks, decimal tickValue, decimal accountEquity, bool shouldLog)
        {
            if (slDistanceInTicks <= 0 || tickValue <= 0 || accountEquity <= 0 || RiskPercentOfAccount <= 0)
            {
                if (shouldLog)
                    DebugLog.W("468/CALC", $"Invalid inputs for PercentRisk: SL={slDistanceInTicks}t tickVal={tickValue} " +
                                          $"equity={accountEquity} risk%={RiskPercentOfAccount}");
                return Quantity; // Fallback to manual
            }

            // Risk calculation: account currency -> quote currency (assuming 1:1 for now)
            decimal riskPerContract = slDistanceInTicks * tickValue; // In quote currency
            decimal targetRisk = accountEquity * (RiskPercentOfAccount / 100m); // In account currency
            var quoteCurrency = Security?.QuoteCurrency ?? "USD";
            var accountCurrency = "USD"; // Account balance typically in base currency

            if (riskPerContract > targetRisk)
            {
                // UNDERFUNDED scenario: risk per contract exceeds target risk
                if (SkipIfUnderfunded)
                {
                    if (shouldLog)
                        DebugLog.W("468/CALC", $"ABORT_UNDERFUNDED: riskPerContract={riskPerContract:F2}{quoteCurrency} > " +
                                              $"targetRisk={targetRisk:F2}{accountCurrency} ({RiskPercentOfAccount:F1}% of {accountEquity:F2}{accountCurrency}) " +
                                              $"-> qty=0 (entry will be skipped)");
                    return 0; // Return 0 to signal ABORT
                }
                else
                {
                    decimal minQty = MinQtyIfUnderfunded;
                    decimal actualRisk = riskPerContract * minQty;
                    if (shouldLog)
                        DebugLog.W("468/CALC", $"WARNING_UNDERFUNDED: forcing minQty={minQty} " +
                                              $"(actualRisk={actualRisk:F2}{quoteCurrency} > targetRisk={targetRisk:F2}{accountCurrency})");
                    return minQty;
                }
            }

            decimal calculatedQty = Math.Floor(targetRisk / riskPerContract);

            if (shouldLog)
            {
                decimal actualRisk = calculatedQty * riskPerContract;
                DebugLog.W("468/CALC", $"PercentOfAccount: equity={accountEquity:F2}{accountCurrency} risk%={RiskPercentOfAccount:F1}% " +
                                      $"targetRisk={targetRisk:F2}{accountCurrency} riskPerContract={riskPerContract:F2}{quoteCurrency} " +
                                      $"-> rawQty={calculatedQty} actualRisk={actualRisk:F2}{quoteCurrency}");
            }

            return calculatedQty;
        }

        /// <summary>
        /// Quantize raw quantity to instrument lot step and apply min/max limits
        /// Uses Security.LotSize, LotMinSize, LotMaxSize when available
        /// </summary>
        private int QuantizeToLotStep(decimal rawQuantity, bool shouldLog)
        {
            try
            {
                // Get lot constraints from Security
                decimal lotSize = 1; // Default lot step
                decimal lotMinSize = 1; // Default minimum
                decimal lotMaxSize = 1000; // Default maximum

                // TODO: Add LotSize constraints detection once ATAS API nullable types are confirmed
                // For now using safe defaults (1, 1, 1000) that work for most instruments
                // Will implement proper Security.LotSize/LotMinSize/LotMaxSize detection in future refinement

                // Quantize to lot step
                decimal quantizedQty = Math.Floor(rawQuantity / lotSize) * lotSize;

                // Apply min/max limits
                quantizedQty = Math.Max(lotMinSize, quantizedQty);
                quantizedQty = Math.Min(lotMaxSize, quantizedQty);

                int finalQty = (int)quantizedQty;

                if (shouldLog && (finalQty != rawQuantity || lotSize != 1))
                {
                    DebugLog.W("468/CALC", $"Lot quantization: raw={rawQuantity:F2} lotStep={lotSize} " +
                                          $"min={lotMinSize} max={lotMaxSize} -> final={finalQty}");
                }

                return finalQty;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/CALC", $"QuantizeToLotStep error: {ex.Message}, using raw quantity as int");
                return Math.Max(1, Math.Min(1000, (int)rawQuantity)); // Fallback to simple clamp
            }
        }

        /// <summary>
        /// Calculate current stop loss distance in ticks based on strategy settings
        /// Keeps all math in ticks end-to-end for precision
        /// </summary>
        private decimal GetStopLossDistanceInTicks()
        {
            try
            {
                // Base SL distance from StopOffsetTicks setting (already in ticks)
                decimal slDistanceInTicks = StopOffsetTicks;

                // If using SL from signal candle, add estimated additional distance
                if (UseSignalCandleSL)
                {
                    // Conservative estimate for signal candle SL extension
                    // In real implementation this would calculate actual distance from signal candle
                    slDistanceInTicks += 5; // Conservative 5-tick buffer for signal candle SL
                }

                // Apply safety limits (all in ticks)
                slDistanceInTicks = Math.Max(1, slDistanceInTicks); // Minimum 1 tick
                slDistanceInTicks = Math.Min(500, slDistanceInTicks); // Maximum 500 ticks (safety)

                return slDistanceInTicks;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/CALC", $"GetStopLossDistanceInTicks error: {ex.Message}, using fallback 10 ticks");
                return 10; // Conservative fallback
            }
        }

        // ============== Capa 2: hash de inputs de cálculo (para throttle inteligente) ==============
        private string ComputeCalcInputsHash()
        {
            try
            {
                // Entradas que afectan el qty calculado
                var mode = PositionSizingMode;

                // Usamos los efectivos/diagnóstico (no cambian comportamiento live)
                decimal tickValue = EffectiveTickValue;
                decimal slTicks   = GetStopLossDistanceInTicks();
                decimal eq        = EffectiveAccountEquity;
                decimal pct       = RiskPercentOfAccount;
                decimal fixedUsd  = RiskPerTradeUsd;
                decimal tsize     = EffectiveTickSize;

                // Símbolo efectivo por si hay overrides dependientes de él
                var sym = GetEffectiveSecurityCode();

                // Compacto y determinista
                return $"{mode}|{sym}|tv={tickValue:F4}|tsz={tsize:F4}|sl={slTicks:F2}|eq={eq:F2}|pct={pct:F4}|usd={fixedUsd:F2}";
            }
            catch
            {
                // Fallback: fuerza cálculo (hash distinto) si algo falla
                return Guid.NewGuid().ToString("N");
            }
        }
        // ===========================================================================================
    }
}