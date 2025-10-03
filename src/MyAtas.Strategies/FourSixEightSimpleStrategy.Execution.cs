using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Shared;

namespace MyAtas.Strategies
{
    // Envío de órdenes y construcción de brackets (tal cual tu estado actual).
    public partial class FourSixEightSimpleStrategy
    {
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

            // *** PHANTOM FIX: Registrar signo para ENTRY ***
            RegisterChildSign(order.Comment, dir, isEntry: true);

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

            // *** PHANTOM FIX: Registrar signo para TP ***
            RegisterChildSign(order.Comment, _entryDir, isEntry: false);

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

            // *** PHANTOM FIX: Registrar signo para SL ***
            RegisterChildSign(order.Comment, _entryDir, isEntry: false);

            DebugLog.W("468/ORD", $"STOP submitted: {side} {qty} @{triggerPx:F2} OCO={(oco??"none")}");
        }

        // ====================== BRACKETS (ROBUST) ======================
        private void BuildAndSubmitBracket(int dir, int totalQty, int signalBar, int execBar)
        {
            if (totalQty <= 0) return;

            // TODO: External Risk Management Integration Point
            // When ExternalRiskControlsStops=true, this method could delegate
            // SL creation to external RM while keeping TP logic internal

            var (slPx, tpList) = BuildBracketPrices(dir, signalBar, execBar); // tpList respeta EnableTP1/2/3
            var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;

            int enabled = tpList.Count;
            if (enabled <= 0)
            {
                // Sin TPs activos → SL único por la qty completa
                SubmitStop(null, coverSide, totalQty, slPx);
                DebugLog.W("468/ORD", $"BRACKETS ATTACHED: tp=0 sl=1 total={totalQty} (SL-only @ {slPx:F2})");
                return;
            }

            // Reparto de cantidad entre TPs habilitados (p.ej., 3→[2,1] si enabled=2)
            var qtySplit = SplitQtyForTPs(totalQty, enabled);

            // FIX: Iterar solo qtySplit.Count veces, no enabled veces
            // Cuando totalQty < enabled, qtySplit tendrá menos elementos
            for (int i = 0; i < qtySplit.Count; i++)
            {
                int legQty = qtySplit[i];
                var oco = Guid.NewGuid().ToString("N");
                SubmitStop(oco, coverSide, legQty, slPx);
                SubmitLimit(oco, coverSide, legQty, tpList[i]);
            }

            DebugLog.W("468/ORD", $"BRACKETS ATTACHED: tp={qtySplit.Count} sl={qtySplit.Count} total={totalQty} (SL={slPx:F2} | TPs={string.Join(",", tpList.Take(qtySplit.Count).Select(x=>x.ToString("F2")))})");
            DebugLog.W("468/STR", $"BRACKETS: SL={slPx:F2} | TPs={string.Join(",", tpList.Take(qtySplit.Count).Select(x=>x.ToString("F2")))} | Split=[{string.Join(",", qtySplit)}] | Total={totalQty}");
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

            // FIX: Cuando totalQty < nTps, crear solo totalQty brackets
            // Ejemplo: totalQty=2, nTps=3 → crear solo 2 brackets [1,1]
            int actualBrackets = Math.Min(totalQty, nTps);
            int baseQ = totalQty / actualBrackets;
            int rem = totalQty % actualBrackets;

            for (int i = 0; i < actualBrackets; i++)
                q.Add(baseQ + (i < rem ? 1 : 0));

            return q;
        }

        // FIXED: Implement missing ElementAtOrDefault method
        private static T GetElementAtOrDefault<T>(List<T> list, int index, T defaultValue)
        {
            return (index >= 0 && index < list.Count) ? list[index] : defaultValue;
        }
    }
}