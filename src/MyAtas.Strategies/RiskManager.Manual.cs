using System;
using System.ComponentModel;
using System.Linq;
using ATAS.Strategies.Chart;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Shared;

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
            // Solo logs no intrusivos para ver prefijos/estados.
            try
            {
                if (!EnableLogging) return;

                var c = order?.Comment ?? "";
                var st = order.Status();
                var state = order.State;
                DebugLog.W("RM/ORD", $"id=? comment={c} state={state} status={st}");

                // Reglas de convivencia (futuras): ignorar 468, gestionar OwnerPrefix.
            }
            catch { /* noop */ }
        }
    }
}