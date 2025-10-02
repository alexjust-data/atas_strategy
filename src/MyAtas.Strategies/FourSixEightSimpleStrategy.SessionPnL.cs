using System;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Shared;

namespace MyAtas.Strategies
{
    // Session P&L tracking: Realized + Unrealized P&L desde activación hasta desactivación
    public partial class FourSixEightSimpleStrategy
    {
        // ====================== SESSION P&L STATE ======================
        private decimal _sessionStartingEquity = 0m;     // Equity inicial al activar estrategia
        private int _sessionPositionQty = 0;             // Qty actual de posición para P&L unrealized
        private decimal _sessionRealizedPnL = 0m;        // P&L realizado acumulado desde activación

        // ====================== HELPERS ======================
        private decimal ResolveTickValueUSD()
        {
            try
            {
                // Usar EffectiveTickValue si ya está calculado
                if (EffectiveTickValue > 0m)
                    return EffectiveTickValue;

                // Fallback final: usar valor por defecto
                return 0.5m;
            }
            catch
            {
                return 0.5m;
            }
        }

        private decimal GetLastPriceSafe()
        {
            try
            {
                return GetCandle(CurrentBar).Close;
            }
            catch
            {
                return 0m;
            }
        }

        private decimal ExtractAvgFillPriceFromOrder(Order order)
        {
            try
            {
                foreach (var name in new[] { "AvgPrice", "AveragePrice", "AvgFillPrice", "Price" })
                {
                    var p = order?.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Convert.ToDecimal(p.GetValue(order));
                    if (v > 0m) return v;
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/PNL", $"ExtractAvgFillPriceFromOrder failed: {ex.Message}");
            }
            return 0m;
        }

        // ====================== SESSION P&L TRACKING ======================

        /// <summary>
        /// Actualiza Session P&L = Realized + Unrealized
        /// Llamar desde OnCalculate() cada tick para actualización en tiempo real
        /// </summary>
        private void UpdateSessionPnL()
        {
            try
            {
                // Calcular P&L no realizado de posición abierta usando tracking interno
                decimal unrealizedPnL = 0m;
                if (_sessionPositionQty != 0 && _entryPrice > 0m)
                {
                    var lastPrice = GetLastPriceSafe();
                    var tickValue = ResolveTickValueUSD();
                    var tickSize = InternalTickSize;

                    // P&L = (priceDiff / tickSize) × tickValue × abs(qty)
                    // LONG: priceDiff = lastPrice - entryPrice
                    // SHORT: priceDiff = entryPrice - lastPrice
                    var priceDiff = _sessionPositionQty > 0 ? (lastPrice - _entryPrice) : (_entryPrice - lastPrice);
                    var ticks = priceDiff / tickSize;
                    unrealizedPnL = ticks * tickValue * Math.Abs(_sessionPositionQty);

                    if (Math.Abs(unrealizedPnL) > 0.01m)
                        DebugLog.W("468/PNL", $"Unrealized: qty={_sessionPositionQty} entry={_entryPrice:F2} last={lastPrice:F2} priceDiff={priceDiff:F2} ticks={ticks:F2} tickVal={tickValue:F2} → unrealized={unrealizedPnL:F2}");
                }

                // Session P&L = Realized + Unrealized
                SessionPnL = _sessionRealizedPnL + unrealizedPnL;

                if (Math.Abs(SessionPnL) > 0.01m || Math.Abs(unrealizedPnL) > 0.01m)
                    DebugLog.W("468/PNL", $"SessionPnL={SessionPnL:F2} (Realized={_sessionRealizedPnL:F2} Unrealized={unrealizedPnL:F2})");
            }
            catch (Exception ex)
            {
                DebugLog.W("468/PNL", $"UpdateSessionPnL EX: {ex.Message}");
            }
        }

        /// <summary>
        /// Registra entrada de posición para tracking de P&L
        /// Llamar cuando se llena orden ENTRY
        /// </summary>
        private void TrackPositionEntry(decimal entryPrice, int qty, int direction)
        {
            try
            {
                if (_sessionPositionQty == 0)
                {
                    // Nueva posición
                    _sessionPositionQty = qty * direction;  // signed: +LONG / -SHORT
                    // NO sobrescribir _entryPrice si ya tiene valor (fue capturado por UpdateEntryPriceFromOrder)
                    DebugLog.W("468/PNL", $"Position entry: Price={entryPrice:F2} Qty={qty} Dir={direction} → Tracking started (using _entryPrice={_entryPrice:F2})");
                }
                else
                {
                    // Incremento de posición (promedio ponderado - raro en esta estrategia pero por completitud)
                    var totalQty = Math.Abs(_sessionPositionQty) + Math.Abs(qty);
                    _entryPrice = (_entryPrice * Math.Abs(_sessionPositionQty) + entryPrice * Math.Abs(qty)) / totalQty;
                    _sessionPositionQty = totalQty * direction;
                    DebugLog.W("468/PNL", $"Position add: NewEntry={entryPrice:F2} Qty={qty} → AvgEntry={_entryPrice:F2} TotalQty={totalQty}");
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/PNL", $"TrackPositionEntry EX: {ex.Message}");
            }
        }

        /// <summary>
        /// Registra cierre de posición (TP/SL fill) y calcula P&L realizado
        /// </summary>
        private void TrackPositionClose(decimal exitPrice, int qty, int direction)
        {
            try
            {
                if (_sessionPositionQty == 0 || _entryPrice == 0m)
                {
                    DebugLog.W("468/PNL", "TrackPositionClose: No position tracked to close");
                    return;
                }

                var tickValue = ResolveTickValueUSD();
                var tickSize = InternalTickSize;

                // Calcular P&L de la porción cerrada
                // LONG: P&L = (exitPrice - entryPrice) × qty × (tickValue / tickSize)
                // SHORT: P&L = (entryPrice - exitPrice) × qty × (tickValue / tickSize)
                var priceDiff = direction > 0 ? (exitPrice - _entryPrice) : (_entryPrice - exitPrice);
                var ticks = priceDiff / tickSize;
                var tradePnL = ticks * tickValue * Math.Abs(qty);

                _sessionRealizedPnL += tradePnL;

                DebugLog.W("468/PNL", $"Position close: Entry={_entryPrice:F2} Exit={exitPrice:F2} Qty={qty} Dir={direction} → P&L={tradePnL:F2} (Total Realized={_sessionRealizedPnL:F2})");

                // Actualizar posición restante
                _sessionPositionQty = Math.Abs(_sessionPositionQty) - Math.Abs(qty);
                if (_sessionPositionQty == 0)
                {
                    // Posición completamente cerrada - NO resetear _entryPrice aquí porque puede haber otro trade en la sesión
                    DebugLog.W("468/PNL", "Position fully closed");
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/PNL", $"TrackPositionClose EX: {ex.Message}");
            }
        }

        /// <summary>
        /// Resetea tracking de Session P&L
        /// Llamar en OnStopped() cuando se desactiva la estrategia
        /// </summary>
        private void ResetSessionPnL()
        {
            try
            {
                DebugLog.W("468/PNL", $"Session ended - Final P&L: {SessionPnL:F2} USD (Realized: {_sessionRealizedPnL:F2})");
                _sessionStartingEquity = 0m;
                _sessionPositionQty = 0;
                _sessionRealizedPnL = 0m;
                SessionPnL = 0m;
            }
            catch (Exception ex)
            {
                DebugLog.W("468/PNL", $"ResetSessionPnL EX: {ex.Message}");
            }
        }
    }
}
