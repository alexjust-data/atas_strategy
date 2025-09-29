using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Shared;

namespace MyAtas.Strategies
{
    // BreakEven por TP touch/fill (simple, qty total viva), con micro-throttle.
    public partial class FourSixEightSimpleStrategy
    {
        // --- Estado BE ---
        private bool _breakevenApplied = false;
        private decimal _beLastTpPrice = 0m; // TP "congelado" mientras BE esté activo
        private decimal _entryPrice = 0m;
        private int _beLastTouchBar = -1;
        private DateTime _beLastTouchAt = DateTime.MinValue;
        private const int _beThrottleMs = 250; // micro-throttle

        // --- Hook: disparo por FILL de TP (si el modo lo permite) ---
        private void CheckBreakEvenTrigger_OnOrderChanged(Order order, OrderStatus status)
        {
            try
            {
                if (_breakevenApplied) { DebugLog.W("468/BRK", "SKIP BE(FILL): already applied"); return; }
                if (BreakevenMode != BreakevenMode.OnTPFill) { DebugLog.W("468/BRK", $"SKIP BE(FILL): mode={BreakevenMode}"); return; }
                if (!(TriggerOnTP1TouchFill || TriggerOnTP2TouchFill || TriggerOnTP3TouchFill)) { DebugLog.W("468/BRK", "SKIP BE(FILL): all TP touch/fill toggles are OFF"); return; }
                var c = order?.Comment ?? string.Empty;
                if (!c.StartsWith("468TP:")) { DebugLog.W("468/BRK", $"SKIP BE(FILL): not a TP comment [{c}]"); return; }
                if (status == OrderStatus.Filled || status == OrderStatus.PartlyFilled)
                {
                    DebugLog.W("468/BRK", $"BE by FILL: TP status={status}");
                    if (!ActivateBreakEven("TP fill"))
                        DebugLog.W("468/BRK", "BE(FILL) aborted (see previous logs)");
                }
                else
                {
                    DebugLog.W("468/BRK", $"SKIP BE(FILL): status={status}");
                }
            }
            catch { /* best-effort */ }
        }

        // --- Hook: disparo por TOUCH del TP en la barra actual (1x/bar + throttle tiempo) ---
        private void CheckBreakEvenTouch_OnCalculate(int bar)
        {
            try
            {
                if (_breakevenApplied) { DebugLog.W("468/BRK", "SKIP BE(TOUCH): already applied"); return; }
                if (!(TriggerOnTP1TouchFill || TriggerOnTP2TouchFill || TriggerOnTP3TouchFill)) { DebugLog.W("468/BRK", "SKIP BE(TOUCH): all TP touch/fill toggles are OFF"); return; }
                if (!_tradeActive || !_bracketsPlaced || _entryDir == 0) { DebugLog.W("468/BRK", $"SKIP BE(TOUCH): tradeActive={_tradeActive} bracketsPlaced={_bracketsPlaced} dir={_entryDir}"); return; }
                if (bar == _beLastTouchBar) { DebugLog.W("468/BRK", $"SKIP BE(TOUCH): already handled this bar={bar}"); return; }
                if (_beLastTouchAt != DateTime.MinValue && (DateTime.UtcNow - _beLastTouchAt).TotalMilliseconds < _beThrottleMs) { DebugLog.W("468/BRK", "SKIP BE(TOUCH): throttled"); return; }

                var tps = GetActiveTPsSortedByIndex(_entryDir); // index lógico: 0→TP1, 1→TP2, 2→TP3
                DebugLog.W("468/BRK", $"BE(TOUCH) scan: activeTPs={tps.Count} dir={_entryDir}");
                if (tps.Count == 0) { DebugLog.W("468/BRK", "SKIP BE(TOUCH): no active TPs yet"); return; }

                var cndl = GetCandle(bar);
                for (int i = 0; i < tps.Count; i++)
                {
                    // Respeta checkboxes por TP
                    if (i == 0 && !TriggerOnTP1TouchFill) continue;
                    if (i == 1 && !TriggerOnTP2TouchFill) continue;
                    if (i == 2 && !TriggerOnTP3TouchFill) continue;

                    var px = SafeGetPrice(tps[i]);
                    if (px <= 0m) continue;

                    bool touched = _entryDir > 0 ? (cndl.High >= px) : (cndl.Low <= px);
                    DebugLog.W("468/BRK", $"TOUCH TEST: TP{i+1} px={px:F2} vs barHL=[{cndl.Low:F2},{cndl.High:F2}] touched={touched}");
                    if (!touched) continue;

                    DebugLog.W("468/BRK", $"TP TOUCH DETECTED: idx={(i+1)} price={px:F2} bar={bar}");
                    if (ActivateBreakEven($"TP{i+1} touch"))
                    {
                        _beLastTouchBar = bar;
                        _beLastTouchAt = DateTime.UtcNow;
                    }
                    break; // una sola activación por barra
                }
            }
            catch { /* best-effort */ }
        }

