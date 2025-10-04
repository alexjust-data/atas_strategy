using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Shared;
using MyAtas.Risk.Engine;
using MyAtas.Risk.Models;

namespace MyAtas.Strategies
{
    // Position Sizing: Cálculo de quantity basado en riesgo (Manual/FixedUSD/PercentAccount)
    public partial class FourSixEightSimpleStrategy
    {
        // ====================== POSITION SIZING STATE ======================
        private DateTime _nextEquityProbeAt = DateTime.MinValue;
        private readonly RiskEngine _riskEngine = new RiskEngine();

        // ====================== ACCOUNT EQUITY DETECTION ======================

        /// <summary>
        /// Lee equity de la cuenta desde Portfolio (con reflection por compatibilidad brokers)
        /// </summary>
        private decimal ReadAccountEquityUSD()
        {
            try
            {
                // 1) Portfolio properties (Equity/Balance/Cash)
                if (Portfolio != null)
                {
                    foreach (var name in new[] { "Equity", "Balance", "Cash", "AccountBalance" })
                    {
                        var p = Portfolio.GetType().GetProperty(name);
                        if (p == null) continue;
                        try
                        {
                            var v = Convert.ToDecimal(p.GetValue(Portfolio));
                            if (v > 0m) return v;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return 0m;
        }

        // ====================== TICK VALUE RESOLUTION ======================

        /// <summary>
        /// Normaliza cadenas para comparar (mayúsculas, sin espacios/guiones, sin acentos)
        /// </summary>
        private static string NormalizeSymbol(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            s = s.ToUpperInvariant();
            s = s.Replace(" ", "").Replace("-", "").Replace("_", "");
            s = s.Replace("É", "E").Replace("Á", "A").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U").Replace("Ñ", "N");
            return s;
        }

        /// <summary>
        /// Resuelve tick value desde overrides o heurísticas por símbolo
        /// </summary>
        private decimal ResolveTickValueUsd(string securityNameOrCode, string overrides, decimal fallback)
        {
            var key = NormalizeSymbol(securityNameOrCode);

            // 1) Overrides de la UI: "MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10"
            if (!string.IsNullOrWhiteSpace(overrides))
            {
                foreach (var pair in overrides.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=');
                    if (kv.Length != 2) continue;
                    var k = NormalizeSymbol(kv[0]);
                    var vRaw = kv[1].Trim().Replace(',', '.');
                    if (!decimal.TryParse(vRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) continue;

                    // Coincidencia amplia: si el nombre/código contiene la clave normalizada
                    if (key.Contains(k)) return v;
                }
            }

            // 2) Heurísticas por símbolo (si el broker no da el código)
            if (key.Contains("MICROEMININASDAQ") || key.StartsWith("MNQ")) return 0.5m;
            if (key.StartsWith("NQ")) return 5m;
            if (key.StartsWith("MES")) return 1.25m;
            if (key.StartsWith("ES")) return 12.5m;
            if (key.Contains("MICROGOLD") || key.StartsWith("MGC")) return 1m;
            if (key.StartsWith("GC")) return 10m;

            return fallback;
        }

        // ====================== POSITION SIZING CALCULATION ======================

        /// <summary>
        /// Calcula la quantity para la entrada basándose en SizingMode
        /// </summary>
        private int CalculatePositionSize(decimal entryPrice, decimal stopPrice, int direction)
        {
            try
            {
                // Resolver equity efectivo (con priority: Override > Snapshot > Default)
                decimal equity = ManualAccountEquityOverride > 0m ? ManualAccountEquityOverride : EffectiveAccountEquity;
                if (equity <= 0m) equity = 10000m; // Fallback final

                // Calcular stop distance en ticks
                var tickSize = InternalTickSize;
                var stopDistance = Math.Abs(entryPrice - stopPrice);
                var stopTicks = (int)Math.Ceiling(stopDistance / tickSize);

                // Preparar contexto para RiskEngine
                var ctx = new EntryContext(
                    Account: Portfolio?.ToString() ?? "UNKNOWN",
                    Symbol: Security?.ToString() ?? "UNKNOWN",
                    Direction: direction > 0 ? MyAtas.Risk.Models.Direction.Long : MyAtas.Risk.Models.Direction.Short,
                    EntryPrice: entryPrice,
                    ApproxStopTicks: Math.Max(1, stopTicks),
                    TickSize: tickSize,
                    TickValueUSD: EffectiveTickValue > 0m ? EffectiveTickValue : 0.5m,
                    TimeUtc: DateTime.UtcNow
                );

                // Preparar configuración de sizing
                var sizingCfg = new SizingConfig(
                    Mode: PositionSizingMode.ToString(),
                    ManualQty: Quantity, // Usar Quantity actual de la estrategia
                    RiskUsd: RiskPerTradeUsd,
                    RiskPct: RiskPercentOfAccount,
                    AccountEquityOverride: equity,
                    TickValueOverrides: TickValueOverrides,
                    UnderfundedPolicy: UnderfundedPolicy.Min1,
                    MinQty: 1,
                    MaxQty: 1000
                );

                // Calcular quantity usando RiskEngine
                var sizer = new PositionSizer();
                var qty = sizer.ComputeQty(ctx, sizingCfg, out var reason);

                if (EnableDetailedRiskLogging)
                    DebugLog.W("468/SIZE", $"CalculatePositionSize: mode={PositionSizingMode} entry={entryPrice:F2} stop={stopPrice:F2} stopTicks={stopTicks} equity={equity:F2} → qty={qty} (reason: {reason})");

                return Math.Max(1, qty);
            }
            catch (Exception ex)
            {
                DebugLog.W("468/SIZE", $"CalculatePositionSize EX: {ex.Message} → fallback qty=1");
                return 1;
            }
        }

        /// <summary>
        /// Actualiza los valores de diagnóstico en la UI (Effective tick value, tick size, account equity)
        /// </summary>
        private void UpdateDiagnostics()
        {
            try
            {
                // Update tick value
                var secName = Security?.ToString() ?? "";
                EffectiveTickValue = ResolveTickValueUsd(secName, TickValueOverrides, 0.5m);

                // Update tick size
                EffectiveTickSize = InternalTickSize;

                // Update account equity
                EffectiveAccountEquity = ManualAccountEquityOverride > 0m
                    ? ManualAccountEquityOverride
                    : (AccountEquitySnapshot > 0m ? AccountEquitySnapshot : 10000m);
            }
            catch (Exception ex)
            {
                DebugLog.W("468/SIZE", $"UpdateDiagnostics EX: {ex.Message}");
            }
        }

        /// <summary>
        /// Actualiza AccountEquitySnapshot periódicamente (1x por segundo)
        /// </summary>
        private void UpdateAccountEquityPeriodically()
        {
            if (DateTime.UtcNow < _nextEquityProbeAt) return;

            try
            {
                AccountEquitySnapshot = ReadAccountEquityUSD();
                _nextEquityProbeAt = DateTime.UtcNow.AddSeconds(1);

                if (EnableDetailedRiskLogging && AccountEquitySnapshot > 0m)
                    DebugLog.W("468/SNAP", $"Equity≈{AccountEquitySnapshot:F2} USD");

                // Actualizar diagnostics en UI
                UpdateDiagnostics();
            }
            catch (Exception ex)
            {
                DebugLog.W("468/SIZE", $"UpdateAccountEquityPeriodically EX: {ex.Message}");
            }
        }
    }
}
