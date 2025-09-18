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
        #region ===== RISK CORE (clean) =====
        // Flags Capa 0 (deben existir ya)
        // public bool EnableRiskManagement { get; set; } = false;
        // public bool RiskDryRun { get; set; } = true;
        private bool RMEnabled => EnableRiskManagement;
        private bool RMDryRunEffective => !EnableRiskManagement || RiskDryRun;

        // Gating de logs (respetan Capa 0)
        private void RiskLog(string tag, string message)
        {
            if (tag == "468/RISK" && !RMEnabled) return;
            MyAtas.Shared.DebugLog.W(tag, message);
        }
        private void CalcLog(string tag, string message)
        {
            if (tag == "468/CALC" && !RMEnabled) return;
            MyAtas.Shared.DebugLog.W(tag, message);
        }

        // Cache símbolo + fuente
        private string _cachedSecurityCode;
        private string _cachedSymbolSource;

        /// <summary>
        /// Símbolo efectivo (cacheado).
        /// Prioridad: InstrumentInfo.Instrument → Security.Instrument → Security.Code → Instrument → "UNKNOWN"
        /// </summary>
        private string GetEffectiveSecurityCode()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_cachedSecurityCode))
                    return _cachedSecurityCode;

                // P1: InstrumentInfo.Instrument (reflexión para compatibilidad)
                try
                {
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
                                RiskLog("468/RISK", $"SYMBOL source=InstrumentInfo.Instrument value={name}");
                            _cachedSecurityCode = name.Trim().ToUpperInvariant();
                            _cachedSymbolSource = "InstrumentInfo.Instrument";
                            return _cachedSecurityCode;
                        }
                    }
                }
                catch { /* best effort */ }

                // P2: Security.Instrument
                if (!string.IsNullOrWhiteSpace(Security?.Instrument))
                {
                    if (EnableDetailedRiskLogging)
                        RiskLog("468/RISK", $"SYMBOL source=Security.Instrument value={Security.Instrument}");
                    _cachedSecurityCode = Security.Instrument.Trim().ToUpperInvariant();
                    _cachedSymbolSource = "Security.Instrument";
                    return _cachedSecurityCode;
                }

                // P3: Security.Code
                if (!string.IsNullOrWhiteSpace(Security?.Code))
                {
                    if (EnableDetailedRiskLogging)
                        RiskLog("468/RISK", $"SYMBOL source=Security.Code value={Security.Code}");
                    _cachedSecurityCode = Security.Code.Trim().ToUpperInvariant();
                    _cachedSymbolSource = "Security.Code";
                    return _cachedSecurityCode;
                }

                // P4: Instrument (base)
                if (!string.IsNullOrWhiteSpace(Instrument))
                {
                    if (EnableDetailedRiskLogging)
                        RiskLog("468/RISK", $"SYMBOL source=Instrument value={Instrument}");
                    _cachedSecurityCode = Instrument.Trim().ToUpperInvariant();
                    _cachedSymbolSource = "Instrument";
                    return _cachedSecurityCode;
                }

                RiskLog("468/RISK", "SYMBOL source=fallback value=UNKNOWN (no detection)");
                _cachedSecurityCode = "UNKNOWN";
                _cachedSymbolSource = "fallback";
                return _cachedSecurityCode;
            }
            catch (Exception ex)
            {
                RiskLog("468/RISK", $"SYMBOL error: {ex.Message} -> fallback=UNKNOWN");
                _cachedSecurityCode = "UNKNOWN";
                _cachedSymbolSource = "error";
                return _cachedSecurityCode;
            }
        }

        /// <summary>
        /// Fuente única para tick value con override-first logic
        /// Prioridad: CSV override → Security.TickCost → fallback
        /// </summary>
        private decimal GetEffectiveTickValue()
        {
            // 1) Override por símbolo
            var sym = GetEffectiveSecurityCode(); // cacheado + log en INIT
            var fromCsv = TryGetTickValueOverride(sym); // tu parsing actual
            if (fromCsv.HasValue && fromCsv.Value > 0)
            {
                RiskLog("468/RISK", $"TICK-VALUE override hit sym={sym} value={fromCsv.Value:F2}");
                return fromCsv.Value;
            }

            // 2) Autodetección segura (Security.TickCost si existe y >0)
            var auto = (Security != null ? Security.TickCost : 0m);
            if (auto > 0)
            {
                RiskLog("468/RISK", $"TICK-VALUE auto-detected value={auto:F2}");
                return auto;
            }

            // 3) Fallback conocido (0.5m para MNQ/micro futures)
            var fb = 0.5m;
            RiskLog("468/RISK", $"TICK-VALUE fallback value={fb:F2}");
            return fb;
        }

        /// <summary>
        /// Equity efectivo sin CS1061. Patrón simplificado.
        /// Manual override → Portfolio.BalanceAvailable → Portfolio.Balance → fallback
        /// </summary>
        private decimal GetEffectiveAccountEquity()
        {
            // Manual override first
            if (ManualAccountEquityOverride > 0) return ManualAccountEquityOverride;

            try
            {
                var avail = Portfolio?.BalanceAvailable; // decimal?
                if (avail.HasValue && avail.Value > 0) return avail.Value;

                var bal = Portfolio?.Balance ?? 0m;     // decimal (no nullable)
                if (bal > 0) return bal;
            }
            catch { /* swallow & fallback */ }

            return 10000.0m; // fallback (o tu valor por defecto)
        }

        /// <summary>
        /// Diagnóstico consolidado: actualiza TickValue/Size/Equity con prioridad
        /// Security.TickSize → InstrumentInfo.TickSize → fallback interno.
        /// </summary>
        private void UpdateDiagnostics()
        {
            try
            {
                // Efectivos (diagnóstico)
                EffectiveTickValue = GetEffectiveTickValue();
                EffectiveAccountEquity = GetEffectiveAccountEquity();

                // TickSize
                decimal ts = 0m;
                if (Security != null && Security.TickSize > 0)
                    ts = Security.TickSize;

                if (ts <= 0m)
                {
                    try
                    {
                        var ii = GetType().GetProperty("InstrumentInfo",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic)?.GetValue(this);
                        if (ii != null)
                        {
                            var pTs = ii.GetType().GetProperty("TickSize");
                            var infoTs = (pTs?.GetValue(ii) as decimal?) ?? 0m;
                            if (infoTs > 0) ts = infoTs;
                        }
                    }
                    catch { /* best-effort */ }
                }

                if (ts <= 0m)
                    ts = EffectiveTickSize > 0 ? EffectiveTickSize : 0.25m; // último recurso

                EffectiveTickSize = ts;

                // Throttling + de-dup para DIAG logs
                if (EnableDetailedRiskLogging)
                {
                    var bar = CurrentBar;
                    var code = GetEffectiveSecurityCode();
                    var qc = Security?.QuoteCurrency ?? "USD";
                    var hash = $"{EffectiveTickValue:F4}|{EffectiveTickSize:F4}|{EffectiveAccountEquity:F2}";

                    if (hash != _lastDiagHash || _lastDiagRefreshBar < 0 || (bar - _lastDiagRefreshBar) >= 20)
                    {
                        RiskLog("468/RISK", $"DIAG [{code}] tickValue={EffectiveTickValue:F2}{qc}/t " +
                                            $"tickSize={EffectiveTickSize:F4}pts/t " +
                                            $"equity={EffectiveAccountEquity:F2}USD");
                        _lastDiagHash = hash;
                        _lastDiagRefreshBar = bar;
                    }
                }
            }
            catch (Exception ex)
            {
                RiskLog("468/RISK", $"UpdateDiagnostics error: {ex.Message}");
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
                RiskLog("468/RISK", $"ParseTickValueOverrides error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Try to get tick value override for specific symbol
        /// </summary>
        private decimal? TryGetTickValueOverride(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            var overrides = ParseTickValueOverrides();
            if (overrides.TryGetValue(symbol.ToUpperInvariant(), out var value) && value > 0)
                return value;

            return null;
        }

        /// <summary>
        /// CAPA 7 — Calcula distancia real de SL (en ticks) desde entryPrice y stopPrice.
        /// </summary>
        private decimal ComputeActualSlTicks(decimal entryPrice, decimal stopPrice)
        {
            try
            {
                var ts = (EffectiveTickSize > 0 ? EffectiveTickSize : (Security != null && Security.TickSize > 0 ? Security.TickSize : 0.25m));
                if (ts <= 0) ts = 0.25m;
                var distPts = Math.Abs(entryPrice - stopPrice);
                var ticks = distPts / ts;
                // Saneamiento
                if (ticks < 0) ticks = 0;
                if (ticks > 10000) ticks = 10000;
                return ticks;
            }
            catch { return 0m; }
        }

        /// <summary>
        /// CAPA 7 — Placeholder para encontrar la orden de STOP asociada a una ENTRY.
        /// TODO: Implementar con tu mecanismo real de brackets.
        /// </summary>
        private Order FindAttachedStopFor(Order entryOrder)
        {
            // Placeholder - retorna null por ahora
            // En implementación real, buscarías en _liveOrders o brackets por relación con entryOrder
            return null;
        }

        /// <summary>
        /// Detecta cambios intrasesión de modo DRY-RUN/LIVE y los registra
        /// </summary>
        private void CheckAndLogModeSwitch()
        {
            var eff = RMDryRunEffective;
            if (eff != _lastEffectiveDryRun)
            {
                RiskLog("468/RISK", $"MODE-SWITCH DryRun {(_lastEffectiveDryRun ? "ON" : "OFF")}->{(eff ? "ON" : "OFF")}");
                _lastEffectiveDryRun = eff;
            }
        }
        #endregion
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

        [System.ComponentModel.DisplayName("Enable Bracket Watchdog")]
        [Category("Execution")]
        public bool EnableBracketWatchdog { get; set; } = true;

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

        // CAPA 5 — Soft-engage: usar qty automática en vivo (OFF por defecto)
        [Category("Risk Management/Position Sizing"), DisplayName("Use auto quantity for live orders")]
        [Description("Cuando RM está activo y NO está en Dry-Run, usa LastAutoQty/CalculateQuantity para las órdenes en vivo.")]
        public bool UseAutoQuantityForLiveOrders { get; set; } = false;

        // CAPA 6/7 — Límites opcionales (diagnóstico, no imponen aún)
        [Category("Risk Management/Limits"), DisplayName("Max contracts (diagnostic)")]
        [Description("Límite superior sugerido para contratos por trade (diagnóstico; no impone todavía).")]
        public int MaxContracts { get; set; } = 1000;

        [Category("Risk Management/Limits"), DisplayName("Max risk per trade USD (diagnostic)")]
        [Description("Tope de riesgo/trade en USD (diagnóstico; no impone todavía, solo log WARN si se excede). 0 = deshabilitado.")]
        public decimal MaxRiskPerTradeUSD { get; set; } = 0m;

        // CAPA 7 — Currency (documental, sin conversión aún)
        [Category("Risk Management/Currency"), DisplayName("USD conversion factor (diagnostic)")]
        [Description("Factor documental de conversión a USD para logs cuando el instrumento no cotiza en USD. No se aplica aún al cálculo.")]
        public decimal CurrencyToUsdFactor { get; set; } = 1.0m;

        // Throttling and diagnostics
        [Category("Risk Management/Diagnostics"), DisplayName("Risk calc every N bars")]
        [Description("Frecuencia de cálculo de risk management para logging (diagnóstico).")]
        public int RiskCalcEveryNBars { get; set; } = 20;

        [Category("Risk Management/Diagnostics"), DisplayName("AutoQty cap (0=off)")]
        [Description("Límite duro opcional para auto quantity (0=sin límite). Solo diagnóstico por ahora.")]
        public int AutoQtyCap { get; set; } = 0;

        [Category("Risk Management/Diagnostics"), DisplayName("Market watchdog seconds")]
        [Description("Segundos de espera antes de avisar retraso en market order (típico en demo). 0=off.")]
        public int MarketWatchdogSec { get; set; } = 60;


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

        [Category("Risk Management/Diagnostics"), DisplayName("Underfunded abort last")]
        [ReadOnly(true)]
        public bool UnderfundedAbortLast { get; private set; } = false;

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

        // === BREAKEVEN STATE ===
        private List<decimal> _lastTpPrices = new List<decimal>(3);
        private decimal _lastEntryPrice = 0m;
        private DateTime _lastBreakevenMoveUtc = DateTime.MinValue;

        // === BRACKET WATCHDOG STATE ===
        private bool _scheduleAttachBrackets = false;

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

        // Risk Management session control and throttling
        private bool _riskInitDone = false;
        private int _lastDiagRefreshBar = -1;
        private string _lastDiagHash = "";
        private int _lastCalcBar = -1;
        private string _lastInputsHash = "";
        private bool _lastEffectiveDryRun = true; // para detectar cambios intrasesión

        // Market order watchdog for demo delays
        private DateTime _lastMarketSentUtc = DateTime.MinValue;
        private bool _marketOrderPending = false;

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

                // ---- RISK INIT (único por sesión) ----
                if (!_riskInitDone)
                {
                    var sym = GetEffectiveSecurityCode();
                    var qc = Security?.QuoteCurrency ?? "USD";
                    var effTs = EffectiveTickSize;

                    RiskLog("468/RISK", $"INIT flags EnableRiskManagement={EnableRiskManagement} RiskDryRun={RiskDryRun} effectiveDryRun={RMDryRunEffective}");
                    RiskLog("468/RISK", $"INIT SYMBOL source=effective value={sym} qc={qc} tickSize={effTs:F4}");
                    RiskLog("468/RISK", $"INIT OVERRIDES present={(string.IsNullOrWhiteSpace(TickValueOverrides) ? "NO" : "YES")}");

                    _lastEffectiveDryRun = RMDryRunEffective;
                    _riskInitDone = true;
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
                        bool inputsChanged = !string.Equals(inputsHash, _lastInputsHash, StringComparison.Ordinal);
                        bool barInterval   = (bar % RiskCalcEveryNBars) == 0;
                        bool cooldownOk    = (DateTime.UtcNow - _lastCalcUtc) >= _calcCooldown;

                        if (cooldownOk || inputsChanged || barInterval)
                        {
                            try
                            {
                                // Detectar cambios intrasesión de modo DRY-RUN/LIVE
                                CheckAndLogModeSwitch();

                                var qty = CalculateQuantity(); // dry-run: no toca Quantity real

                                var sym = GetEffectiveSecurityCode();
                                var qc  = Security?.QuoteCurrency ?? "USD";
                                var mode = PositionSizingMode;

                                // Snapshot consolidado único con estado real
                                var finalQty = AutoQtyCap > 0 ? Math.Min(qty, AutoQtyCap) : qty; // cap diagnóstico
                                var dryTag = RMDryRunEffective ? "DRYRUN=ON" : "DRYRUN=OFF";
                                CalcLog("468/CALC",
                                    $"SNAPSHOT sym={sym} bars={bar} mode={mode} qty={finalQty} slTicks={LastStopDistance:F1} " +
                                    $"rpc={LastRiskPerContract:F2}{qc} tickValue={EffectiveTickValue:F2}{qc}/t " +
                                    $"equity={EffectiveAccountEquity:F2}USD {dryTag}");

                                // Pulso mínimo cuando el log detallado está OFF (ritmo visible sin inundar)
                                if (!EnableDetailedRiskLogging)
                                {
                                    CalcLog("468/CALC", $"PULSE sym={sym} bar={bar} mode={mode} qty={qty}");
                                }

                                _lastInputsHash = inputsHash;
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
                        var securityCode = GetEffectiveSecurityCode(); // FIX: resolver UNKNOWN
                        var tickValue = GetEffectiveTickValue();
                        var equity = GetEffectiveAccountEquity();
                        // Capa 0: en RM OFF no emitir 468/RISK de rutina para no contaminar baseline
                        RiskLog("468/RISK", $"REFRESH sym={securityCode} bar={bar} tickValue={tickValue:F2} equity={equity:F2}");
                        _lastDiagnosticRefreshBar = bar;
                    }
                }
            }
            catch (Exception ex)
            {
                RiskLog("468/RISK", $"Pulse logging error: {ex.Message}");
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

                // ===== ENHANCED QTY DECISION with TRACING =====
                // Asegúrate de tener diagnósticos frescos antes del cálculo
                if (EnableRiskManagement)
                {
                    try
                    {
                        UpdateDiagnostics();
                        CalculateQuantity(); // Updates LastAutoQty and UnderfundedAbortLast
                        DebugLog.W("468/STR", $"TRACE: Post-calc LastAutoQty={LastAutoQty} UnderfundedAbort={UnderfundedAbortLast}");
                    }
                    catch (Exception ex)
                    {
                        DebugLog.W("468/STR", $"Risk calculation error: {ex.Message}");
                    }
                }

                var finalQty = DecideEffectiveQtyForEntry(Math.Max(1, Quantity));
                DebugLog.W("468/STR", $"TRACE: DecideEffectiveQty returned={finalQty} (manualQty={Quantity})");

                // Check for abort due to underfunded
                if (finalQty <= 0)
                {
                    DebugLog.W("468/STR", $"TRACE: ABORT ENTRY due to finalQty=0 (underfunded or size limits)");
                    DebugLog.W("468/STR", $"ENTRY ABORTED: finalQty={finalQty} (see 468/RISK logs for details)");
                    _pending = null;
                    return;
                }
                // ===== END UNIFIED QTY DECISION =====

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
                    SubmitMarket(dir, finalQty, bar, s.BarId);
                    _lastExecUid = s.Uid;

                    DebugLog.Critical("468/STR", $"ENTRY sent at N+1 bar={bar} (signal N={s.BarId}) dir={(dir>0?"BUY":"SELL")} qty={finalQty} - brackets will attach post-fill");
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

            // --- BRACKET WATCHDOG: Re-attach missing brackets (replay stability) ---
            if (IsFirstTickOf(bar))
            {
                try { BracketWatchdog(); } catch { /* best-effort */ }
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
                    // A) Resolver dirección real de ejecución vs intención inicial
                    if (status == OrderStatus.PartlyFilled || status == OrderStatus.Filled)
                    {
                        var executedDir = order.Direction == OrderDirections.Buy ? 1 : -1;
                        if (_entryDir != executedDir)
                        {
                            DebugLog.Critical("468/STR", $"ENTRY SIDE RESOLVE: prev={_entryDir} -> actual={(executedDir>0?"BUY":"SELL")}");
                            _entryDir = executedDir;
                        }
                        var effectivePrice = GetEffectiveFillPrice(order);
                        if (effectivePrice > 0)
                        {
                            _lastEntryPrice = effectivePrice;
                            DebugLog.W("468/STR", $"ENTRY FILL side={(_entryDir>0?"BUY":"SELL")} price={_lastEntryPrice:F2}");
                        }
                        else
                        {
                            // último recurso: usa Close del bar si no hay fill price (mejor que 0, evita saltarse BE)
                            var fallback = GetCandle(CurrentBar).Close;
                            _lastEntryPrice = fallback;
                            DebugLog.W("468/STR", $"ENTRY FILL price unresolved -> fallback close={_lastEntryPrice:F2}");
                        }

                        // C) Market order watchdog - detect demo delays
                        if (_marketOrderPending && MarketWatchdogSec > 0)
                        {
                            var elapsed = (DateTime.UtcNow - _lastMarketSentUtc).TotalSeconds;
                            if (elapsed > MarketWatchdogSec)
                            {
                                DebugLog.W("468/ORD", $"MARKET fill delayed {elapsed:F1}s (demo emulation?)");
                            }
                            _marketOrderPending = false;
                        }

                        // Schedule bracket attachment for watchdog
                        _scheduleAttachBrackets = true;
                    }

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
                // CAPA 7 — Cuando una ENTRY se llena y ya conocemos el SL real, validar consistencia
                if (RMEnabled && order != null && order.Status() == OrderStatus.Filled)
                {
                    // Detectar si es una entrada (usa tu propia condición si ya la tienes)
                    bool isEntry = order.Comment != null && order.Comment.Contains("468ENTRY");
                    if (isEntry)
                    {
                        var entryPrice = GetEffectiveFillPrice(order);

                        // Intenta localizar la orden de STOP asociada al bracket
                        // (Por ahora usamos un placeholder - sustituye por tu mecanismo real)
                        var stopOrder = FindAttachedStopFor(order); // TODO: usa tu propio método/colección
                        if (stopOrder != null && stopOrder.Price > 0)
                        {
                            var stopPrice = stopOrder.Price;
                            var actualTicks = ComputeActualSlTicks(entryPrice, stopPrice);

                            // Comparar con lo que usó el motor (diagnóstico)
                            var plannedTicks = LastStopDistance; // lo que CalculatedQuantity usó
                            var delta = actualTicks - plannedTicks;

                            var qc = Security?.QuoteCurrency ?? "USD";
                            var rpc = LastRiskPerContract; // ya en {qc}
                            CalcLog("468/CALC",
                                $"CONSISTENCY SL [entry={entryPrice:F2} stop={stopPrice:F2}] " +
                                $"planned={plannedTicks:F2}t actual={actualTicks:F2}t delta={delta:F2}t " +
                                $"rpc={rpc:F2}{qc}");

                            // Si hay drift notable, dejar WARN
                            if (Math.Abs(delta) >= 1m)
                                RiskLog("468/RISK", $"SL DRIFT WARNING: planned={plannedTicks:F2}t vs actual={actualTicks:F2}t (Δ={delta:F2}t)");
                        }
                    }
                }

                // === BREAKEVEN TRIGGER: por TP fill ===
                if (BreakevenMode == BreakevenMode.OnTPFill
                    && order != null
                    && (order.Comment?.StartsWith("468TP") ?? false)
                    && order.Status() == OrderStatus.Filled)
                {
                    // Determinar qué TP se llenó usando Comment con fallback por precio
                    int tpIndex = DetectTpIndexFromCommentOrPrice(order); // 0=TP1,1=TP2,2=TP3,-1=desconocido

                    bool trigger = false;
                    string reason = "unmapped";
                    if (tpIndex == 0 && TriggerOnTP1TouchFill) { trigger = true; reason = "TP1"; }
                    else if (tpIndex == 1 && TriggerOnTP2TouchFill) { trigger = true; reason = "TP2"; }
                    else if (tpIndex == 2 && TriggerOnTP3TouchFill) { trigger = true; reason = "TP3"; }
                    else if (tpIndex < 0)
                    {
                        // Si no pudimos mapear el índice, opcionalmente considerar TP1 por defecto si está enabled
                        if (TriggerOnTP1TouchFill) { trigger = true; reason = "TP?→fallback TP1"; }
                    }

                    if (trigger)
                    {
                        // Check entry price availability BEFORE proceeding
                        if (_lastEntryPrice <= 0m)
                        {
                            DebugLog.W("468/BRK", "SKIP BE: unknown entryPrice, attempting capture from current position");

                            // Attempt to recover entry price from position if available
                            try
                            {
                                var portfolio = Portfolio;
                                var security = Security;
                                if (portfolio != null && security != null)
                                {
                                    var getPos = portfolio.GetType().GetMethod("GetPosition", new[] { security.GetType() });
                                    var pos = getPos?.Invoke(portfolio, new[] { security });
                                    if (pos != null)
                                    {
                                        var pAvg = pos.GetType().GetProperty("AveragePrice") ?? pos.GetType().GetProperty("AvgPrice");
                                        if (pAvg != null)
                                        {
                                            var avgPrice = Convert.ToDecimal(pAvg.GetValue(pos));
                                            if (avgPrice > 0m)
                                            {
                                                _lastEntryPrice = avgPrice;
                                                DebugLog.W("468/BRK", $"RECOVERED entry price from position: {_lastEntryPrice:F2}");
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugLog.W("468/BRK", $"Failed to recover entry price: {ex.Message}");
                            }

                            // Final check after recovery attempt
                            if (_lastEntryPrice <= 0m)
                            {
                                DebugLog.W("468/BRK", "ABORT BE: entryPrice still unknown after recovery attempt");
                                return;
                            }
                        }

                        // Pequeño throttle para evitar múltiples movimientos seguidos
                        if ((DateTime.UtcNow - _lastBreakevenMoveUtc) < TimeSpan.FromSeconds(1))
                        {
                            DebugLog.W("468/BRK", "SKIP BE: throttled");
                        }
                        else
                        {
                            var trigPx = GetEffectiveFillPrice(order);
                            DebugLog.W("468/BRK", $"TRIGGER by {reason} fill @ {(trigPx>0m?trigPx:order.Price):F2} entryPrice={_lastEntryPrice:F2}");
                            MoveAllStopsToBreakevenAdvanced(_entryDir);
                        }
                    }
                    else
                    {
                        DebugLog.W("468/BRK", $"NO-BE: TP index={tpIndex}, config TP1={TriggerOnTP1TouchFill}, TP2={TriggerOnTP2TouchFill}, TP3={TriggerOnTP3TouchFill}");
                    }
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
            // Cachear los targets de TP para identificar TP1/2/3 en OnOrderChanged
            _lastTpPrices = (tpList != null) ? new List<decimal>(tpList) : new List<decimal>(0);
            var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;

            int enabled = tpList.Count;
            if (enabled <= 0)
            {
                // Sin TPs activos → SL único por la qty completa
                SubmitStop(null, coverSide, totalQty, slPx);
                DebugLog.W("468/STR", $"BRACKETS: SL-only {totalQty} @ {slPx:F2} (no TPs enabled)");

                // Log consistency check after SL-only bracket
                LogSlConsistencyIfPossible();
                return;
            }

            // Reparto de cantidad entre TPs habilitados (p.ej., 3→[2,1] si enabled=2)
            var qtySplit = SplitQtyForTPs(totalQty, enabled); // ya existe en tu código

            for (int i = 0; i < enabled; i++)
            {
                int legQty = Math.Max(1, qtySplit[i]);
                var oco = Guid.NewGuid().ToString("N");
                SubmitStop(oco, coverSide, legQty, slPx);
                SubmitLimit(oco, coverSide, legQty, tpList[i], i); // i = 0→TP1, 1→TP2, 2→TP3
            }

            DebugLog.W("468/STR", $"BRACKETS: SL={slPx:F2} | TPs={string.Join(",", tpList.Select(x=>x.ToString("F2")))} | Split=[{string.Join(",", qtySplit)}] | Total={totalQty}");

            // Log consistency check after brackets are attached
            LogSlConsistencyIfPossible();
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

        private void BracketWatchdog()
        {
            if (!EnableBracketWatchdog) return;

            var net = Math.Abs(GetNetPosition());
            if (net <= 0) { _scheduleAttachBrackets = false; return; }

            bool hasActiveSL = _liveOrders.Any(o => o?.Comment?.StartsWith("468SL:") == true && o.State == OrderStates.Active);
            bool hasActiveTP = _liveOrders.Any(o => o?.Comment?.StartsWith("468TP") == true && o.State == OrderStates.Active);

            if ((!hasActiveSL || !hasActiveTP) && _scheduleAttachBrackets)
            {
                // Recalcular precios desde _lastEntryPrice y _entryDir
                var (slPx, tpList) = BuildBracketPrices(_entryDir, /*signalBar*/CurrentBar, /*execBar*/CurrentBar);
                slPx = RoundToTick(slPx);
                for (int i=0;i<tpList.Count;i++) tpList[i] = RoundToTick(tpList[i]);

                var coverSide = _entryDir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                var oco = Guid.NewGuid().ToString("N");
                var qtySplit = SplitQtyForTPs(net, tpList.Count);
                SubmitStop(oco, coverSide, net, slPx);
                for (int i=0;i<tpList.Count;i++)
                    SubmitLimit(oco, coverSide, qtySplit[i], tpList[i], i);

                DebugLog.Critical("468/BRK", $"WATCHDOG re-attached brackets net={net} sl={slPx:F2} tps=[{string.Join(",",tpList)}]");
                _scheduleAttachBrackets = false;
            }
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

            // C) Market order watchdog - track timing for demo delay detection
            _lastMarketSentUtc = DateTime.UtcNow;
            _marketOrderPending = true;

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

        private void SubmitLimit(string oco, OrderDirections side, int qty, decimal px, int tpIndex /* 0-based */)
        {
            var idx = Math.Max(0, Math.Min(tpIndex, 2)) + 1; // 1..3
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
                Comment   = $"468TP{idx}:{DateTime.UtcNow:HHmmss}:{(oco!=null?oco.Substring(0,Math.Min(6,oco.Length)):"nooco")}"
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

        /// <summary>
        /// Cancela todos los SL activos y crea un único SL en breakeven (+offset) para la posición viva.
        /// Totalmente idempotente: si net=0 o no hay entryPrice, no hace nada.
        /// </summary>
        /// <summary>
        /// Legacy compatibility method that calls the enhanced version
        /// </summary>
        private void MoveAllStopsToBreakeven(int dir)
        {
            MoveAllStopsToBreakevenAdvanced(dir);
        }

        /// <summary>
        /// Detecta el índice TP (0-based) por Comment con fallback por precio para retrocompatibilidad
        /// </summary>
        private int DetectTpIndexFromCommentOrPrice(Order order)
        {
            try
            {
                var c = order?.Comment ?? "";
                if (c.StartsWith("468TP1:")) return 0;
                if (c.StartsWith("468TP2:")) return 1;
                if (c.StartsWith("468TP3:")) return 2;
                if (c.StartsWith("468TP:"))
                {
                    // Retrocompatibilidad: mapear por precio con tolerancia 0.5 tick
                    if (_lastTpPrices != null && _lastTpPrices.Count > 0)
                    {
                        var ts = (EffectiveTickSize > 0m) ? EffectiveTickSize : (Security?.TickSize ?? 0.25m);
                        if (ts <= 0m) ts = 0.25m;
                        var tol = ts * 0.5m;
                        for (int i = 0; i < _lastTpPrices.Count; i++)
                            if (Math.Abs(_lastTpPrices[i] - order.Price) <= tol)
                                return i;
                    }
                }
            }
            catch { /* best-effort */ }
            return -1; // desconocido
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

        /// <summary>
        /// ENHANCED: Cancela todos los SL activos en TODOS los grupos OCO y crea un único SL en breakeven (+offset) para la posición viva.
        /// Maneja múltiples órdenes SL asociadas a un mismo grupo para que todas se muevan juntas.
        /// </summary>
        private void MoveAllStopsToBreakevenAdvanced(int dir)
        {
            try
            {
                int net = Math.Abs(GetNetPosition());
                if (net <= 0)
                {
                    DebugLog.W("468/BRK", "SKIP BE: net=0 (no position)");
                    return;
                }
                if (_lastEntryPrice <= 0m)
                {
                    DebugLog.W("468/BRK", "SKIP BE: unknown entryPrice");
                    return;
                }

                // Breakeven = entry ± offset*tick
                var ts = (EffectiveTickSize > 0m) ? EffectiveTickSize : (Security?.TickSize ?? 0.25m);
                if (ts <= 0m) ts = 0.25m;
                var offsetTicks = Math.Max(0, BreakevenOffsetTicks);
                var bePx = dir > 0
                    ? _lastEntryPrice + offsetTicks * ts
                    : _lastEntryPrice - offsetTicks * ts;

                // Track OCO groups and their SL orders
                var ocoGroups = new Dictionary<string, List<Order>>();

                // Group SL orders by OCO ID
                foreach (var o in _liveOrders.ToList())
                {
                    if (o?.Comment?.StartsWith("468SL:") ?? false)
                    {
                        try
                        {
                            // Extract OCO group from order (ATAS has specific OCO handling)
                            var ocoId = ExtractOcoId(o);
                            if (!string.IsNullOrEmpty(ocoId))
                            {
                                if (!ocoGroups.ContainsKey(ocoId))
                                    ocoGroups[ocoId] = new List<Order>();
                                ocoGroups[ocoId].Add(o);
                            }
                            else
                            {
                                // Individual SL not in OCO group
                                if (!ocoGroups.ContainsKey("INDIVIDUAL"))
                                    ocoGroups["INDIVIDUAL"] = new List<Order>();
                                ocoGroups["INDIVIDUAL"].Add(o);
                            }
                        }
                        catch
                        {
                            // Fallback: treat as individual
                            if (!ocoGroups.ContainsKey("INDIVIDUAL"))
                                ocoGroups["INDIVIDUAL"] = new List<Order>();
                            ocoGroups["INDIVIDUAL"].Add(o);
                        }
                    }
                }

                int totalCanceledSL = 0;
                // Cancel ALL SL orders in ALL OCO groups
                foreach (var ocoGroup in ocoGroups)
                {
                    DebugLog.W("468/BRK", $"Canceling {ocoGroup.Value.Count} SL orders in group {ocoGroup.Key}");
                    foreach (var o in ocoGroup.Value)
                    {
                        try { CancelOrder(o); } catch { /* swallow */ }
                        totalCanceledSL++;
                        DebugLog.W("468/BRK", $"Cancelled SL: {o.Comment} from OCO group {ocoGroup.Key}");
                    }
                }

                // Crear nuevo SL unificado en breakeven
                var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                var newOco = Guid.NewGuid().ToString("N");
                SubmitStop(newOco, coverSide, net, bePx);
                _lastBreakevenMoveUtc = DateTime.UtcNow;
                DebugLog.Critical("468/BRK", $"BREAKEVEN MOVE COMPLETE → Canceled {totalCanceledSL} SL orders, Created unified SL {net} @ {bePx:F2} (entry={_lastEntryPrice:F2}, off={offsetTicks}t)");
            }
            catch (Exception ex)
            {
                DebugLog.W("468/BRK", $"MoveAllStopsToBreakevenAdvanced ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// Log de consistencia SL (diagnóstico)
        /// </summary>
        private void LogSlConsistencyIfPossible()
        {
            try
            {
                // Busca un stop activo (prefijo estándar de tus comentarios)
                var sl = _liveOrders.FirstOrDefault(o =>
                    o != null && (o.Comment?.StartsWith("468SL:") ?? false) && o.State == OrderStates.Active);

                if (sl == null || _lastEntryPrice <= 0) return;

                var actualTicks = Math.Abs(sl.Price - _lastEntryPrice) / Ticks(1);
                DebugLog.W("468/CALC", $"CONSISTENCY SL [entry={_lastEntryPrice:F2} stop={sl.Price:F2}] " +
                                       $"planned={LastStopDistance:F1}t actual={actualTicks:F1}t " +
                                       $"delta={(actualTicks - LastStopDistance):F1}t rpc={LastRiskPerContract:F2}USD");
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Helper to extract OCO group ID from an order
        /// </summary>
        private string ExtractOcoId(Order order)
        {
            try
            {
                // ATAS orders may have OCO properties - use reflection to find them
                var ocoProps = new[] { "OcoId", "OCOId", "GroupId", "LinkedOrderId", "ParentId" };

                foreach (var propName in ocoProps)
                {
                    var prop = order.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var value = prop.GetValue(order);
                        if (value != null && !string.IsNullOrEmpty(value.ToString()))
                        {
                            return value.ToString();
                        }
                    }
                }

                // Fallback: parse from comment if it contains OCO info
                var comment = order.Comment ?? "";
                if (comment.Contains("OCO:"))
                {
                    var ocoIndex = comment.IndexOf("OCO:");
                    if (ocoIndex >= 0)
                    {
                        var ocoSegment = comment.Substring(ocoIndex + 4);
                        var spaceIndex = ocoSegment.IndexOf(' ');
                        if (spaceIndex > 0)
                            return ocoSegment.Substring(0, spaceIndex);
                        else
                            return ocoSegment;
                    }
                }
            }
            catch
            {
                // Ignore errors in OCO extraction
            }

            return null; // No OCO group found
        }

        // ====================== HELPERS - FIXED ======================

        // ===== Helpers de fills/ejecución =====
        private decimal GetEffectiveFillPrice(object order)
        {
            if (order == null) return 0m;
            try
            {
                var t = order.GetType();
                string[] names = { "AveragePrice", "AvgFillPrice", "ExecutionPrice", "LastFillPrice", "FillPrice", "Price" };
                foreach (var n in names)
                {
                    var p = t.GetProperty(n);
                    if (p == null) continue;
                    var v = p.GetValue(order);
                    if (v is decimal d && d > 0) return d;
                    if (v is double dd && dd > 0) return (decimal)dd;
                    if (v is float ff && ff > 0) return (decimal)ff;
                }
            }
            catch { /* best-effort */ }
            return 0m;
        }
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
            var ts = (EffectiveTickSize > 0m) ? EffectiveTickSize : (Security?.TickSize ?? 0.25m);
            if (ts <= 0m) ts = 0.25m;
            var steps = Math.Round(price / ts, MidpointRounding.AwayFromZero);
            return steps * ts;
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

                    // ENHANCED: Capture market fill prices for breakeven triggering
                    if (c.StartsWith("468TP:"))
                    {
                        var tpFillPrice = GetEffectiveFillPrice(order);
                        if (tpFillPrice > 0m)
                        {
                            DebugLog.W("468/BRK", $"TP FILL DETECTED: {orderId} @ {tpFillPrice:F2} qty={netQty}");
                        }
                    }

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

            const decimal eps = 0.0001m;
            bool trendUp   = (gN - gN1) >  eps;
            bool trendDown = (gN1 - gN) >  eps;
            bool ok = dir > 0 ? trendUp : trendDown; // BUY exige subiendo; SELL exige bajando (estricto)

            DebugLog.W("468/STR", $"CONF#1 (GL slope @N+1) gN={gN:F5} gN1={gN1:F5} " +
                                   $"trend={(trendUp? "UP": (trendDown? "DOWN":"FLAT"))} -> {(ok? "OK":"FAIL")}");
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

        // ====================== PASO 3: CALCULATION ENGINE ======================

        // Throttling state for calculation calls
        private DateTime _lastCalcLogTime = DateTime.MinValue;

        // Pulse logging for when EnableDetailedRiskLogging is false
        private DateTime _lastPulseLogTime = DateTime.MinValue;
        private int _lastDiagnosticRefreshBar = -1;

        // Capa 2 — throttle del motor de cálculo
        private DateTime _lastCalcUtc = DateTime.MinValue;
        private static readonly TimeSpan _calcCooldown = TimeSpan.FromSeconds(30);
        private const int _calcEveryNBars = 10; // dispara cada N velas (además de por cambio de inputs)



        /// <summary>
        /// Decide la qty final que pasará a SubmitMarket, respetando soft-engage
        /// </summary>
        private int DecideEffectiveQtyForEntry(int manualQty)
        {
            // Siempre registra snapshot de riesgo (si RM activo), pero no fuerza qty a menos que toque
            var autoQty = LastAutoQty;
            var rpc     = LastRiskPerContract;

            // Condiciones para usar autoQty
            bool canUseAuto =
                EnableRiskManagement &&
                !RiskDryRun &&
                UseAutoQuantityForLiveOrders &&
                autoQty > 0 &&
                rpc > 0 &&
                !UnderfundedAbortLast; // setéalo dentro de CalculateQuantity()

            if (canUseAuto)
            {
                DebugLog.W("468/RISK", $"AUTO-QTY APPLIED qty={autoQty} (rpc={rpc:F2})");
                return autoQty;
            }

            DebugLog.W("468/RISK", $"AUTO-QTY IGNORED reason=" +
                $"{(EnableRiskManagement ? "" : "RM_OFF ")}" +
                $"{(RiskDryRun ? "DRY_RUN " : "")}" +
                $"{(UseAutoQuantityForLiveOrders ? "" : "FLAG_OFF ")}" +
                $"{(autoQty > 0 ? "" : "autoQty<=0 ")}" +
                $"{(rpc > 0 ? "" : "rpc<=0 ")}" +
                $"{(UnderfundedAbortLast ? "underfunded" : "")}".Trim());

            return manualQty;
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
                // Reset underfunded flag at start of calculation
                UnderfundedAbortLast = false;

                var mode = PositionSizingMode;
                var tickValue = GetEffectiveTickValue();
                var tickSize = EffectiveTickSize;
                var accountEquity = GetEffectiveAccountEquity();

                // Use real SL alignment if we have trading context, otherwise fallback
                bool useRealSL = (_entryDir != 0 && _lastSignalBar >= 0);
                var slDistanceInTicks = useRealSL
                    ? ComputeRiskTicksFromRealSL(_entryDir, _lastSignalBar, CurrentBar)
                    : GetStopLossDistanceInTicks();

                // Throttle logging - only log when inputs change significantly
                string currentInputsHash = $"{mode}|{tickValue:F2}|{slDistanceInTicks:F1}|{RiskPerTradeUsd:F0}|{RiskPercentOfAccount:F1}|{accountEquity:F0}";
                bool shouldLog = EnableDetailedRiskLogging &&
                               (currentInputsHash != _lastInputsHash ||
                                (DateTime.UtcNow - _lastCalcLogTime).TotalSeconds > 30);

                if (shouldLog)
                {
                    var quoteCurrency = Security?.QuoteCurrency ?? "USD";
                    var accountCurrency = "USD"; // Portfolio balance is typically in account base currency
                    CalcLog("468/CALC", $"Starting calculation: mode={mode} " +
                                          $"tickValue={tickValue:F2}{quoteCurrency}/t tickSize={tickSize:F4}pts/t " +
                                          $"SLticks={slDistanceInTicks:F1} (src={(useRealSL ? "realSL" : "fallback")}) " +
                                          $"equity={accountEquity:F2}{accountCurrency}");
                    _lastInputsHash = currentInputsHash;
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

                // CAPA 7 — Validación de límites (solo logs; no impone)
                try
                {
                    var qc = Security?.QuoteCurrency ?? "USD";
                    var actualRisk = LastAutoQty * LastRiskPerContract;

                    if (MaxContracts > 0 && LastAutoQty > MaxContracts)
                        RiskLog("468/RISK", $"LIMIT WARN: qty={LastAutoQty} > MaxContracts={MaxContracts} (diagnostic only)");

                    if (MaxRiskPerTradeUSD > 0 && actualRisk > MaxRiskPerTradeUSD)
                        RiskLog("468/RISK", $"LIMIT WARN: actualRisk={actualRisk:F2}{qc} > MaxRiskPerTradeUSD={MaxRiskPerTradeUSD:F2}USD (diagnostic only)");
                }
                catch { /* no-op */ }

                if (shouldLog)
                {
                    if (finalQuantity == 0)
                    {
                        CalcLog("468/CALC", $"ABORT_UNDERFUNDED: qty=0 -> entry would be skipped");
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
                    CalcLog("468/CALC", $"Invalid inputs for FixedRisk: SL={slDistanceInTicks}t tickVal={tickValue} risk={RiskPerTradeUsd}");
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
                        CalcLog("468/CALC", $"ABORT_UNDERFUNDED: riskPerContract={riskPerContract:F2}{quoteCurrency} > " +
                                              $"targetRisk={targetRisk:F2}{quoteCurrency} -> qty=0 (entry will be skipped)");
                    UnderfundedAbortLast = true; // Set flag for DecideEffectiveQtyForEntry
                    return 0; // Return 0 to signal ABORT
                }
                else
                {
                    decimal minQty = MinQtyIfUnderfunded;
                    decimal actualRisk = riskPerContract * minQty;
                    if (shouldLog)
                        CalcLog("468/CALC", $"WARNING_UNDERFUNDED: forcing minQty={minQty} " +
                                              $"(actualRisk={actualRisk:F2}{quoteCurrency} > targetRisk={targetRisk:F2}{quoteCurrency})");
                    return minQty;
                }
            }

            decimal calculatedQty = Math.Floor(targetRisk / riskPerContract);

            if (shouldLog)
            {
                decimal actualRisk = calculatedQty * riskPerContract;
                CalcLog("468/CALC", $"FixedRiskUSD: targetRisk={targetRisk:F2}{quoteCurrency} " +
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
                    CalcLog("468/CALC", $"Invalid inputs for PercentRisk: SL={slDistanceInTicks}t tickVal={tickValue} " +
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
                        CalcLog("468/CALC", $"ABORT_UNDERFUNDED: riskPerContract={riskPerContract:F2}{quoteCurrency} > " +
                                              $"targetRisk={targetRisk:F2}{accountCurrency} ({RiskPercentOfAccount:F1}% of {accountEquity:F2}{accountCurrency}) " +
                                              $"-> qty=0 (entry will be skipped)");
                    UnderfundedAbortLast = true; // Set flag for DecideEffectiveQtyForEntry
                    return 0; // Return 0 to signal ABORT
                }
                else
                {
                    decimal minQty = MinQtyIfUnderfunded;
                    decimal actualRisk = riskPerContract * minQty;
                    if (shouldLog)
                        CalcLog("468/CALC", $"WARNING_UNDERFUNDED: forcing minQty={minQty} " +
                                              $"(actualRisk={actualRisk:F2}{quoteCurrency} > targetRisk={targetRisk:F2}{accountCurrency})");
                    return minQty;
                }
            }

            decimal calculatedQty = Math.Floor(targetRisk / riskPerContract);

            if (shouldLog)
            {
                decimal actualRisk = calculatedQty * riskPerContract;
                CalcLog("468/CALC", $"PercentOfAccount: equity={accountEquity:F2}{accountCurrency} risk%={RiskPercentOfAccount:F1}% " +
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
                    CalcLog("468/CALC", $"Lot quantization: raw={rawQuantity:F2} lotStep={lotSize} " +
                                          $"min={lotMinSize} max={lotMaxSize} -> final={finalQty}");
                }

                return finalQty;
            }
            catch (Exception ex)
            {
                CalcLog("468/CALC", $"QuantizeToLotStep error: {ex.Message}, using raw quantity as int");
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
                CalcLog("468/CALC", $"GetStopLossDistanceInTicks error: {ex.Message}, using fallback 10 ticks");
                return 10; // Conservative fallback
            }
        }

        /// <summary>
        /// Calcula distancia de riesgo usando el SL real que se enviará en brackets
        /// Máxima coherencia: mismo SL que BuildBracketPrices usa
        /// </summary>
        /// <summary>
        /// ENHANCED: Computes SL distance using actual bracket construction logic for context-aware risk sizing
        /// This ensures calculated quantities align with the actual SL distances that will be used in brackets
        /// </summary>
        private decimal ComputeRiskTicksFromRealSL(int dir, int signalBar, int execBar)
        {
            try
            {
                // Use the same logic as BuildBracketPrices to ensure consistency
                var (slPx, tp1Px) = BuildBracketPrices(dir, signalBar, execBar);

                // Get realistic entry price estimation
                decimal entryPx = 0m;

                // Priority 1: Use captured entry price if available
                if (_lastEntryPrice > 0m)
                {
                    entryPx = _lastEntryPrice;
                }
                // Priority 2: Use execution bar open (most realistic for market orders)
                else if (execBar <= CurrentBar)
                {
                    entryPx = GetCandle(execBar).Open;
                }
                // Priority 3: Fallback to signal bar close
                else
                {
                    entryPx = GetCandle(signalBar).Close;
                }

                // Final fallback
                if (entryPx <= 0m)
                {
                    entryPx = GetCandle(Math.Min(signalBar, CurrentBar)).Close;
                }

                var distPoints = Math.Abs(entryPx - slPx);
                var tickSize = EffectiveTickSize > 0m ? EffectiveTickSize : (Security?.TickSize ?? 0.25m);
                var ticks = distPoints / tickSize;

                var clampedTicks = Math.Max(1m, Math.Min(500m, ticks)); // límites de seguridad

                // Enhanced logging for risk calculation debugging
                if (EnableDetailedRiskLogging)
                {
                    RiskLog("468/RISK", $"CONTEXT_SL dir={dir} entry={entryPx:F2} sl={slPx:F2} dist={distPoints:F2}pts ticks={ticks:F2} final={clampedTicks:F2}");
                }

                return clampedTicks;
            }
            catch (Exception ex)
            {
                RiskLog("468/RISK", $"ComputeRiskTicksFromRealSL error: {ex.Message}, using fallback");
                return GetStopLossDistanceInTicks(); // Fallback to basic calculation
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