        // --- Aplicación del BE: reconstruye los OCO(s) de los TP(s) supervivientes ---
        private bool ActivateBreakEven(string reason)
        {
            try
            {
                int net = Math.Abs(GetNetPosition());
                int netByFills = Math.Abs(NetByFills()); // fallback detection
                DebugLog.W("468/BRK", $"BE ACTIVATE START: reason='{reason}' net={net} netByFills={netByFills} dir={_entryDir} entryPx={_entryPrice:F2}");

                if (net <= 0)
                {
                    if (netByFills > 0)
                    {
                        DebugLog.W("468/BRK", $"BE FALLBACK: net=0 but netByFills={netByFills} -> using fills");
                        net = netByFills;
                    }
                    else
                    {
                        DebugLog.W("468/BRK", "BE ABORT: no position detected (both net=0 and netByFills=0)");
                        return false;
                    }
                }

                if (_entryDir == 0)
                {
                    DebugLog.W("468/BRK", "BE ABORT: _entryDir=0");
                    return false;
                }

                decimal bePx = CalculateBreakEvenPrice(_entryDir, _entryPrice, BreakevenOffsetTicks);
                DebugLog.W("468/BRK", $"BE PRICE CALC: entryPx={_entryPrice:F2} offset={BreakevenOffsetTicks}ticks -> bePx={bePx:F2}");

                if (bePx <= 0m)
                {
                    DebugLog.W("468/BRK", "BE ABORT: calculated BE price <= 0");
                    return false;
                }

                var tps = GetActiveTPs();                    // TP(s) supervivientes
                var sls = GetActiveSLs();                    // SL(s) actuales
                DebugLog.W("468/BRK", $"BE ORDERS SCAN: activeTPs={tps.Count} activeSLs={sls.Count}");

                // Si hay TP(s) vivos, RECONSTRUYE cada OCO: cancela TP+SL del grupo y vuelve a crear TP (mismo precio) y SL (en BE)
                if (tps.Count > 0)
                {
                    var coverSide = _entryDir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                    // Tomamos snapshot por OCO para no perder info al cancelar
                    var groups = tps
                        .Select(tp => new {
                            Oco = GetOco(tp),
                            Qty = (int)Math.Max(1, Math.Abs(tp.QuantityToFill)),
                            TpPx = SafeGetPrice(tp)
                        })
                        .Where(g => !string.IsNullOrEmpty(g.Oco) && g.Qty > 0 && g.TpPx > 0m)
                        .ToList();

                    DebugLog.W("468/BRK", $"BE GROUPS: {groups.Count} TP group(s) to rebuild at BE");

                    foreach (var g in groups)
                    {
                        // 1) Cancelar TODAS las órdenes activas del OCO (TP y SL). OCO puede cancelar la pareja por nosotros; está bien.
                        try
                        {
                            var grpOrders = _liveOrders
                                .Where(o => o != null
                                            && o.State == OrderStates.Active
                                            && o.Status() != OrderStatus.Filled
                                            && o.Status() != OrderStatus.Canceled
                                            && string.Equals(GetOco(o), g.Oco, StringComparison.Ordinal))
                                .ToList();
                            foreach (var o in grpOrders)
                            {
                                DebugLog.W("468/BRK", $"BE CANCEL OCO[{g.Oco[..Math.Min(6,g.Oco.Length)]}]: {o.Comment} px={SafeGetPrice(o):F2}");
                                CancelOrder(o);
                            }
                        }
                        catch (Exception ex) { DebugLog.W("468/BRK", $"BE CANCEL GROUP EX: {ex.Message}"); }

                        // 2) Re-crear OCO nuevo con SL en BE y TP en su precio original (misma qty)
                        var newOco = Guid.NewGuid().ToString("N");
                        DebugLog.W("468/BRK", $"BE REBUILD OCO: newOco={newOco[..6]} qty={g.Qty} tp@{g.TpPx:F2} slBE@{bePx:F2}");
                        SubmitStop(newOco, coverSide, g.Qty, bePx);
                        SubmitLimit(newOco, coverSide, g.Qty, g.TpPx);
                        _beLastTpPrice = g.TpPx; // memoriza el TP que debe sobrevivir durante BE
                    }

                    _breakevenApplied = true;
                    DebugLog.W("468/BRK", $"BREAKEVEN MOVE (REBUILD): newSL={bePx:F2} dir={_entryDir} net={net} reason={reason} groups={groups.Count}");
                    return true;
                }
                else
                {
                    // Fallback ultra defensivo: no hay TP vivo → crea un SL único en BE por la qty neta (sin tocar nada más)
                    var coverSide = _entryDir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                    var newOco = Guid.NewGuid().ToString("N");
                    DebugLog.W("468/BRK", $"BE FALLBACK (no TP): STOP qty={net} @{bePx:F2} oco={newOco[..6]}");
                    SubmitStop(newOco, coverSide, net, bePx);
                    _breakevenApplied = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("468/BRK", "ActivateBreakEven EX: " + ex.Message);
                return false;
            }
        }

        private decimal CalculateBreakEvenPrice(int dir, decimal entryPrice, int offsetTicks)
        {
            try
            {
                decimal px = entryPrice;
                if (px <= 0m)
                    px = GetCandle(_lastSignalBar + 1 <= CurrentBar ? _lastSignalBar + 1 : _lastSignalBar).Open; // fallback
                var off = Ticks(Math.Max(0, offsetTicks));
                px = dir > 0 ? px + off : px - off;
                return RoundToTick(px);
            }
            catch { return 0m; }
        }

        // Captura del precio de entrada desde la orden (al primer fill/part-fill)
        private void UpdateEntryPriceFromOrder(Order order)
        {
            try
            {
                foreach (var name in new[] { "AvgPrice", "AveragePrice", "AvgFillPrice", "Price" })
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Convert.ToDecimal(p.GetValue(order));
                    if (v > 0m) { _entryPrice = v; break; }
                }
                if (_entryPrice <= 0m)
                    _entryPrice = GetCandle(_lastSignalBar + 1 <= CurrentBar ? _lastSignalBar + 1 : _lastSignalBar).Open;
                DebugLog.W("468/BRK", $"ENTRY PRICE CAPTURED: {_entryPrice:F2}");
            }
            catch { /* ignore */ }
        }

