using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using ATAS.DataFeedsCore;
using ATAS.Types;
using MyAtasIndicator.Shared;

namespace MyAtasIndicator.Strategies
{
    [DisplayName("468 Strategy - Simple")]
    public class FourSixEightStrategy : ChartStrategy
    {
        #region Parameters
        
        [Display(Name = "Volumen por Orden", GroupName = "Trading", Order = 1)]
        public int OrderVolume { get; set; } = 1;
        
        [Display(Name = "Habilitar Trading", GroupName = "Trading", Order = 2)]
        public bool EnableTrading { get; set; } = false;
        
        [Display(Name = "Stop Loss (Ticks)", GroupName = "Risk Management", Order = 5)]
        public int StopLossTicks { get; set; } = 50;
        
        [Display(Name = "Take Profit (Ticks)", GroupName = "Risk Management", Order = 6)]
        public int TakeProfitTicks { get; set; } = 100;
        
        [Display(Name = "Período Williams %R", GroupName = "Indicator", Order = 10)]
        public int WilliamsPeriod { get; set; } = 40;
        
        [Display(Name = "Habilitar Logs", GroupName = "Debug", Order = 20)]
        public bool EnableLogging { get; set; } = true;
        
        #endregion

        #region Private Fields
        
        private bool _lastSignalBullish = false;
        private bool _lastSignalBearish = false;
        private bool _waitingForNewBar = false;
        private int _lastSignalBar = -1;
        
        // Trading state
        private Order? _currentOrder;
        private Order? _stopLossOrder;
        private Order? _takeProfitOrder;
        private bool _hasPosition = false;
        private decimal _entryPrice = 0m;
        
        // Cache for Williams %R values - IDENTICAL to indicator
        private readonly Dictionary<int, List<decimal>> _cache = new Dictionary<int, List<decimal>>();
        private const int kWPR = 50000;
        
        #endregion

        #region Strategy Events

        protected override void OnStarted()
        {
            if (EnableLogging)
            {
                // Strategy iniciada - registrar en logs si es necesario
            }
        }

        protected override void OnStopped()
        {
            if (EnableLogging)
            {
                // Strategy detenida - registrar en logs si es necesario
            }
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            // Calcular y cachear Williams %R para TODOS los bars (igual que el indicador)
            var wpr = CalculateWilliamsR(bar, WilliamsPeriod);
            SetCache(kWPR, bar, wpr);
            
            // Solo operar en tiempo real (última barra)
            if (bar < CurrentBar - 1) 
                return;
                
            // Usar EXACTAMENTE la misma lógica que el indicador
            var prevWpr = bar > 0 ? GetPrevCached(kWPR, bar - 1) : wpr;
            
            // Detectar cruces del Williams %R - IDÉNTICO al indicador
            bool crossOverWpr = bar > 0 && wpr > -50m && prevWpr <= -50m;   // Cambio Alcista
            bool crossUnderWpr = bar > 0 && wpr < -50m && prevWpr >= -50m;  // Cambio Bajista
            
            // Procesar señales
            if (crossOverWpr && !_lastSignalBullish)
            {
                ProcessBullishSignal(bar);
            }
            else if (crossUnderWpr && !_lastSignalBearish)
            {
                ProcessBearishSignal(bar);
            }
            
            // Resetear flags si no hay señales
            if (!crossOverWpr) _lastSignalBullish = false;
            if (!crossUnderWpr) _lastSignalBearish = false;
        }

        #endregion

        #region Trading Logic

        private void ProcessBullishSignal(int bar)
        {
            if (EnableLogging)
            {
                // SEÑAL ALCISTA detectada - WPR cruzó arriba de -50
            }
                
            _lastSignalBullish = true;
            _lastSignalBar = bar;
            
            // Ejecutar orden LONG si trading está habilitado y no hay posición
            if (EnableTrading && !_hasPosition && _currentOrder == null)
            {
                ExecuteBuyOrder();
            }
        }

        private void ProcessBearishSignal(int bar)
        {
            if (EnableLogging)
            {
                // SEÑAL BAJISTA detectada - WPR cruzó abajo de -50
            }
                
            _lastSignalBearish = true;
            _lastSignalBar = bar;
            
            // Ejecutar orden SHORT si trading está habilitado y no hay posición
            if (EnableTrading && !_hasPosition && _currentOrder == null)
            {
                ExecuteSellOrder();
            }
        }

        private void ExecuteBuyOrder()
        {
            try 
            {
                _currentOrder = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = OrderDirections.Buy,
                    Type = OrderTypes.Market,
                    QuantityToFill = OrderVolume
                };
                
                OpenOrder(_currentOrder);
            }
            catch (Exception)
            {
                if (EnableLogging)
                {
                    // Error al crear orden BUY
                }
            }
        }
        
        private void ExecuteSellOrder()
        {
            try 
            {
                _currentOrder = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = OrderDirections.Sell,
                    Type = OrderTypes.Market,
                    QuantityToFill = OrderVolume
                };
                
                OpenOrder(_currentOrder);
            }
            catch (Exception)
            {
                if (EnableLogging)
                {
                    // Error al crear orden SELL
                }
            }
        }
        
        private void PlaceStopLoss(OrderDirections direction, decimal entryPrice)
        {
            if (StopLossTicks <= 0) return;
            
            try
            {
                decimal stopPrice;
                OrderDirections stopDirection;
                
                if (direction == OrderDirections.Buy)
                {
                    // Para posición LONG, SL es SELL por debajo del precio de entrada
                    stopPrice = entryPrice - (StopLossTicks * Security.TickSize);
                    stopDirection = OrderDirections.Sell;
                }
                else
                {
                    // Para posición SHORT, SL es BUY por encima del precio de entrada
                    stopPrice = entryPrice + (StopLossTicks * Security.TickSize);
                    stopDirection = OrderDirections.Buy;
                }
                
                _stopLossOrder = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = stopDirection,
                    Type = OrderTypes.Stop,
                    Price = stopPrice,
                    QuantityToFill = OrderVolume
                };
                
                OpenOrder(_stopLossOrder);
            }
            catch (Exception)
            {
                if (EnableLogging)
                {
                    // Error al crear Stop Loss
                }
            }
        }
        
        private void PlaceTakeProfit(OrderDirections direction, decimal entryPrice)
        {
            if (TakeProfitTicks <= 0) return;
            
            try
            {
                decimal targetPrice;
                OrderDirections targetDirection;
                
                if (direction == OrderDirections.Buy)
                {
                    // Para posición LONG, TP es SELL por encima del precio de entrada
                    targetPrice = entryPrice + (TakeProfitTicks * Security.TickSize);
                    targetDirection = OrderDirections.Sell;
                }
                else
                {
                    // Para posición SHORT, TP es BUY por debajo del precio de entrada
                    targetPrice = entryPrice - (TakeProfitTicks * Security.TickSize);
                    targetDirection = OrderDirections.Buy;
                }
                
                _takeProfitOrder = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = targetDirection,
                    Type = OrderTypes.Limit,
                    Price = targetPrice,
                    QuantityToFill = OrderVolume
                };
                
                OpenOrder(_takeProfitOrder);
            }
            catch (Exception)
            {
                if (EnableLogging)
                {
                    // Error al crear Take Profit
                }
            }
        }

        #endregion

        #region Order Events
        
        protected override void OnOrderChanged(Order order)
        {
            if (order == _currentOrder)
            {
                switch (order.Status())
                {
                    case OrderStatus.Placed:
                        // Orden principal colocada
                        break;
                        
                    case OrderStatus.Filled:
                        // Orden principal ejecutada - establecer posición
                        _hasPosition = true;
                        _entryPrice = order.Price;
                        
                        // Colocar Stop Loss y Take Profit
                        PlaceStopLoss(order.Direction, _entryPrice);
                        PlaceTakeProfit(order.Direction, _entryPrice);
                        
                        _currentOrder = null;
                        break;
                        
                    case OrderStatus.Canceled:
                        _currentOrder = null;
                        break;
                }
            }
            else if (order == _stopLossOrder || order == _takeProfitOrder)
            {
                if (order.Status() == OrderStatus.Filled)
                {
                    // SL o TP ejecutado - cerrar posición
                    ClosePosition();
                }
                else if (order.Status() == OrderStatus.Canceled)
                {
                    // Limpiar referencia de orden cancelada/rechazada
                    if (order == _stopLossOrder) _stopLossOrder = null;
                    if (order == _takeProfitOrder) _takeProfitOrder = null;
                }
            }
        }
        
        protected override void OnOrderRegisterFailed(Order order, string message)
        {
            if (order == _currentOrder)
            {
                _currentOrder = null;
            }
            else if (order == _stopLossOrder)
            {
                _stopLossOrder = null;
            }
            else if (order == _takeProfitOrder)
            {
                _takeProfitOrder = null;
            }
            
            if (EnableLogging)
            {
                // Error al registrar orden
            }
        }
        
        private void ClosePosition()
        {
            _hasPosition = false;
            _entryPrice = 0m;
            
            // Cancelar órdenes pendientes
            try
            {
                if (_stopLossOrder != null && _stopLossOrder.Status() == OrderStatus.Placed)
                {
                    CancelOrder(_stopLossOrder);
                }
                if (_takeProfitOrder != null && _takeProfitOrder.Status() == OrderStatus.Placed)
                {
                    CancelOrder(_takeProfitOrder);
                }
            }
            catch (Exception)
            {
                if (EnableLogging)
                {
                    // Error al cancelar órdenes
                }
            }
            
            _stopLossOrder = null;
            _takeProfitOrder = null;
        }

        #endregion

        #region Cache Methods - IDENTICAL to Indicator
        
        private decimal GetPrevCached(int key, int barIndex)
        {
            if (!_cache.TryGetValue(key, out var list)) return 0m;
            if (barIndex < 0 || barIndex >= list.Count) return 0m;
            return list[barIndex];
        }
        
        private void SetCache(int key, int barIndex, decimal v)
        {
            if (!_cache.TryGetValue(key, out var list))
            {
                list = new List<decimal>();
                _cache[key] = list;
            }
            while (list.Count <= barIndex) list.Add(0m);
            list[barIndex] = v;
        }
        
        #endregion

        #region Helper Methods

        private decimal CalculateWilliamsR(int bar, int period)
        {
            if (bar < period - 1)
                return 0m;

            var highest = decimal.MinValue;
            var lowest = decimal.MaxValue;

            // Calcular el máximo y mínimo en el período
            for (int i = bar - period + 1; i <= bar; i++)
            {
                var candle = GetCandle(i);
                if (candle.High > highest) highest = candle.High;
                if (candle.Low < lowest) lowest = candle.Low;
            }

            var currentClose = GetCandle(bar).Close;
            var range = highest - lowest;
            
            if (range == 0) return 0m;
            
            return -100m * (highest - currentClose) / range;
        }

        #endregion
    }
}