        // ===== Helpers de órdenes vivas =====
        private List<Order> GetActiveTPs()
            => _liveOrders.Where(o => o != null
                                   && (o.Comment?.StartsWith("468TP:") ?? false)
                                   && o.State == OrderStates.Active
                                   && o.Status() != OrderStatus.Filled
                                   && o.Status() != OrderStatus.Canceled).ToList();

        private List<Order> GetActiveSLs()
            => _liveOrders.Where(o => o != null
                                   && (o.Comment?.StartsWith("468SL:") ?? false)
                                   && o.State == OrderStates.Active
                                   && o.Status() != OrderStatus.Filled
                                   && o.Status() != OrderStatus.Canceled).ToList();

        private List<Order> GetActiveTPsSortedByIndex(int dir)
        {
            var tps = GetActiveTPs();
            // Para largos: TP1 < TP2 < TP3; para cortos: TP1 > TP2 > TP3
            return (dir > 0)
                ? tps.OrderBy(tp => SafeGetPrice(tp)).ToList()
                : tps.OrderByDescending(tp => SafeGetPrice(tp)).ToList();
        }

        private static decimal SafeGetPrice(Order o)
        {
            try
            {
                var p = o?.GetType().GetProperty("Price")?.GetValue(o);
                return p != null ? Convert.ToDecimal(p) : 0m;
            }
            catch { return 0m; }
        }

        private static string ExtractAnyOco(List<Order> tps, List<Order> sls)
        {
            try
            {
                var o = tps.FirstOrDefault() ?? sls.FirstOrDefault();
                if (o == null) return null;
                var prop = o.GetType().GetProperty("OCOGroup");
                return prop != null ? (string)prop.GetValue(o) : null;
            }
            catch { return null; }
        }

        // Helpers OCO
        private static string GetOco(Order o)
        {
            try { return (string)o?.GetType().GetProperty("OCOGroup")?.GetValue(o); }
            catch { return null; }
        }
    }
}