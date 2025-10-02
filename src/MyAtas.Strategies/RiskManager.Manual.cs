using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using ATAS.Strategies.Chart;
using ATAS.Types;
using ATAS.DataFeedsCore;
using MyAtas.Shared;
using MyAtas.Risk.Engine;
using MyAtas.Risk.Models;

namespace MyAtas.Strategies
{
    // Enums externos para serializaciÃƒÂ³n correcta de ATAS
    public enum RmSizingMode { Manual, FixedRiskUSD, PercentAccount }
    // Mantiene compat. binaria pero aclara intención:
    public enum RmBeMode { Off, OnTPTouch, OnTPFill }
    public enum RmTrailMode { Off, BarByBar, TpToTp }
    public enum RmStopPlacement { ByTicks, PrevBarOppositeExtreme }  // modo de colocaciÃƒÂ³n del SL
    public enum RmPrevBarOffsetSide { Outside, Inside }              // NEW: lado del offset (fuera/dentro)

    // --- Helpers de normalizacion de TPs/splits ---
    internal static class _RmSplitHelper
    {
        // Devuelve arrays listos para el motor: solo TPs con split>0 y suma=100 (el ultimo absorbe la diferencia)
        public static (decimal[] r, int[] s) BuildTpArrays(int preset, decimal tp1, decimal tp2, decimal tp3, int sp1, int sp2, int sp3)
        {
            var rList = new System.Collections.Generic.List<decimal>();
            var sList = new System.Collections.Generic.List<int>();
            var n = Math.Clamp(preset, 1, 3);
            if (n >= 1 && sp1 > 0) { rList.Add(tp1); sList.Add(Math.Max(0, sp1)); }
            if (n >= 2 && sp2 > 0) { rList.Add(tp2); sList.Add(Math.Max(0, sp2)); }
            if (n >= 3 && sp3 > 0) { rList.Add(tp3); sList.Add(Math.Max(0, sp3)); }
            if (rList.Count == 0)
            {
                // Fallback: un solo TP al 100% a 1R
                rList.Add(Math.Max(1m, tp1));
                sList.Add(100);
                return (rList.ToArray(), sList.ToArray());
            }
            var sum = sList.Sum();
            if (sum != 100)
            {
                // Re-normaliza a 100 y el ultimo absorbe la diferencia
                for (int i = 0; i < sList.Count; i++)
                    sList[i] = (int)Math.Max(0, Math.Round(100m * sList[i] / Math.Max(1, sum)));
                var diff = 100 - sList.Sum();
                sList[^1] = Math.Max(1, sList[^1] + diff);
            }
            return (rList.ToArray(), sList.ToArray());
        }

        // Reparte 'total' según 'splits' (% normalizados a 100). Último absorbe diferencia.
        public static int[] SplitQty(int total, int[] splits)
        {
            if (total <= 0 || splits == null || splits.Length == 0) return Array.Empty<int>();
            var q = new int[splits.Length];
            var remain = total;
            for (int i = 0; i < splits.Length; i++)
            {
                if (i == splits.Length - 1)
                {
                    q[i] = Math.Max(0, remain);
                }
                else
                {
                    var qi = (int)Math.Round(total * (splits[i] / 100m));
                    q[i] = Math.Max(0, qi);
                    remain -= q[i];
                }
            }
            // Ajuste anti-todo-en-cero: si sumó 0 pero total>0, pon 1 al último
            if (q.Sum() == 0 && total > 0) q[^1] = total;
            // Ajuste si por redondeo sobrepasó:
            var diff = q.Sum() - total;
            if (diff > 0) q[^1] = Math.Max(0, q[^1] - diff);
            return q;
        }
    }

    // Nota: esqueleto "safe". No envÃ¯Â¿Â½a ni cancela Ã¯Â¿Â½rdenes.
    public class RiskManagerManualStrategy : ChartStrategy
    {
        // =================== Activation ===================
        [Category("Activation"), DisplayName("Manage manual entries")]
        public bool ManageManualEntries { get; set; } = true;

        [Category("Activation"), DisplayName("Allow attach without net (fallback)")]
        public bool AllowAttachFallback { get; set; } = true;

        [Category("Activation"), DisplayName("Ignore orders with prefix")]
        public string IgnorePrefix { get; set; } = "468"; // no interferir con la 468

        [Category("Activation"), DisplayName("Owner prefix (this strategy)")]
        public string OwnerPrefix { get; set; } = "RM:";

        [Category("Activation"), DisplayName("Enforce manual qty on entry")]
        [Description("Si la entrada manual ejecuta menos contratos que el objetivo calculado por el RM, la estrategia enviarÃƒÂ¡ una orden a mercado por la diferencia (delta).")]
        public bool EnforceManualQty { get; set; } = true;

        [Category("Position Sizing"), DisplayName("Mode")]
        public RmSizingMode SizingMode { get; set; } = RmSizingMode.Manual;

        [Category("Position Sizing"), DisplayName("Manual qty")]
        [Description("Cantidad objetivo de la ESTRATEGIA. Si difiere de la qty del ChartTrader y 'Enforce manual qty' estÃƒÂ¡ activo, el RM ajustarÃƒÂ¡ con orden a mercado.")]
        public int ManualQty { get; set; } = 1;

        [Category("Position Sizing"), DisplayName("Risk per trade (USD)")]
        public decimal RiskPerTradeUsd { get; set; } = 100m;

        [Category("Position Sizing"), DisplayName("Risk % of account")]
        public decimal RiskPercentOfAccount { get; set; } = 0.5m;

        [Category("Position Sizing"), DisplayName("Default stop (ticks)")]
        public int DefaultStopTicks { get; set; } = 12;

        [Category("Position Sizing"), DisplayName("Fallback tick size")]
        public decimal FallbackTickSize { get; set; } = 0.25m;

        [Category("Position Sizing"), DisplayName("Fallback tick value (USD)")]
        public decimal FallbackTickValueUsd { get; set; } = 12.5m;

        [Category("Position Sizing"), DisplayName("Min qty")]
        public int MinQty { get; set; } = 1;

        [Category("Position Sizing"), DisplayName("Max qty")]
        public int MaxQty { get; set; } = 1000;

        [Category("Position Sizing"), DisplayName("Underfunded policy")]
        public MyAtas.Risk.Models.UnderfundedPolicy Underfunded { get; set; } =
            MyAtas.Risk.Models.UnderfundedPolicy.Min1;

        // === Snapshot de cuenta (solo lectura en UI) ===
        [Category("Position Sizing"), DisplayName("Account equity (USD)")]
        [System.ComponentModel.ReadOnly(true)]
        public decimal AccountEquitySnapshot { get; private set; } = 0m;

        private DateTime _nextEquityProbeAt = DateTime.MinValue;

        [Category("Position Sizing"), DisplayName("Account equity override (USD)")]
        public decimal AccountEquityOverride { get; set; } = 0m;

        // === Session P&L Tracking ===
        [Category("Position Sizing"), DisplayName("Session P&L (USD)")]
        [System.ComponentModel.ReadOnly(true)]
        [Description("Ganancia/pérdida acumulada desde que se activó la estrategia. Se resetea al desactivar.")]
        public decimal SessionPnL { get; private set; } = 0m;

        [Category("Position Sizing"), DisplayName("Tick value overrides (SYM=V;...)")]
        public string TickValueOverrides { get; set; } = "MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10";

        // =================== Stops & TPs ===================
        [Category("Stops & TPs"), DisplayName("Preset TPs (1..3)")]
        public int PresetTPs { get; set; } = 2; // 1..3

        [Category("Stops & TPs"), DisplayName("TP1 R multiple")]
        public decimal TP1R { get; set; } = 1.0m;

        [Category("Stops & TPs"), DisplayName("TP2 R multiple")]
        public decimal TP2R { get; set; } = 2.0m;

        [Category("Stops & TPs"), DisplayName("TP3 R multiple")]
        public decimal TP3R { get; set; } = 3.0m;

        [Category("Stops & TPs"), DisplayName("TP1 split (%)")]
        public int TP1pctunit { get; set; } = 50;

        [Category("Stops & TPs"), DisplayName("TP2 split (%)")]
        public int TP2pctunit { get; set; } = 50;

        [Category("Stops & TPs"), DisplayName("TP3 split (%)")]
        public int TP3pctunit { get; set; } = 0;

        // === Stop placement ===
        [Category("Stops & TPs"), DisplayName("Stop placement mode")]
        [Description("ByTicks: usa 'Default stop (ticks)'. PrevBarOppositeExtreme: coloca el SL en el extremo opuesto de la vela N-1 (+offset).")]
        public RmStopPlacement StopPlacementMode { get; set; } = RmStopPlacement.ByTicks;

        [Category("Stops & TPs"), DisplayName("Prev-bar offset (ticks)")]
        [Description("Holgura aÃƒÂ±adida al extremo de la vela N-1 (1 = un tick mÃƒÂ¡s allÃƒÂ¡ del High/Low).")]
        public int PrevBarOffsetTicks { get; set; } = 1;

        [Category("Stops & TPs"), DisplayName("Prev-bar offset side")]
        [Description("Outside: fuera del extremo (mÃƒÂ¡s allÃƒÂ¡ del High/Low). Inside: dentro del rango de la vela.")]
        public RmPrevBarOffsetSide PrevBarOffsetSide { get; set; } = RmPrevBarOffsetSide.Outside;

        // =================== Breakeven ===================
        [Category("Breakeven"), DisplayName("Mode")]
        public RmBeMode BreakEvenMode { get; set; } = RmBeMode.OnTPTouch;

        [Category("Breakeven"), DisplayName("BE trigger TP (1..3)")]
        [Description("Qué TP dispara el paso a breakeven (1, 2 o 3). Funciona también en modo virtual sin TP real.")]
        public int BeTriggerTp { get; set; } = 1;

        [Category("Breakeven"), DisplayName("BE offset (ticks)")]
        public int BeOffsetTicks { get; set; } = 4;

        [Category("Breakeven"), DisplayName("Virtual BE")]
        public bool VirtualBreakEven { get; set; } = false;

        // =================== Trailing (placeholder) ===================
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

        // === Breakeven: estado mínimo requerido por ResetAttachState / gates ===
        private bool   _beArmed     = false;
        private bool   _beDone      = false;
        private decimal _beTargetPx = 0m;

        // === Breakeven helpers ===
        private int _beDirHint = 0; // +1/-1 al armar (por si net no está disponible aún)

        // Tracking de extremos desde el momento de armar BE (evita usar datos históricos de barra)
        private decimal _beArmedAtPrice = 0m;  // Precio cuando se armó BE (baseline)
        private decimal _beMaxReached = 0m;    // Máximo alcanzado DESPUÉS de armar BE
        private decimal _beMinReached = 0m;    // Mínimo alcanzado DESPUÉS de armar BE

        // === Session P&L tracking ===
        private decimal _sessionStartingEquity = 0m;  // Equity inicial al activar estrategia
        private decimal _currentPositionEntryPrice = 0m;  // Precio promedio de entrada actual
        private int _currentPositionQty = 0;  // Cantidad de posición actual (signed: +LONG / -SHORT)
        private decimal _sessionRealizedPnL = 0m;  // P&L realizado acumulado desde activación

        private decimal ComputeBePrice(int dir, decimal entryPx, decimal tickSize)
        {
            var off = Math.Max(0, BeOffsetTicks) * tickSize;
            return dir > 0 ? ShrinkPrice(entryPx + off) : ShrinkPrice(entryPx - off);
        }

        // Mueve todos los SL a 'newStopPx' preservando TPs:
        // 1) intenta MODIFICAR in-place; 2) si no se puede, cancela SL+TP y recrea ambos.
        private void MoveAllRmStopsTo(decimal newStopPx, string reason = "BE")
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;

                // Agrupar TPs y SLs por OCO
                var tpsByOco = new System.Collections.Generic.Dictionary<string, Order>();
                var slsByOco = new System.Collections.Generic.Dictionary<string, Order>();

                foreach (var o in list)
                {
                    if (o == null) continue;
                    var c = o.Comment ?? "";
                    var st = o.Status();
                    if (o.Canceled || st == OrderStatus.Canceled || st == OrderStatus.Filled) continue;

                    if (c.StartsWith(OwnerPrefix + "TP:"))
                        tpsByOco[o.OCOGroup ?? ""] = o;
                    else if (c.StartsWith(OwnerPrefix + "SL:"))
                        slsByOco[o.OCOGroup ?? ""] = o;
                }

                int replaced = 0, recreated = 0, modified = 0;
                foreach (var kv in tpsByOco)
                {
                    var oco = kv.Key;
                    if (!slsByOco.TryGetValue(oco, out var slOld)) continue;

                    var qty = Math.Max(0, slOld.QuantityToFill);
                    if (qty <= 0) continue;

                    // 1) MODIFICAR in-place si la API lo permite
                    if (TryModifyStopInPlace(slOld, newStopPx)) { modified++; continue; }

                    // 2) Fallback: cancelar SL+TP y recrear pareja completa
                    var tpOld = kv.Value;
                    var tpPx  = tpOld?.Price ?? 0m;
                    var tpQty = Math.Max(0, tpOld?.QuantityToFill ?? 0);
                    try { CancelOrder(slOld); } catch { }
                    try { if (tpOld != null) CancelOrder(tpOld); } catch { }

                    var side = slOld.Direction; // ya es la cara "cover" correcta
                    var slNew = new Order
                    {
                        Portfolio      = Portfolio,
                        Security       = Security,
                        Direction      = side,
                        Type           = OrderTypes.Stop,
                        TriggerPrice   = newStopPx,
                        QuantityToFill = qty,
                        OCOGroup       = oco,
                        IsAttached     = true,
                        Comment        = $"{OwnerPrefix}SL:{Guid.NewGuid():N}"
                    };
                    TrySetReduceOnly(slNew);
                    TrySetCloseOnTrigger(slNew);
                    OpenOrder(slNew);
                    replaced++;

                    if (tpQty > 0 && tpPx > 0m)
                    {
                        var tpNew = new Order
                        {
                            Portfolio      = Portfolio,
                            Security       = Security,
                            Direction      = side,
                            Type           = OrderTypes.Limit,
                            Price          = tpPx,
                            QuantityToFill = tpQty,
                            OCOGroup       = oco,
                            IsAttached     = true,
                            Comment        = $"{OwnerPrefix}TP:{Guid.NewGuid():N}"
                        };
                        TrySetReduceOnly(tpNew);
                        OpenOrder(tpNew);
                        recreated++;
                    }
                }

                if (EnableLogging)
                    DebugLog.W("RM/BE", $"Moved SLs to {newStopPx:F2} (modified={modified} replaced={replaced} tpRecreated={recreated}) reason={reason}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/BE", $"MoveAllRmStopsTo EX: {ex.Message}");
            }
        }

        // Intenta modificar un STOP existente sin tocar su OCO (evita matar el TP).
        private bool TryModifyStopInPlace(Order stopOrder, decimal newStopPx)
        {
            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"TryModifyStopInPlace ENTER: order={stopOrder?.Comment} oldTrigger={stopOrder?.TriggerPrice:F2} newTrigger={newStopPx:F2}");
            try
            {
                var tm = this.TradingManager;
                if (tm == null)
                {
                    if (EnableLogging) DebugLog.W("RM/BE/MOD", "TradingManager is NULL → return false");
                    return false;
                }

                var tmt = tm.GetType();
                if (EnableLogging) DebugLog.W("RM/BE/MOD", $"TradingManager type: {tmt.Name}");

                // Escanear todos los métodos disponibles
                var allMethods = tmt.GetMethods();
                if (EnableLogging) DebugLog.W("RM/BE/MOD", $"TradingManager has {allMethods.Length} total methods");

                foreach (var name in new[] { "ModifyOrder", "ChangeOrder", "UpdateOrder" })
                {
                    if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Searching for method: {name}");
                    var matches = allMethods.Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Found {matches.Count} methods matching '{name}'");

                    foreach (var mi in matches)
                    {
                        var ps = mi.GetParameters();
                        if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Method {mi.Name} has {ps.Length} parameters: {string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"))}");

                        // Firma simple: (Order, decimal triggerPrice)
                        if (ps.Length == 2 && ps[0].ParameterType.IsAssignableFrom(stopOrder.GetType()) && ps[1].ParameterType == typeof(decimal))
                        {
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"MATCH: Simple signature (Order, decimal) → invoking {mi.Name}");
                            var result = mi.Invoke(tm, new object[] { stopOrder, newStopPx });
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Invocation result: {result} → return true");
                            return true;
                        }

                        // Firma multi-parámetro: (Order oldOrder, Order newOrder, bool, bool)
                        if (ps.Length >= 2 && ps.Length <= 6
                            && ps[0].ParameterType.IsAssignableFrom(stopOrder.GetType())
                            && ps[1].ParameterType.IsAssignableFrom(stopOrder.GetType()))
                        {
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"MATCH: (Order, Order, ...) signature → creating modified order");

                            // Crear orden modificada clonando la original
                            var modifiedOrder = new Order
                            {
                                Portfolio      = stopOrder.Portfolio,
                                Security       = stopOrder.Security,
                                Direction      = stopOrder.Direction,
                                Type           = stopOrder.Type,
                                TriggerPrice   = newStopPx,  // ← AQUÍ EL NUEVO TRIGGER
                                Price          = stopOrder.Price,
                                QuantityToFill = stopOrder.QuantityToFill,
                                OCOGroup       = stopOrder.OCOGroup,
                                IsAttached     = stopOrder.IsAttached,
                                Comment        = stopOrder.Comment
                            };

                            // Intentar copiar ReduceOnly si está disponible
                            TrySetReduceOnly(modifiedOrder);
                            TrySetCloseOnTrigger(modifiedOrder);

                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Created modified order: trigger {stopOrder.TriggerPrice:F2} → {newStopPx:F2}");

                            var args = new object[ps.Length];
                            args[0] = stopOrder;       // oldOrder
                            args[1] = modifiedOrder;   // newOrder con nuevo TriggerPrice
                            for (int i = 2; i < ps.Length; i++)
                            {
                                args[i] = ps[i].ParameterType == typeof(bool) ? true
                                       : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                            }

                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Args: oldOrder={stopOrder.Comment} newTrigger={newStopPx:F2} + {ps.Length-2} bools");
                            var result = mi.Invoke(tm, args);
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Invocation result: {result} → return true");
                            return true;
                        }
                    }
                }

                if (EnableLogging) DebugLog.W("RM/BE/MOD", "NO modify methods found → return false (will use cancel+recreate)");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/BE/MOD", $"EXCEPTION: {ex.GetType().Name} - {ex.Message}");
                if (EnableLogging && ex.InnerException != null) DebugLog.W("RM/BE/MOD", $"INNER: {ex.InnerException.Message}");
            }
            return false;
        }

        private RiskEngine _engine;

        // =================== Stop-to-Flat (RM Close) ===================
        // Cuando el usuario pulsa el botÃƒÂ³n rojo de ATAS (Stop Strategy),
        // queremos: cancelar brackets propios y hacer FLATTEN de la posiciÃƒÂ³n.
        private bool _stopToFlat = false;
        private DateTime _rmStopGraceUntil = DateTime.MinValue;     // mientras now<=esto, estamos drenando cancel/fill
        private const int _rmStopGraceMs = 2200;                    // holgura post-cancel/flatten
        private DateTime _nextStopSweepAt = DateTime.MinValue;
        private const int _stopSweepEveryMs = 250;                  // sweep periÃƒÂ³dico durante el stop

        // ==== Diagnostics / Build stamp ====
        private const string BuildStamp = "RM.Manual/stop-to-flat 2025-09-30T22:00Z";

        // ==== Post-Close grace & timeouts ====
        private DateTime _postCloseUntil = DateTime.MinValue; // if now <= this Ã¢â€ â€™ inGrace
        private readonly int _postCloseGraceMs = 2200;        // un poco mÃƒÂ¡s de holgura tras Close
        private readonly int _cleanupWaitMs = 300;            // wait before aggressive cleanup (ms)
        private readonly int _maxRetryMs = 2000;              // absolute escape from WAIT (ms)

        // ==== State snapshots for external-close detection ====
        private bool _hadRmBracketsPrevTick = false;          // were there RM brackets last tick?
        private int  _prevNet = 0;                            // last net position snapshot
        private DateTime _lastExternalCloseAt = DateTime.MinValue;
        private const int ExternalCloseDebounceMs = 1500;

        // ==== Attach protection ====
        private DateTime _lastAttachArmAt = DateTime.MinValue;
        private const int AttachProtectMs = 1200;             // proteger el attach mÃƒÂ¡s tiempo

        // --- Estado de net para detectar 0Ã¢â€ â€™Ã¢â€° 0 (entrada) ---
        private bool _pendingAttach = false;
        private decimal _pendingEntryPrice = 0m;
        private DateTime _pendingSince = DateTime.MinValue;
        private int _pendingDirHint = 0;                 // +1/-1 si logramos leerlo del Order
        private int _pendingFillQty = 0;                 // qty del fill manual (si la API lo expone)
        private readonly int _attachThrottleMs = 200; // consolidaciÃƒÂ³n mÃƒÂ­nima
        private readonly int _attachDeadlineMs = 120; // fallback rÃƒÂ¡pido si el net no llega
        private readonly System.Collections.Generic.List<Order> _liveOrders = new();
        private readonly object _liveOrdersLock = new();
        // Ancla de contexto para SL por estructura: ÃƒÂ­ndice de N-1 "en el momento del fill"
        private int _pendingPrevBarIdxAtFill = -1;

        // Helper property for compatibility
        private bool IsActivated => ManageManualEntries;

        private void ResetAttachState(string reason = "")
        {
            _pendingAttach = false;
            _pendingEntryPrice = 0m;
            _pendingPrevBarIdxAtFill = -1;
            _pendingFillQty = 0;
            _beArmed = false; _beDone = false; _beTargetPx = 0m;
            _beArmedAtPrice = _beMaxReached = _beMinReached = 0m;  // Limpiar tracking BE
            if (EnableLogging) DebugLog.W("RM/GATE", $"ResetAttachState: {reason}");
        }

        // Constructor explÃƒÂ­cito para evitar excepciones durante carga ATAS
        public RiskManagerManualStrategy()
        {
            try
            {
                // No inicializar aquÃƒÂ­ para evitar problemas de carga
                _engine = null;
            }
            catch
            {
                // Constructor sin excepciones para ATAS
            }
        }

        private RiskEngine GetEngine()
        {
            if (_engine == null)
            {
                try
                {
                    _engine = new RiskEngine();
                }
                catch
                {
                    // Fallback seguro
                    _engine = null;
                }
            }
            return _engine;
        }

        private bool IsFirstTickOf(int currentBar)
        {
            if (currentBar != _lastSeenBar) { _lastSeenBar = currentBar; return true; }
            return false;
        }

        // Lee net de forma robusta priorizando TradingManager.Position (net de CUENTA)
        private int ReadNetPosition()
        {
            var snap = ReadPositionSnapshot();
            return snap.NetQty;
        }

        private (int NetQty, decimal AvgPrice) ReadPositionSnapshot()
        {
            try
            {
                if (Portfolio == null || Security == null)
                    return (0, 0m);

                // 0) PRIMERO: TradingManager.Position (CUENTA seleccionada)
                try
                {
                    var tm = this.TradingManager;
                    var tmPos = tm?.GetType().GetProperty("Position")?.GetValue(tm);
                    if (tmPos != null)
                    {
                        int netQty = 0; decimal avgPrice = 0m;
                        foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                        {
                            var p = tmPos.GetType().GetProperty(name);
                            if (p != null)
                            {
                                var v = p.GetValue(tmPos);
                                if (v != null) { netQty = Convert.ToInt32(v); break; }
                            }
                        }
                        foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                        {
                            var p = tmPos.GetType().GetProperty(name);
                            if (p != null)
                            {
                                var v = p.GetValue(tmPos);
                                if (v != null) { avgPrice = Convert.ToDecimal(v); if (avgPrice > 0m) break; }
                            }
                        }
                        if (EnableLogging)
                            DebugLog.W("RM/SNAP", $"TM.Position net={netQty} avg={avgPrice:F2}");
                        return (netQty, avgPrice);
                    }
                } catch { /* fallback */ }

                // 1) Portfolio.GetPosition(Security)
                try
                {
                    var getPos = Portfolio.GetType().GetMethod("GetPosition", new[] { Security.GetType() });
                    if (getPos != null)
                    {
                        var pos = getPos.Invoke(Portfolio, new object[] { Security });
                        if (pos != null)
                        {
                            var netQty = 0;
                            var avgPrice = 0m;

                            // Leer Net/Amount/Qty/Position
                            foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                            {
                                var p = pos.GetType().GetProperty(name);
                                if (p != null)
                                {
                                    var v = p.GetValue(pos);
                                    if (v != null)
                                    {
                                        netQty = Convert.ToInt32(v);
                                        break;
                                    }
                                }
                            }

                            // Leer AvgPrice/AveragePrice/EntryPrice/Price
                            foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                            {
                                var p = pos.GetType().GetProperty(name);
                                if (p != null)
                                {
                                    var v = p.GetValue(pos);
                                    if (v != null)
                                    {
                                        avgPrice = Convert.ToDecimal(v);
                                        if (avgPrice > 0m) break;
                                    }
                                }
                            }

                            if (EnableLogging && (netQty != 0 || avgPrice > 0m))
                                DebugLog.W("RM/SNAP", $"pos avgPrice={avgPrice:F2} net={netQty} (source=Portfolio.GetPosition)");
                            return (netQty, avgPrice);
                        }
                    }
                }
                catch { /* seguir al fallback */ }

                // 2) Iterar Portfolio.Positions
                try
                {
                    var positionsProp = Portfolio.GetType().GetProperty("Positions");
                    var positions = positionsProp?.GetValue(Portfolio) as System.Collections.IEnumerable;
                    if (positions != null)
                    {
                        foreach (var pos in positions)
                        {
                            var secProp = pos.GetType().GetProperty("Security");
                            var secStr = secProp?.GetValue(pos)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(secStr) && (Security?.ToString() ?? "") == secStr)
                            {
                                var netQty = 0;
                                var avgPrice = 0m;

                                // Leer Net/Amount/Qty/Position
                                foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                                {
                                    var p = pos.GetType().GetProperty(name);
                                    if (p != null)
                                    {
                                        var v = p.GetValue(pos);
                                        if (v != null)
                                        {
                                            netQty = Convert.ToInt32(v);
                                            break;
                                        }
                                    }
                                }

                                // Leer AvgPrice/AveragePrice/EntryPrice/Price
                                foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                                {
                                    var p = pos.GetType().GetProperty(name);
                                    if (p != null)
                                    {
                                        var v = p.GetValue(pos);
                                        if (v != null)
                                        {
                                            avgPrice = Convert.ToDecimal(v);
                                            if (avgPrice > 0m) break;
                                        }
                                    }
                                }

                                if (EnableLogging && (netQty != 0 || avgPrice > 0m))
                                    DebugLog.W("RM/SNAP", $"pos avgPrice={avgPrice:F2} net={netQty} (source=Portfolio.Positions)");
                                return (netQty, avgPrice);
                            }
                        }
                    }
                }
                catch { /* devolver valores por defecto */ }
            }
            catch { }
            return (0, 0m);
        }

        // Sondea la cuenta real (TradingManager.Account / Portfolio) por reflexión.
        // Busca en orden: Equity, NetLiquidation, Balance, AccountBalance, Cash.
        // Devuelve el primer valor > 0m encontrado.
        private decimal ReadAccountEquityUSD()
        {
            try
            {
                // 1) TradingManager.Account.* (Equity/Balance/NetLiquidation/Cash)
                var tm = this.TradingManager;
                var acct = tm?.GetType().GetProperty("Account")?.GetValue(tm);
                if (acct != null)
                {
                    foreach (var name in new[] { "Equity", "NetLiquidation", "Balance", "AccountBalance", "Cash" })
                    {
                        var p = acct.GetType().GetProperty(name);
                        if (p == null) continue;
                        try
                        {
                            var v = Convert.ToDecimal(p.GetValue(acct));
                            if (v > 0m) return v;
                        }
                        catch { }
                    }
                }
                // 2) Portfolio.* (Equity/Balance/Cash)
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

        // Normaliza cadenas para comparar (mayúsculas, sin espacios/guiones, sin acentos)
        private static string K(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            s = s.ToUpperInvariant();
            s = s.Replace(" ", "").Replace("-", "").Replace("_", "");
            s = s.Replace("É","E").Replace("Á","A").Replace("Í","I").Replace("Ó","O").Replace("Ú","U").Replace("Ñ","N");
            return s;
        }

        private decimal ResolveTickValueUsd(string securityNameOrCode, string overrides, decimal fallback)
        {
            var key = K(securityNameOrCode);

            // 1) Overrides de la UI: "MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10;MICROEMININASDAQ100=0.5"
            if (!string.IsNullOrWhiteSpace(overrides))
            {
                foreach (var pair in overrides.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=');
                    if (kv.Length != 2) continue;
                    var k = K(kv[0]);
                    var vRaw = kv[1].Trim().Replace(',', '.');
                    if (!decimal.TryParse(vRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) continue;

                    // Coincidencia amplia: si el nombre/código contiene la clave normalizada
                    if (key.Contains(k)) return v;
                }
            }

            // 2) Heurísticas por si el broker no da el código del símbolo
            if (key.Contains("MICROEMININASDAQ") || key.StartsWith("MNQ")) return 0.5m;
            if (key.StartsWith("NQ")) return 5m;
            if (key.StartsWith("MES")) return 1.25m;
            if (key.StartsWith("ES")) return 12.5m;
            if (key.Contains("MICROGOLD") || key.StartsWith("MGC")) return 1m;
            if (key.StartsWith("GC")) return 10m;

            return fallback;
        }

        private decimal ResolveTickValueUSD()
        {
            return ResolveTickValueUsd(Security?.ToString() ?? "", TickValueOverrides ?? "", FallbackTickValueUsd);
        }

        // Devuelve el objeto Position nativo de ATAS (para usar ClosePosition del TradingManager).
        private object GetAtasPositionObject()
        {
            try
            {
                if (Portfolio == null || Security == null) return null;
                var getPos = Portfolio.GetType().GetMethod("GetPosition", new[] { Security.GetType() });
                if (getPos != null)
                {
                    var pos = getPos.Invoke(Portfolio, new object[] { Security });
                    return pos; // puede ser null si no hay posiciÃƒÂ³n
                }
            }
            catch { }
            return null;
        }

        private decimal ExtractAvgFillPrice(Order order)
        {
            foreach (var name in new[] { "AvgPrice", "AveragePrice", "AvgFillPrice", "Price" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Convert.ToDecimal(p.GetValue(order));
                    if (v > 0m) return v;
                }
                catch { }
            }
            return 0m; // NADA de GetCandle() aquÃƒÂ­
        }

        private int ExtractFilledQty(Order order)
        {
            foreach (var name in new[] { "Filled", "FilledQuantity", "Quantity", "QuantityToFill", "Volume", "Lots" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = p.GetValue(order);
                    if (v == null) continue;
                    var q = Convert.ToInt32(v);
                    if (q > 0) return q;
                }
                catch { }
            }
            return 0;
        }

        private int ExtractDirFromOrder(Order order)
        {
            foreach (var name in new[] { "Direction", "Side", "OrderDirection", "OrderSide", "TradeSide" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var s = p.GetValue(order)?.ToString() ?? "";
                    if (s.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0) return +1;
                    if (s.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0) return -1;
                }
                catch { }
            }
            return 0;
        }


        protected override void OnCalculate(int bar, decimal value)
        {
            if (!IsActivated) return;

            // Refresca equity en UI (1×/s)
            if (DateTime.UtcNow >= _nextEquityProbeAt)
            {
                AccountEquitySnapshot = ReadAccountEquityUSD();
                _nextEquityProbeAt = DateTime.UtcNow.AddSeconds(1);
                if (EnableLogging) DebugLog.W("RM/SNAP", $"Equity≈{AccountEquitySnapshot:F2} USD");

                // Inicializar session starting equity la primera vez
                if (_sessionStartingEquity == 0m)
                {
                    // Priorizar AccountEquityOverride si está configurado
                    if (AccountEquityOverride > 0m)
                    {
                        _sessionStartingEquity = AccountEquityOverride;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Session started with OVERRIDE equity: {_sessionStartingEquity:F2} USD");
                    }
                    else if (AccountEquitySnapshot > 0m)
                    {
                        _sessionStartingEquity = AccountEquitySnapshot;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Session started with detected equity: {_sessionStartingEquity:F2} USD");
                    }
                    else
                    {
                        // Último fallback: usar 10000 USD como base
                        _sessionStartingEquity = 10000m;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Session started with DEFAULT equity (no account data): {_sessionStartingEquity:F2} USD");
                    }
                }

                // Actualizar Session P&L = Realized + Unrealized
                UpdateSessionPnL();
            }

            // Heartbeat del estado de Stop-to-Flat (visible en logs)
            if (EnableLogging && IsFirstTickOf(bar))
            {
                var now = DateTime.UtcNow;
                DebugLog.W("RM/HEARTBEAT",
                    $"bar={bar} t={GetCandle(bar).Time:HH:mm:ss} mode={SizingMode} be={BreakEvenMode} trail={TrailingMode} build={BuildStamp} graceUntil={_postCloseUntil:HH:mm:ss.fff} inGrace={(now <= _postCloseUntil)}");
                DebugLog.W("RM/STOP", $"tick={bar} inStop={_stopToFlat} stopGraceUntil={_rmStopGraceUntil:HH:mm:ss.fff} inGrace={(now <= _rmStopGraceUntil)}");
            }

            // Si estamos parando (Stop-to-Flat), no armes/adjuntes brackets nuevos
            if (_stopToFlat)
            {
                // Limpieza pasiva mientras drenamos: reporta si siguen vivos SL/TP
                var flat = Math.Abs(ReadNetPositionSafe()) == 0;
                var live = HasLiveRmBrackets();
                if (EnableLogging)
                    DebugLog.W("RM/STOP", $"drain: flat={flat} liveBrackets={live} inGrace={(DateTime.UtcNow <= _rmStopGraceUntil)}");

                // Barrido periÃƒÂ³dico durante el stop: re-cancelar y re-flatten si hace falta (sin duplicar)
                var now = DateTime.UtcNow;
                if (now >= _nextStopSweepAt && now <= _rmStopGraceUntil)
                {
                    CancelNonBracketWorkingOrders("stop-sweep");
                    // reintento preferente: flatten nativo
                    var closedAgain = TryClosePositionViaTradingManager();
                    if (!closedAgain)
                        EnsureFlattenOutstanding("stop-sweep");
                    _nextStopSweepAt = now.AddMilliseconds(_stopSweepEveryMs);
                }
                // No retornamos de OnCalculate global: simplemente dejamos que no se dispare TryAttachBracketsNow()
            }

            // Fallback por barra si quedÃƒÂ³ pendiente
            if (_pendingAttach && (DateTime.UtcNow - _pendingSince).TotalMilliseconds >= _attachThrottleMs)
                TryAttachBracketsNow();

            // === Net & external-close detection (with attach protection) ===
            try
            {
                var currentNet = ReadNetPosition();
                var isFlat = Math.Abs(currentNet) == 0;
                var hadBrNow = HasLiveRmBrackets();

                bool transitionClose = (_prevNet != 0 && currentNet == 0);
                bool bracketsEdgeClose = (_hadRmBracketsPrevTick && !hadBrNow && isFlat);
                bool recentAttach = _pendingAttach && (DateTime.UtcNow - _lastAttachArmAt).TotalMilliseconds < AttachProtectMs;
                bool debounce = (DateTime.UtcNow - _lastExternalCloseAt).TotalMilliseconds < ExternalCloseDebounceMs;

                if ((transitionClose || bracketsEdgeClose) && !debounce)
                {
                    CancelResidualBrackets("external close detected");
                    if (!recentAttach) _pendingAttach = false; // <- NO matar attach reciÃƒÂ©n armado
                    _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                    _lastExternalCloseAt = DateTime.UtcNow;
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", $"External close Ã¢â€ â€™ grace until={_postCloseUntil:HH:mm:ss.fff}, recentAttach={recentAttach}");
                }

                // Update prev snapshots for next tick
                _hadRmBracketsPrevTick = hadBrNow;
                _prevNet = currentNet;
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"OnCalculate net/check EX: {ex.Message}");
            }

            // 4) Limpia solo si realmente estamos "idle" (sin attach armado/BE ni brackets) con hysteresis
            try
            {
                var net = Math.Abs(ReadNetPosition());
                var hasLive = HasLiveRmBrackets(includeNone: true);
                var justArmed = _pendingAttach && (DateTime.UtcNow - _lastAttachArmAt).TotalMilliseconds < (AttachProtectMs + 400);
                // No limpies si hay BE armado: evita desarmarlo por snapshots 0
                if (!_beArmed && net == 0 && !hasLive && !justArmed)
                {
                    ResetAttachState("flat idle");
                    if (_postCloseUntil < DateTime.UtcNow)
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(250);
                }
            }
            catch { /* defensivo */ }

            // === BE por TOUCH de precio (funciona con TP real o virtual) ===
            try
            {
                if (_beArmed && !_beDone && BreakEvenMode == RmBeMode.OnTPTouch)
                {
                    var snap = ReadPositionSnapshot();
                    // inPos robusto: acepta net de snapshot, net previo, o "en gracia" si Virtual BE
                    var inPos = Math.Abs(snap.NetQty) != 0 || Math.Abs(_prevNet) != 0;
                    if (!inPos && VirtualBreakEven) inPos = true;

                    if (inPos && _beTargetPx > 0m)
                    {
                        var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);
                        var (last, hi, lo) = GetLastPriceTriplet();
                        var dir = Math.Sign(snap.NetQty != 0 ? snap.NetQty : (_prevNet != 0 ? _prevNet : _beDirHint));

                        // Toque intrabar: LONG usa High, SHORT usa Low
                        bool touched = dir > 0 ? (hi >= _beTargetPx) : (lo <= _beTargetPx);

                        if (EnableLogging)
                            DebugLog.W("RM/BE/TRACE",
                                $"inPos={inPos} dir={(dir > 0 ? "LONG" : "SHORT")} tgt={_beTargetPx:F2} last={last:F2} hi={hi:F2} lo={lo:F2} touched={touched}");

                        if (touched)
                        {
                            var refPx = snap.AvgPrice > 0m ? snap.AvgPrice : last;
                            var bePx = ComputeBePrice(dir, refPx, tickSize);
                            if (EnableLogging) DebugLog.W("RM/BE", $"TOUCH trigger @ {_beTargetPx:F2} Ã¢â€ â€™ move SL to BE {bePx:F2}");
                            MoveAllRmStopsTo(bePx, "BE touch");
                            _beDone = true;
                        }
                    }
                }
            }
            catch (Exception ex) { DebugLog.W("RM/BE", $"BE touch check EX: {ex.Message}"); }
        }

        protected override void OnOrderChanged(Order order)
        {
            // ==== Enhanced logging for ALL order events ====
            try
            {
                var c = order?.Comment ?? "";
                var st = order.Status();
                if (EnableLogging)
                    DebugLog.W("RM/EVT", $"OnOrderChanged: id={order?.Id} comment='{c}' status={st} side={order?.Direction} qty={order?.QuantityToFill} canceled={order?.Canceled}");
                if (_stopToFlat && EnableLogging)
                    DebugLog.W("RM/STOP", $"EVT: id={order?.Id} comment='{c}' status={st} qty={order?.QuantityToFill} canceled={order?.Canceled}");

                // Track also EXTERNAL orders (ChartTrader) for later mass-cancel on Stop
                // Scope to this instrument+portfolio
                if (order?.Security?.ToString() == Security?.ToString() &&
                    order?.Portfolio?.ToString() == Portfolio?.ToString())
                {
                    lock (_liveOrdersLock)
                    {
                        _liveOrders.Add(order);
                        if (_liveOrders.Count > 512)
                            _liveOrders.RemoveRange(0, _liveOrders.Count - 512);
                    }
                }
            }
            catch { /* ignore logging issues */ }

            try
            {
                if (!IsActivated || !ManageManualEntries) return;

                var comment = order?.Comment ?? "";
                var st = order.Status();

                // 3a) Ignorar fills de cierre manual/estrategia
                if (comment.Equals("Close position", StringComparison.OrdinalIgnoreCase))
                {
                    if (st == OrderStatus.Filled || st == OrderStatus.PartlyFilled)
                    {
                        // Cuarentena corta para evitar attach reentrante
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(800);
                        ResetAttachState("Close position fill");
                    }
                    if (EnableLogging) DebugLog.W("RM/GATE", "Skip arming on 'Close position' fill");
                    return;
                }

                if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged: comment={comment} status={st}");

                // Ignora la 468 y mis propias RM
                if (comment.StartsWith(IgnorePrefix))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: IgnorePrefix detected ({IgnorePrefix})");
                    return;
                }
                if (comment.StartsWith(OwnerPrefix))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: OwnerPrefix detected ({OwnerPrefix})");
                    return;
                }

                // Keep legacy "Close position" detection (when comment is present)
                if (comment.IndexOf("Close position", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pendingAttach = false;
                    CancelResidualBrackets("user pressed Close (comment match)");
                    var graceUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                    _postCloseUntil = graceUntil;
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", $"Grace window opened after Close (EVT), until={graceUntil:HH:mm:ss.fff}");
                    return;
                }

                // SÃƒÂ³lo nos interesan fills/parciales
                if (!(st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: status={st} (not filled/partly)");
                    return;
                }

                // Marca attach pendiente; la direcciÃƒÂ³n la deduciremos con el net por barra o ahora mismo
                _pendingDirHint = ExtractDirFromOrder(order);
                _pendingFillQty = ExtractFilledQty(order);

                // Solo guardar entryPrice si el order tiene AvgPrice vÃƒÂ¡lido; si no, lo obtendremos de la posiciÃƒÂ³n
                var orderAvgPrice = ExtractAvgFillPrice(order);
                _pendingEntryPrice = orderAvgPrice > 0m ? orderAvgPrice : 0m;

                // Anclar la "prev-bar" en el instante del fill (N-1 respecto a la barra visible ahora)
                try
                {
                    // En ATAS: ÃƒÂºltimo ÃƒÂ­ndice es CurrentBar-1 (vela actual). La "prev" cerrada es CurrentBar-2.
                    _pendingPrevBarIdxAtFill = Math.Max(0, CurrentBar - 2);
                    if (EnableLogging)
                        DebugLog.W("RM/STOPMODE", $"Anchor prevBarIdx set at fill (N-1): {_pendingPrevBarIdxAtFill} (curBarAtFill={CurrentBar})");
                }
                catch { _pendingPrevBarIdxAtFill = -1; }

                // Armar attach con protecciÃƒÂ³n temporal
                _pendingAttach = true;
                _pendingSince = DateTime.UtcNow;
                _lastAttachArmAt = _pendingSince;

                if (EnableLogging)
                    DebugLog.W("RM/ORD", $"Manual order DETECTED Ã¢â€ â€™ pendingAttach=true | dir={_pendingDirHint} fillQty={_pendingFillQty} entryPx={_pendingEntryPrice:F2}");

                // === Track position entry for P&L ===
                // Intentar obtener precio de entry de múltiples fuentes
                var entryPriceForTracking = _pendingEntryPrice;
                if (entryPriceForTracking <= 0m)
                {
                    // Fallback 1: leer desde posición actual
                    var snapForEntry = ReadPositionSnapshot();
                    entryPriceForTracking = snapForEntry.AvgPrice;
                    if (EnableLogging) DebugLog.W("RM/PNL", $"Entry price from order=0, trying position avgPrice={entryPriceForTracking:F2}");
                }

                if (entryPriceForTracking <= 0m)
                {
                    // Fallback 2: usar precio de la última barra (mejor aproximación)
                    entryPriceForTracking = GetLastPriceSafe();
                    if (EnableLogging) DebugLog.W("RM/PNL", $"Entry price from position=0, using last bar price={entryPriceForTracking:F2}");
                }

                if (entryPriceForTracking > 0m && _pendingFillQty > 0)
                {
                    TrackPositionEntry(entryPriceForTracking, _pendingFillQty, _pendingDirHint);
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/PNL", $"SKIP TrackPositionEntry: entryPx={entryPriceForTracking:F2} fillQty={_pendingFillQty}");
                }

                TryAttachBracketsNow(); // intenta en el mismo tick; si el gate decide WAIT, ya lo reintentarÃƒÂ¡ OnCalculate
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"OnOrderChanged EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }

            // === BE por FILL real del TP ===
            try
            {
                if (_beArmed && !_beDone && BreakEvenMode == RmBeMode.OnTPFill)
                {
                    var c = order?.Comment ?? "";
                    var st = order.Status();
                    if (c.StartsWith(OwnerPrefix + "TP:") && (st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                    {
                        // Confirmar lado y entrada
                        var snap = ReadPositionSnapshot();
                        var dir = Math.Sign(snap.NetQty != 0 ? snap.NetQty : _beDirHint);
                        if (dir != 0)
                        {
                            var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);
                            var bePx = ComputeBePrice(dir, snap.AvgPrice > 0m ? snap.AvgPrice : ExtractAvgFillPrice(order), tickSize);
                            if (EnableLogging) DebugLog.W("RM/BE", $"FILL trigger by TP Ã¢â€ â€™ move SL to BE {bePx:F2}");
                            MoveAllRmStopsTo(bePx, "BE fill");
                            _beDone = true;
                        }
                    }
                }
            }
            catch { /* tolerante */ }

            // === Track TP/SL fills for P&L ===
            try
            {
                var c = order?.Comment ?? "";
                var st = order.Status();
                if ((c.StartsWith(OwnerPrefix + "TP:") || c.StartsWith(OwnerPrefix + "SL:"))
                    && (st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                {
                    if (EnableLogging) DebugLog.W("RM/PNL", $"TP/SL fill detected: comment={c} status={st}");

                    var exitPrice = ExtractAvgFillPrice(order);
                    var filledQty = ExtractFilledQty(order);

                    if (EnableLogging) DebugLog.W("RM/PNL", $"Exit details: price={exitPrice:F2} qty={filledQty}");

                    // Fallback: si exitPrice es 0, usar order.Price
                    if (exitPrice <= 0m)
                    {
                        exitPrice = order.Price;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Using order.Price as fallback: {exitPrice:F2}");
                    }

                    if (exitPrice > 0m && filledQty > 0)
                    {
                        // Dirección: TP/SL son órdenes de cierre, así que están en dirección opuesta a la posición
                        // Si fue LONG → TP/SL son SELL → dir original = +1
                        // Si fue SHORT → TP/SL son BUY → dir original = -1
                        var orderDir = ExtractDirFromOrder(order);
                        var positionDir = -orderDir;  // invertir porque es orden de cierre

                        if (EnableLogging) DebugLog.W("RM/PNL", $"Calling TrackPositionClose: exitPx={exitPrice:F2} qty={filledQty} dir={positionDir}");
                        TrackPositionClose(exitPrice, filledQty, positionDir);
                    }
                    else
                    {
                        if (EnableLogging) DebugLog.W("RM/PNL", $"SKIP TrackPositionClose: exitPx={exitPrice:F2} qty={filledQty}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"Track TP/SL fill EX: {ex.Message}");
            }
        }

        // New: capture newly seen orders too (fires when order is registered)
        protected override void OnNewOrder(Order order)
        {
            try
            {
                if (order == null) return;
                if (EnableLogging)
                    DebugLog.W("RM/EVT", $"OnNewOrder: id={order.Id} comment='{order.Comment}' type={order.Type} status={order.Status()} qty={order.QuantityToFill}");
                if (order.Security?.ToString() == Security?.ToString() &&
                    order.Portfolio?.ToString() == Portfolio?.ToString())
                {
                    lock (_liveOrdersLock)
                    {
                        _liveOrders.Add(order);
                        if (_liveOrders.Count > 512)
                            _liveOrders.RemoveRange(0, _liveOrders.Count - 512);
                    }
                }
            }
            catch { }
        }

        // =================== Stop Strategy hooks ===================
        protected override void OnStarted()
        {
            try
            {
                AccountEquitySnapshot = ReadAccountEquityUSD();
                if (EnableLogging) DebugLog.W("RM/SNAP", $"Equity init Ã¢â€ °Ë† {AccountEquitySnapshot:F2} USD");

                _stopToFlat = false;
                _rmStopGraceUntil = DateTime.MinValue;
                _nextStopSweepAt   = DateTime.MinValue;
                if (EnableLogging) DebugLog.W("RM/STOP", "Started Ã¢â€ â€™ reset stop-to-flat flags");
            }
            catch { }
        }

        protected override void OnStopping()
        {
            try
            {
                _stopToFlat = true;
                _rmStopGraceUntil = DateTime.UtcNow.AddMilliseconds(_rmStopGraceMs);
                _nextStopSweepAt   = DateTime.UtcNow; // primer barrido inmediato
                if (EnableLogging) DebugLog.W("RM/STOP", $"OnStopping Ã¢â€ â€™ engage StopToFlat, grace until={_rmStopGraceUntil:HH:mm:ss.fff}");

                // 1) Cancelar brackets + cualquier otra orden viva del instrumento
                CancelResidualBrackets("stop-to-flat");
                CancelNonBracketWorkingOrders("stop-to-flat");

                // 2) FLATTEN: intentar SIEMPRE el cierre nativo y dejar fallback armado
                var snap = ReadPositionSnapshot();
                if (EnableLogging) DebugLog.W("RM/STOP", $"Stop snapshot: net={snap.NetQty} avg={snap.AvgPrice:F2} (via TM/Portfolio)");
                var tmClosed = TryClosePositionViaTradingManager();
                if (EnableLogging) DebugLog.W("RM/STOP", $"ClosePosition attempt (TM) result={tmClosed}");
                // 3) Fallback garantizado: EnsureFlattenOutstanding no duplica y no-op si net==0
                EnsureFlattenOutstanding("OnStopping");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/STOP", $"OnStopping EX: {ex.Message}");
            }
            // No bloquees aquÃƒÂ­: ATAS seguirÃƒÂ¡ el ciclo; terminamos de drenar en eventos/ticks
        }

        protected override void OnStopped()
        {
            try
            {
                if (EnableLogging) DebugLog.W("RM/STOP", "OnStopped Ã¢â€ â€™ strategy stopped (final)");
                _stopToFlat = false;
                _rmStopGraceUntil = DateTime.MinValue;

                // Reset Session P&L tracking
                if (EnableLogging) DebugLog.W("RM/PNL", $"Session ended - Final P&L: {SessionPnL:F2} USD (Realized: {_sessionRealizedPnL:F2})");
                _sessionStartingEquity = 0m;
                _currentPositionEntryPrice = 0m;
                _currentPositionQty = 0;
                _sessionRealizedPnL = 0m;
                SessionPnL = 0m;
            }
            catch { }
        }

        private void TryAttachBracketsNow()
        {
            try
            {
                // Si acabamos de armar attach, no bloquees por una grace antigua
                if (_pendingAttach && DateTime.UtcNow <= _postCloseUntil)
                    _postCloseUntil = DateTime.MinValue;
                // Gate 1: cuarentena post-close
                if (DateTime.UtcNow <= _postCloseUntil)
                {
                    if (EnableLogging) DebugLog.W("RM/GATE", $"HOLD attach (in post-close grace until {_postCloseUntil:HH:mm:ss.fff})");
                    return;
                }
                // Gate 2: requiere attach armado
                if (!_pendingAttach)
                {
                    if (EnableLogging) DebugLog.W("RM/GATE", "Skip attach: _pendingAttach=false");
                    return;
                }
                var netAbs = Math.Abs(ReadNetPosition());
                // No hacemos return aquí: el gate inferior decidirá si WAIT / ATTACH / FALLBACK

                // Si estamos parando, no adjuntar nada (evita re-entradas durante stop)
                if (_stopToFlat)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", "Skipping attach: strategy is stopping");
                    _pendingAttach = false;
                    return;
                }

                // 0) Pre-check & diagnostics
                var now = DateTime.UtcNow;
                // Para los cierres por Stop usamos la gracia local de stop
                var inGrace = (now <= _postCloseUntil) || (now <= _rmStopGraceUntil);
                if (EnableLogging)
                    DebugLog.W("RM/ATTACH", $"Pre-check: pendingSince={_pendingSince:HH:mm:ss.fff} inGrace={inGrace} (closeGrace={(now <= _postCloseUntil)} stopGrace={(now <= _rmStopGraceUntil)})");

                // TelemetrÃƒÂ­a de estados de brackets antes del check
                LogOrderStateHistogram("pre-attach");

                // 1) Are there live brackets? (only SL:/TP:, ignore ENF)
                var any468  = HasLiveOrdersWithPrefix(IgnorePrefix);
                var anyRmSl = HasLiveOrdersWithPrefix(OwnerPrefix + "SL:");
                var anyRmTp = HasLiveOrdersWithPrefix(OwnerPrefix + "TP:");
                var anyBrackets = any468 || anyRmSl || anyRmTp;
                if (EnableLogging)
                    DebugLog.W("RM/ATTACH", $"Bracket check: any468={any468} anyRmSl={anyRmSl} anyRmTp={anyRmTp} anyBrackets={anyBrackets}");

                if (anyBrackets && !inGrace)
                {
                    var waitedMs = (int)(now - _pendingSince).TotalMilliseconds;
                    if (waitedMs > _maxRetryMs)
                    {
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs); // bypass
                        if (EnableLogging)
                            DebugLog.W("RM/GRACE", $"MaxRetry {waitedMs}ms Ã¢â€ â€™ forcing grace & proceeding");
                        // sigue sin return
                    }
                    else if (waitedMs > _cleanupWaitMs)
                    {
                        CancelResidualBrackets($"cleanup timeout waited={waitedMs}ms");
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                        _pendingSince = DateTime.UtcNow;
                        if (EnableLogging)
                            DebugLog.W("RM/GRACE", $"Cleanup timeout Ã¢â€ â€™ grace reset to {_postCloseUntil:HH:mm:ss.fff}");
                        return; // reintenta
                    }
                    else
                    {
                        if (EnableLogging)
                            DebugLog.W("RM/WAIT", $"live brackets Ã¢â€ â€™ retry (waited={waitedMs}ms)");
                        return;
                    }
                }
                else if (anyBrackets && inGrace)
                {
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", "post-close grace ACTIVE Ã¢â€ â€™ ignoring live-brackets block");
                }

                // 2) Gate: si estamos en GRACE, saltamos el gate y vamos a FALLBACK ya mismo
                var netNow = ReadNetPosition();
                var gateWaitedMs = (int)(DateTime.UtcNow - _pendingSince).TotalMilliseconds;
                if (EnableLogging) DebugLog.W("RM/GATE", $"Gate check: _prevNet={_prevNet} netNow={netNow} waitedMs={gateWaitedMs} deadline={_attachDeadlineMs} inGrace={inGrace}");

                if (!inGrace)
                {
                    if (Math.Abs(_prevNet) == 0 && Math.Abs(netNow) > 0)
                    {
                        if (EnableLogging) DebugLog.W("RM/GATE", $"VALID TRANSITION: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms Ã¢â€ â€™ ATTACH");
                    }
                    else
                    {
                        if (gateWaitedMs < _attachDeadlineMs)
                        {
                            if (EnableLogging) DebugLog.W("RM/GATE", $"WAITING: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms < deadline={_attachDeadlineMs}ms Ã¢â€ â€™ WAIT");
                            return;
                        }
                        if (!(AllowAttachFallback && _pendingDirHint != 0))
                        {
                            _pendingAttach = false;
                            if (EnableLogging) DebugLog.W("RM/GATE", $"ABORT: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms Ã¢â€ â€™ no fallback allowed");
                            if (EnableLogging) DebugLog.W("RM/ABORT", "flat after TTL");
                            return;
                        }
                        if (EnableLogging) DebugLog.W("RM/GATE", $"FALLBACK: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms Ã¢â€ â€™ ATTACH(FALLBACK) dirHint={_pendingDirHint}");
                    }
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/GATE", $"GRACE BYPASS: skipping net gate Ã¢â€ â€™ ATTACH(FALLBACK) dirHint={_pendingDirHint}");
                }

                var dir = Math.Abs(netNow) > 0 ? Math.Sign(netNow) : _pendingDirHint;
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Direction determined: dir={dir} (netNow={netNow}, dirHint={_pendingDirHint})");

                // qty OBJETIVO del plan:
                //  - Manual  Ã¢â€ â€™ SIEMPRE la UI (ManualQty), no el net/fill
                //  - Riesgo  Ã¢â€ â€™ la calcularÃƒÂ¡ el engine
                int manualQtyToUse = ManualQty;
                if (SizingMode == RmSizingMode.Manual)
                {
                    manualQtyToUse = Math.Clamp(ManualQty, Math.Max(1, MinQty), Math.Max(1, MaxQty));
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Manual mode: Using UI ManualQty={manualQtyToUse} (ignoring net/fill for TARGET)");
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Risk-based sizing: engine will compute target qty");
                }

                // Instrumento (fallbacks)
                var tickSize = FallbackTickSize;
                try { tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize); } catch { }

                var secName = (Security?.ToString() ?? string.Empty);
                var tickValueResolved = ResolveTickValueUsd(secName, TickValueOverrides, FallbackTickValueUsd);
                var tickValue = tickValueResolved;

                if (EnableLogging)
                    DebugLog.W("RM/TICK", $"tickSize={tickSize} tickValueUSD={tickValue} symbolKey='{secName}' overrides='{TickValueOverrides}'");

                // precio de entrada: order Ã¢â€ â€™ avgPrice posiciÃƒÂ³n Ã¢â€ â€™ vela previa (Close)
                var entryPx = _pendingEntryPrice;
                if (entryPx <= 0m)
                {
                    var snap = ReadPositionSnapshot();
                    entryPx = snap.AvgPrice;
                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", $"Using position avgPrice: entryPx={entryPx:F2} (orderPrice was {_pendingEntryPrice:F2})");
                }
                if (entryPx <= 0m)
                {
                    try
                    {
                        var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                        entryPx = GetCandle(barIdx).Close;
                        if (EnableLogging) DebugLog.W("RM/PLAN", $"Fallback candle price used: entryPx={entryPx:F2} (bar={barIdx})");
                    } catch { }
                    if (entryPx <= 0m) { if (EnableLogging) DebugLog.W("RM/PLAN", "No valid entryPx available Ã¢â€ â€™ retry"); return; }
                }

                // === STOP por estructura (opcional) ===
                var approxStopTicks = Math.Max(1, DefaultStopTicks);
                decimal? overrideStopPx = null;
                try
                {
                    if (StopPlacementMode == RmStopPlacement.PrevBarOppositeExtreme)
                    {
                        var prevIdx = _pendingPrevBarIdxAtFill >= 0
                            ? Math.Max(0, _pendingPrevBarIdxAtFill)
                            : Math.Max(0, CurrentBar - 2);          // N-1 cerrada
                        var prev = GetCandle(prevIdx);
                        var cur  = GetCandle(Math.Max(0, CurrentBar - 1)); // N (intravela)
                        if (prev != null && cur != null)
                        {
                            // Engulfing-safe base:
                            // LONG → min(prev.Low, cur.Low) ; SHORT → max(prev.High, cur.High)
                            var prevExtreme = (dir > 0) ? prev.Low  : prev.High;
                            var curExtreme  = (dir > 0) ? cur.Low   : cur.High;
                            var baseExtreme = (dir > 0)
                                ? Math.Min(prevExtreme, curExtreme)
                                : Math.Max(prevExtreme, curExtreme);

                            var offsetTicks = Math.Max(0, PrevBarOffsetTicks);
                            var offset      = offsetTicks * Convert.ToDecimal(tickSize);
                            var outside     = PrevBarOffsetSide == RmPrevBarOffsetSide.Outside;

                            decimal rawSL;
                            if (dir > 0) // LONG
                                rawSL = outside ? (baseExtreme - offset) : (baseExtreme + offset);
                            else         // SHORT
                                rawSL = outside ? (baseExtreme + offset) : (baseExtreme - offset);

                            overrideStopPx   = ShrinkPrice(rawSL);  // tick-safe
                            approxStopTicks  = Math.Max(1, (int)Math.Round(Math.Abs(entryPx - overrideStopPx.Value) / tickSize));
                            if (EnableLogging)
                                DebugLog.W("RM/STOPMODE",
                                    $"PrevBar+N SL({PrevBarOffsetSide}): prevIdx={prevIdx} prev={prevExtreme:F2} cur={curExtreme:F2} base={baseExtreme:F2} offTicks={offsetTicks} → SL={overrideStopPx.Value:F2} ticks≈{approxStopTicks}");
                        }
                    }
                } catch { /* fallback a DefaultStopTicks */ }

                var ctx = new MyAtas.Risk.Models.EntryContext(
                    Account: Portfolio?.ToString() ?? "DEFAULT",
                    Symbol: Security?.ToString() ?? "",
                    Direction: dir > 0 ? MyAtas.Risk.Models.Direction.Long : MyAtas.Risk.Models.Direction.Short,
                    EntryPrice: entryPx,
                    ApproxStopTicks: approxStopTicks,                      // <-- usa ticks desde N-1 si procede
                    TickSize: tickSize,
                    TickValueUSD: tickValue,
                    TimeUtc: DateTime.UtcNow
                );

                var sizingCfg = new MyAtas.Risk.Models.SizingConfig(
                    Mode: SizingMode.ToString(),
                    ManualQty: manualQtyToUse,
                    RiskUsd: RiskPerTradeUsd,
                    RiskPct: RiskPercentOfAccount,
                    AccountEquityOverride: 0m,
                    TickValueOverrides: TickValueOverrides,
                    UnderfundedPolicy: Underfunded,
                    MinQty: Math.Max(1, MinQty),
                    MaxQty: Math.Max(1, MaxQty)
                );

                // TPs desde UI (filtrando splits=0 y normalizando)
                var (tpR, tpSplits) = _RmSplitHelper.BuildTpArrays(PresetTPs, TP1R, TP2R, TP3R, TP1pctunit, TP2pctunit, TP3pctunit);
                if (EnableLogging)
                    DebugLog.W("RM/SPLIT", $"UI -> preset={PresetTPs} tpR=[{string.Join(",", tpR)}] splits=[{string.Join(",", tpSplits)}]");

                var bracketCfg = new MyAtas.Risk.Models.BracketConfig(
                    StopTicks: approxStopTicks,                            // <-- idem
                    SlOffsetTicks: 0m,
                    TpRMultiples: tpR,
                    Splits: tpSplits
                );

                var engine = GetEngine();
                if (engine == null) { if (EnableLogging) DebugLog.W("RM/PLAN", "Engine not available Ã¢â€ â€™ abort"); return; }

                var plan = engine.BuildPlan(ctx, sizingCfg, bracketCfg, out var szReason);

                if (plan == null)
                {
                    if (EnableLogging) DebugLog.W("RM/PLAN", "BuildPlan Ã¢â€ â€™ null");
                    return;
                }
                else
                {
                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", $"Built plan: totalQty={plan.TotalQty} stop={plan.StopLoss?.Price:F2} tps={plan.TakeProfits?.Count} reason={plan.Reason}");
                }

                // ===== "LA UI MANDA": Si EnforceManualQty está activo, usar ManualQty =====
                var manualTarget = Math.Max(MinQty, ManualQty);
                var targetForEnforce = EnforceManualQty ? manualTarget : plan.TotalQty;

                // Si estamos imponiendo cantidad manual y difiere del plan,
                // recalculamos los splits para que brackets y ENFORCE vayan alineados.
                if (EnforceManualQty && targetForEnforce != plan.TotalQty)
                {
                    var q = targetForEnforce;
                    var s1 = Math.Max(0, Math.Min(100, TP1pctunit));
                    var s2 = Math.Max(0, Math.Min(100, TP2pctunit));
                    var s3 = Math.Max(0, Math.Min(100, TP3pctunit));
                    var sum = Math.Max(1, s1 + s2 + s3);
                    int q1 = (int)Math.Floor(q * s1 / (decimal)sum);
                    int q2 = (int)Math.Floor(q * s2 / (decimal)sum);
                    int q3 = q - q1 - q2; // "resto" al último para evitar qty=0

                    // Extraer precios del plan antes de reconstruir
                    var tp1Px = plan.TakeProfits.Count > 0 ? plan.TakeProfits[0].Price : 0m;
                    var tp2Px = plan.TakeProfits.Count > 1 ? plan.TakeProfits[1].Price : 0m;
                    var tp3Px = plan.TakeProfits.Count > 2 ? plan.TakeProfits[2].Price : 0m;

                    // Reconstruir lista de TPs con nuevas cantidades (BracketLeg es immutable)
                    var newTps = new System.Collections.Generic.List<MyAtas.Risk.Models.BracketLeg>();
                    if (q1 > 0) newTps.Add(new MyAtas.Risk.Models.BracketLeg(tp1Px, q1));
                    if (q2 > 0) newTps.Add(new MyAtas.Risk.Models.BracketLeg(tp2Px, q2));
                    if (q3 > 0) newTps.Add(new MyAtas.Risk.Models.BracketLeg(tp3Px, q3));

                    // Reconstruir plan completo con nueva cantidad total y nuevos TPs (record positional)
                    plan = new MyAtas.Risk.Models.RiskPlan(
                        TotalQty: q,
                        StopLoss: plan.StopLoss,
                        TakeProfits: newTps,
                        OcoPolicy: plan.OcoPolicy,
                        Reason: plan.Reason + " [UI override]"
                    );

                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", $"LA UI MANDA: Overriding plan qty {plan.TotalQty}→{q}, splits=[{q1},{q2},{q3}]");
                }

                // ===== TARGET QTY por modo de dimensionado =====
                var riskPerContract = Math.Max(1, approxStopTicks) * tickValue; // USD por contrato
                int targetQty;
                if (SizingMode == RmSizingMode.Manual)
                {
                    targetQty = Math.Clamp(ManualQty, Math.Max(1, MinQty), Math.Max(1, MaxQty));
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"TARGET QTY (Manual) = {targetQty}");
                }
                else
                {
                    decimal budgetUsd = 0m;
                    if (SizingMode == RmSizingMode.FixedRiskUSD)
                        budgetUsd = Math.Max(0m, RiskPerTradeUsd);
                    else // PercentAccount
                    {
                        var equity = AccountEquityOverride > 0m ? AccountEquityOverride : AccountEquitySnapshot;
                        budgetUsd = Math.Max(0m, Math.Round(equity * (RiskPercentOfAccount / 100m), 2));
                    }

                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Risk budget = {budgetUsd:F2} USD | risk/contract = {riskPerContract:F2} USD");
                    targetQty = (int)Math.Floor(budgetUsd / Math.Max(0.01m, riskPerContract));
                    targetQty = Math.Clamp(targetQty, Math.Max(0, MinQty), Math.Max(1, MaxQty));
                    if (targetQty <= 0)
                    {
                        if (Underfunded == MyAtas.Risk.Models.UnderfundedPolicy.Min1)
                        {
                            targetQty = 1;
                            if (EnableLogging) DebugLog.W("RM/SIZING", "Underfunded Ã¢â€ â€™ forcing qty=1");
                        }
                        else
                        {
                            if (EnableLogging) DebugLog.W("RM/SIZING", "Underfunded Ã¢â€ â€™ qty=0, abort attach");
                            _pendingAttach = false; return;
                        }
                    }
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"TARGET QTY (Risk mode) = {targetQty}");
                }

                // ===== ENFORCEMENT (imponer SIEMPRE el objetivo del modo activo) =====
                targetForEnforce = (SizingMode == RmSizingMode.Manual)
                    ? Math.Clamp(ManualQty, Math.Max(1, MinQty), Math.Max(1, MaxQty))
                    : plan.TotalQty; // objetivo de riesgo

                var currentNet = Math.Abs(ReadNetPosition());
                var filledHint = Math.Max(0, _pendingFillQty);
                var seen = Math.Max(currentNet, filledHint);
                var delta = targetForEnforce - seen;
                if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE: target={targetForEnforce} seen={seen} delta={delta}");

                if (delta != 0)
                {
                    var addSide = (dir > 0 ? OrderDirections.Buy : OrderDirections.Sell);
                    var cutSide = (dir > 0 ? OrderDirections.Sell : OrderDirections.Buy);

                    if (delta > 0)
                    {
                        // abrir la diferencia
                        if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE TRIGGER: MARKET {addSide} +{delta}");
                        SubmitRmMarket(addSide, delta); // (no reduce-only)
                    }
                    else
                    {
                        // cerrar el exceso con reduce-only
                        if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE CUT: MARKET {cutSide} -{Math.Abs(delta)} (reduce-only)");
                        var o = new Order {
                            Portfolio = Portfolio, Security = Security,
                            Direction = cutSide, Type = OrderTypes.Market,
                            QuantityToFill = Math.Abs(delta),
                            Comment = $"{OwnerPrefix}ENF-CUT:{Guid.NewGuid():N}"
                        };
                        TrySetReduceOnly(o);
                        OpenOrder(o);
                    }
                }

                var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Cover side for brackets: coverSide={coverSide} (dir={dir})");

                // OCO 1:1 por TP Ã¢â‚¬â€ cada TP lleva su propio trozo de SL
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Starting bracket loop: TakeProfits.Count={plan.TakeProfits.Count}");

                // Reparto de cantidades por splits (sin ceros) - usar targetForEnforce
                var splitQty = _RmSplitHelper.SplitQty(targetForEnforce, tpSplits);
                if (EnableLogging) DebugLog.W("RM/SPLIT", $"targetForEnforce={targetForEnforce} Ã¢â€ â€™ splitQty=[{string.Join(",", splitQty)}]");

                for (int idx = 0; idx < plan.TakeProfits.Count && idx < splitQty.Length; idx++)
                {
                    var q = splitQty[idx];
                    if (q <= 0) { if (EnableLogging) DebugLog.W("RM/ORD", $"PAIR #{idx + 1}: skip (qty=0)"); continue; }
                    var tp = plan.TakeProfits[idx];
                    var ocoId = Guid.NewGuid().ToString("N");
                    var slPriceToUse = overrideStopPx ?? plan.StopLoss.Price;
                    SubmitRmStop(ocoId, coverSide, q, slPriceToUse);
                    SubmitRmLimit(ocoId, coverSide, q, tp.Price);
                    if (EnableLogging)
                        DebugLog.W("RM/ORD", $"PAIR #{idx + 1}: OCO SL {q}@{slPriceToUse:F2} + TP {q}@{tp.Price:F2} (dir={(dir>0?"LONG":"SHORT")})");
                }

                // Armar el BE (vigilancia del TP trigger para mover SL a breakeven)
                if (BreakEvenMode != RmBeMode.Off)
                {
                    // Calcular precio de TP basándonos en el R múltiple configurado (no en el array filtrado)
                    var tpTrigger = Math.Clamp(BeTriggerTp, 1, 3);
                    decimal tpRMultiple = tpTrigger == 1 ? TP1R : (tpTrigger == 2 ? TP2R : TP3R);

                    // Calcular distancia R desde el stop
                    var slPx = plan.StopLoss?.Price ?? 0m;
                    var r = dir > 0 ? (entryPx - slPx) : (slPx - entryPx);
                    if (r <= 0) r = tickSize; // fallback

                    // Precio de TP = entry ± (R × múltiple)
                    var tpPxRaw = entryPx + (dir > 0 ? (tpRMultiple * r) : -(tpRMultiple * r));
                    _beTargetPx = ShrinkPrice(tpPxRaw);  // tick-safe
                    _beDirHint  = dir;
                    _beArmed    = true;
                    _beDone     = false;

                    // Capturar precio ACTUAL al armar BE (baseline para tracking)
                    try
                    {
                        var currentCandle = GetCandle(Math.Max(0, CurrentBar - 1));
                        _beArmedAtPrice = currentCandle?.Close ?? entryPx;
                        _beMaxReached = 0m;  // Se inicializará en primer tick de GetLastPriceTriplet
                        _beMinReached = 0m;
                    }
                    catch
                    {
                        _beArmedAtPrice = entryPx;
                        _beMaxReached = 0m;
                        _beMinReached = 0m;
                    }

                    if (EnableLogging)
                        DebugLog.W("RM/BE", $"ARMED → mode={BreakEvenMode} triggerTP={tpTrigger} R={tpRMultiple} tpPx={_beTargetPx:F2} baseline={_beArmedAtPrice:F2} dir={(_beDirHint>0?"LONG":"SHORT")}");
                }
                else
                {
                    _beArmed = _beDone = false; _beTargetPx = 0m; _beDirHint = 0;
                    _beArmedAtPrice = _beMaxReached = _beMinReached = 0m;
                }

                _pendingAttach = false;
                _pendingPrevBarIdxAtFill = -1; // limpiar el ancla para la siguiente entrada
                if (EnableLogging) DebugLog.W("RM/PLAN", "Attach DONE");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/PLAN", $"TryAttachBracketsNow EX: {ex.Message}");
            }
        }

        // ====================== RM ORDER SUBMISSION HELPERS ======================
        private void SubmitRmStop(string oco, OrderDirections side, int qty, decimal triggerPx)
        {
            if (qty <= 0) { if (EnableLogging) DebugLog.W("RM/ORD", "SubmitRmStop SKIP: qty<=0"); return; }
            var comment = $"{OwnerPrefix}SL:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop ENTER: side={side} qty={qty} triggerPx={triggerPx:F2} oco={oco} comment={comment}");

            try
            {
                var shrunkPx = ShrinkPrice(triggerPx);
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop: ShrinkPrice({triggerPx:F2}) Ã¢â€ â€™ {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Stop,
                    TriggerPrice = shrunkPx, // Ã¢â€ Â tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // Ã¢â€ Â cancelar al cerrar
                TrySetReduceOnly(order);             // Ã¢â€ Â no abrir nuevas
                TrySetCloseOnTrigger(order);         // Ã¢â€ Â cerrar al disparar

                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop: Calling OpenOrder() for SL");
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"STOP SENT: {side} {qty} @{order.TriggerPrice:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmStop EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        private void SubmitRmLimit(string oco, OrderDirections side, int qty, decimal price)
        {
            if (qty <= 0) { if (EnableLogging) DebugLog.W("RM/ORD", "SubmitRmLimit SKIP: qty<=0"); return; }
            var comment = $"{OwnerPrefix}TP:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit ENTER: side={side} qty={qty} price={price:F2} oco={oco} comment={comment}");

            try
            {
                var shrunkPx = ShrinkPrice(price);
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit: ShrinkPrice({price:F2}) Ã¢â€ â€™ {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Limit,
                    Price = shrunkPx,       // Ã¢â€ Â tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // Ã¢â€ Â cancelar al cerrar
                TrySetReduceOnly(order);             // Ã¢â€ Â no abrir nuevas

                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit: Calling OpenOrder() for TP");
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"LIMIT SENT: {side} {qty} @{order.Price:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmLimit EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        // === Market (enforcement) ===
        private void SubmitRmMarket(OrderDirections side, int qty)
        {
            var comment = $"{OwnerPrefix}ENF:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmMarket ENTER: side={side} qty={qty} comment={comment}");

            try
            {
                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Market,
                    QuantityToFill = qty,
                    Comment = comment
                };
                // IMPORTANTE: no marcar ReduceOnly aquÃƒÂ­ Ã¢â‚¬â€ queremos abrir delta
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmMarket: Calling OpenOrder() for {side} +{qty} (ReduceOnly=false)");
                OpenOrder(order);
                if (EnableLogging) DebugLog.W("RM/ORD", $"ENFORCE MARKET SENT: {side} +{qty} @{GetLastPriceSafe():F2} comment={comment}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmMarket EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        private decimal GetLastPriceSafe()
        {
            try
            {
                var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                return GetCandle(barIdx).Close;
            }
            catch { return 0m; }
        }

        private (decimal Last, decimal High, decimal Low) GetLastPriceTriplet()
        {
            try
            {
                var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                var c = GetCandle(barIdx);
                if (c == null) return (0m, 0m, 0m);

                // Si BE no está armado, usar datos normales de barra
                if (!_beArmed || _beArmedAtPrice == 0m)
                    return (c.Close, c.High, c.Low);

                // BE armado: trackear extremos DESDE que se armó (no usar datos históricos)
                // Actualizar máximo/mínimo alcanzado DESPUÉS de armar
                if (_beMaxReached == 0m) _beMaxReached = _beArmedAtPrice;  // Inicializar si es primer tick
                if (_beMinReached == 0m) _beMinReached = _beArmedAtPrice;

                // Trackear extremos desde baseline
                _beMaxReached = Math.Max(_beMaxReached, c.High);
                _beMinReached = Math.Min(_beMinReached, c.Low);

                return (c.Close, _beMaxReached, _beMinReached);
            }
            catch { }
            return (0m, 0m, 0m);
        }

        // ==== Helpers: RM brackets detection & cleanup ====
        private bool HasLiveRmBrackets(bool includeNone = false)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return false;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!(c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:"))) continue;
                    var st = o.Status();
                    // Consider LIVE only when actively working according to ATAS enum:
                    // Placed (working) or PartlyFilled (still has remainder). Ignore None/Filled/Canceled.
                    // Considera temporalmente "None" como working si así lo pedimos (latencia de registro)
                    if (!o.Canceled && (st == OrderStatus.Placed || st == OrderStatus.PartlyFilled || (includeNone && st == OrderStatus.None)))
                        return true;
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"HasLiveRmBrackets EX: {ex.Message}");
            }
            return false;
        }

        private void LogOrderStateHistogram(string tag)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;
                int slPlaced = 0, slPart = 0, tpPlaced = 0, tpPart = 0;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    var st = o.Status();
                    bool isSL = c.StartsWith(OwnerPrefix + "SL:");
                    bool isTP = c.StartsWith(OwnerPrefix + "TP:");
                    if (isSL && st == OrderStatus.Placed) slPlaced++;
                    else if (isSL && st == OrderStatus.PartlyFilled) slPart++;
                    else if (isTP && st == OrderStatus.Placed) tpPlaced++;
                    else if (isTP && st == OrderStatus.PartlyFilled) tpPart++;
                }
                if (EnableLogging)
                    DebugLog.W("RM/STATES", $"{tag}: SL(placed={slPlaced}, partly={slPart}) TP(placed={tpPlaced}, partly={tpPart})");
            }
            catch { }
        }

        private void CancelResidualBrackets(string reason)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;
                int canceled = 0;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!(c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:"))) continue;
                    var st = o.Status();
                    if (o.Canceled || st == OrderStatus.Canceled || st == OrderStatus.Filled) continue;
                    try { CancelOrder(o); canceled++; } catch { }
                }
                if (EnableLogging) DebugLog.W("RM/CLEAN", $"Canceled residual RM brackets (n={canceled}) reason='{reason}'");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"CancelResidualBrackets EX: {ex.Message}");
            }
        }

        private void CancelNonBracketWorkingOrders(string reason)
        {
            try
            {
                // UniÃƒÂ³n: ÃƒÂ³rdenes de la estrategia + externas detectadas (ChartTrader)
                var union = new System.Collections.Generic.List<Order>();
                if (this.Orders != null) union.AddRange(this.Orders);
                lock (_liveOrdersLock) union.AddRange(_liveOrders);

                var seen = new System.Collections.Generic.HashSet<string>();
                int canceled = 0, considered = 0;
                foreach (var o in union)
                {
                    if (o == null) continue;
                    // Mismo instrumento/portfolio (comparaciÃƒÂ³n laxa por ToString para evitar tipos internos)
                    if (o.Security?.ToString() != Security?.ToString()) continue;
                    if (o.Portfolio?.ToString() != Portfolio?.ToString()) continue;
                    var oid = o.Id ?? $"{o.GetHashCode()}";
                    if (!seen.Add(oid)) continue;

                    var c  = o.Comment ?? "";
                    var st = o.Status();
                    var isBracket = c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:");
                    var isMyFlat  = c.StartsWith(OwnerPrefix + "STPFLAT:");
                    var isLive    = !o.Canceled && st != OrderStatus.Canceled && st != OrderStatus.Filled; // inclusivo: None/Placed/PartlyFilled
                    if (EnableLogging)
                        DebugLog.W("RM/CLEAN", $"consider cancel: id={oid} c='{c}' st={st} canceled={o.Canceled} isBracket={isBracket} isMyFlat={isMyFlat} isLive={isLive}");

                    if (isBracket || isMyFlat) continue; // brackets ya se limpian; no matar STPFLAT
                    considered++;
                    if (isLive)
                    {
                        if (TryCancelAnyOrder(o)) canceled++;
                    }
                }
                if (EnableLogging) DebugLog.W("RM/CLEAN", $"Canceled non-bracket working orders (n={canceled}/{considered}) reason='{reason}'");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"CancelNonBracketWorkingOrders EX: {ex.Message}");
            }
        }

        // Try to cancel with both Strategy API and TradingManager (external orders)
        private bool TryCancelAnyOrder(Order o)
        {
            try
            {
                // 1) Strategy-owned way (works for this strategy orders)
                try { CancelOrder(o); return true; } catch { /* might not belong to strategy */ }

                // 2) TradingManager (platform-level) Ã¢â‚¬â€ sync variant
                var tm = this.TradingManager;
                if (tm != null)
                {
                    var mi = tm.GetType().GetMethod("CancelOrder", new[] { typeof(Order), typeof(bool), typeof(bool) });
                    if (mi != null)
                    {
                        mi.Invoke(tm, new object[] { o, false, false });
                        return true;
                    }
                    // 2b) Async variant
                    var mia = tm.GetType().GetMethod("CancelOrderAsync", new[] { typeof(Order), typeof(bool), typeof(bool) });
                    if (mia != null)
                    {
                        var task = (System.Threading.Tasks.Task)mia.Invoke(tm, new object[] { o, false, false });
                        // fire-and-forget; assume submitted
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"TryCancelAnyOrder EX: {ex.Message}");
            }
            return false;
        }

        // === Additional order options via TradingManager ===
        private void TrySetReduceOnly(Order order)
        {
            try
            {
                var flags = TradingManager?
                    .GetSecurityTradingOptions()?
                    .CreateExtendedOptions(order.Type);
                if (flags is ATAS.DataFeedsCore.IOrderOptionReduceOnly ro)
                {
                    ro.ReduceOnly = true;   // evita abrir posiciÃƒÂ³n nueva
                    order.ExtendedOptions = flags;
                }
            } catch { }
        }

        // Flatten por TradingManager (cascada de firmas). Devuelve true si se invocÃƒÂ³ alguna variante.
        private bool TryClosePositionViaTradingManager()
        {
            try
            {
                var tm = this.TradingManager;
                if (tm == null)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", "TradingManager null Ã¢â€ â€™ fallback MARKET");
                    return false;
                }

                var tmt = tm.GetType();

                // A) Con Position del TM (si existe)
                object posObj = null;
                try { posObj = tmt.GetProperty("Position")?.GetValue(tm); } catch { /* ignore */ }
                if (posObj == null)
                {
                    // Fallback: intentar obtener Position desde Portfolio.GetPosition(Security)
                    posObj = GetAtasPositionObject();
                }

                // 1) ClosePosition(Position, bool, bool)
                if (posObj != null)
                {
                    var mi1 = tmt.GetMethod("ClosePosition", new[] { posObj.GetType(), typeof(bool), typeof(bool) });
                    if (mi1 != null)
                    {
                        var ret = mi1.Invoke(tm, new object[] { posObj, false, true });
                        if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Position,false,true) invoked");
                        return (ret as bool?) ?? true;
                    }

                    // 2) ClosePositionAsync(Position, bool, bool)
                    var mi2 = tmt.GetMethod("ClosePositionAsync", new[] { posObj.GetType(), typeof(bool), typeof(bool) });
                    if (mi2 != null)
                    {
                        try
                        {
                            var task = mi2.Invoke(tm, new object[] { posObj, false, true }) as System.Threading.Tasks.Task;
                            if (EnableLogging) DebugLog.W("RM/STOP", "ClosePositionAsync(Position,false,true) invoked");
                            return true; // asumimos submit correcto
                        }
                        catch (Exception exa)
                        {
                            if (EnableLogging) DebugLog.W("RM/STOP", $"ClosePositionAsync EX: {exa.Message}");
                        }
                    }
                }

                // 3) ClosePosition(Portfolio, Security, bool, bool)
                var mi3 = tmt.GetMethod("ClosePosition", new[] { Portfolio?.GetType(), Security?.GetType(), typeof(bool), typeof(bool) });
                if (mi3 != null)
                {
                    mi3.Invoke(tm, new object[] { Portfolio, Security, true, true });
                    if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Portfolio,Security,true,true) invoked");
                    return true;
                }

                // 4) ClosePosition(Portfolio, Security)
                var mi4 = tmt.GetMethod("ClosePosition", new[] { Portfolio?.GetType(), Security?.GetType() });
                if (mi4 != null)
                {
                    mi4.Invoke(tm, new object[] { Portfolio, Security });
                    if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Portfolio,Security) invoked");
                    return true;
                }

                if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition* not found Ã¢â€ â€™ fallback MARKET");
                return false;
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/STOP", $"TryClosePositionViaTradingManager EX: {ex.Message} Ã¢â€ â€™ fallback MARKET");
                return false;
            }
        }

        private void TrySetCloseOnTrigger(Order order)
        {
            try
            {
                var flags = TradingManager?
                    .GetSecurityTradingOptions()?
                    .CreateExtendedOptions(order.Type);
                if (flags is ATAS.DataFeedsCore.IOrderOptionCloseOnTrigger ct)
                {
                    ct.CloseOnTrigger = true; // cerrar cuando dispare
                    order.ExtendedOptions = flags;
                }
            } catch { }
        }

        private bool HasLiveOrdersWithPrefix(string prefix)
        {
            try
            {
                var list = this.Orders; // Strategy.Orders
                if (list == null) return false;
                return list.Any(o =>
                {
                    var c = o?.Comment ?? "";
                    if (!c.StartsWith(prefix)) return false;
                    // consideramos viva si NO estÃƒÂ¡ cancelada y NO estÃƒÂ¡ llena
                    var st = o.Status();
                    return !o.Canceled
                           && st != OrderStatus.Filled
                           && st != OrderStatus.Canceled;
                });
            }
            catch { return false; }
        }

        // Log de cancelaciÃƒÂ³n fallida (para ver por quÃƒÂ© queda algo "working")
        protected override void OnOrderCancelFailed(Order order, string message)
        {
            if (!EnableLogging) return;
            try
            {
                DebugLog.W("RM/STOP", $"OnOrderCancelFailed: id={order?.Id} comment='{order?.Comment}' status={order?.Status()} msg={message}");
            } catch { }
        }

        // ========= Helpers de neta y flatten =========
        // Siempre usa el snapshot de CUENTA (TM.Position/Portfolio). No uses CurrentPosition aquÃƒÂ­.
        private int ReadNetPositionSafe()
        {
            try { return ReadNetPosition(); } catch { return 0; }
        }

        private bool HasWorkingOrdersWithPrefix(string prefix)
        {
            try
            {
                var list = this.Orders; if (list == null) return false;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!c.StartsWith(prefix)) continue;
                    var st = o.Status();
                    // "working" en ATAS = Placed o PartlyFilled (evitamos None/Filled/Canceled)
                    if (!o.Canceled && (st == OrderStatus.Placed || st == OrderStatus.PartlyFilled))
                        return true;
                }
            } catch { }
            return false;
        }

        // EnvÃƒÂ­a (si hace falta) la orden MARKET reduce-only para quedar flat.
        // Evita duplicarla si ya hay una STPFLAT "working".
        private void EnsureFlattenOutstanding(string reason)
        {
            try
            {
                var net = ReadNetPositionSafe();
                if (net == 0)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding: already flat ({reason})");
                    return;
                }
                if (HasWorkingOrdersWithPrefix(OwnerPrefix + "STPFLAT:"))
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding: STPFLAT already working ({reason})");
                    return;
                }
                var side = net > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                var qty  = Math.Abs(net);
                var comment = $"{OwnerPrefix}STPFLAT:{Guid.NewGuid():N}";
                var o = new Order
                {
                    Portfolio      = Portfolio,
                    Security       = Security,
                    Direction      = side,
                    Type           = OrderTypes.Market,
                    QuantityToFill = qty,
                    Comment        = comment
                };
                TrySetReduceOnly(o); // evita abrir si hay desincronizaciÃƒÂ³n
                OpenOrder(o);
                if (EnableLogging) DebugLog.W("RM/STOP", $"Flatten MARKET sent: {side} {qty} ({reason}) comment={comment}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding EX: {ex.Message}");
            }
        }

        // ====================== Session P&L Tracking ======================

        private void UpdateSessionPnL()
        {
            try
            {
                // Usar nuestras variables de tracking internas en lugar de ReadPositionSnapshot()
                // porque ReadPositionSnapshot() puede devolver 0 en replay/simulación
                var currentQty = _currentPositionQty;
                var avgPrice = _currentPositionEntryPrice;

                // Calcular P&L no realizado de posición abierta
                decimal unrealizedPnL = 0m;
                if (currentQty != 0 && avgPrice > 0m)
                {
                    var lastPrice = GetLastPriceSafe();
                    var tickValue = ResolveTickValueUSD();
                    var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);

                    // P&L = (LastPrice - AvgPrice) × Qty × (TickValue / TickSize)
                    // Para SHORT: P&L = (AvgPrice - LastPrice) × Qty × (TickValue / TickSize)
                    var priceDiff = currentQty > 0 ? (lastPrice - avgPrice) : (avgPrice - lastPrice);
                    var ticks = priceDiff / tickSize;
                    unrealizedPnL = ticks * tickValue * Math.Abs(currentQty);

                    if (EnableLogging && Math.Abs(unrealizedPnL) > 0.01m)
                        DebugLog.W("RM/PNL", $"Unrealized calc: currentQty={currentQty} entry={avgPrice:F2} last={lastPrice:F2} priceDiff={priceDiff:F2} ticks={ticks:F2} tickVal={tickValue:F2} → unrealized={unrealizedPnL:F2}");
                }

                // Session P&L = Realized + Unrealized
                SessionPnL = _sessionRealizedPnL + unrealizedPnL;

                if (EnableLogging && (Math.Abs(SessionPnL) > 0.01m || Math.Abs(unrealizedPnL) > 0.01m))
                    DebugLog.W("RM/PNL", $"SessionPnL={SessionPnL:F2} (Realized={_sessionRealizedPnL:F2} Unrealized={unrealizedPnL:F2})");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"UpdateSessionPnL EX: {ex.Message}");
            }
        }

        private void TrackPositionClose(decimal exitPrice, int qty, int direction)
        {
            try
            {
                if (_currentPositionQty == 0 || _currentPositionEntryPrice == 0m)
                {
                    if (EnableLogging) DebugLog.W("RM/PNL", "TrackPositionClose: No position tracked to close");
                    return;
                }

                var tickValue = ResolveTickValueUSD();
                var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);

                // Calcular P&L de la posición cerrada
                // Para LONG: P&L = (ExitPrice - EntryPrice) × Qty × (TickValue / TickSize)
                // Para SHORT: P&L = (EntryPrice - ExitPrice) × Qty × (TickValue / TickSize)
                var priceDiff = direction > 0 ? (exitPrice - _currentPositionEntryPrice) : (_currentPositionEntryPrice - exitPrice);
                var ticks = priceDiff / tickSize;
                var tradePnL = ticks * tickValue * Math.Abs(qty);

                _sessionRealizedPnL += tradePnL;

                if (EnableLogging)
                    DebugLog.W("RM/PNL", $"Position close: Entry={_currentPositionEntryPrice:F2} Exit={exitPrice:F2} Qty={qty} Dir={direction} → P&L={tradePnL:F2} (Total Realized={_sessionRealizedPnL:F2})");

                // Actualizar posición actual
                _currentPositionQty = Math.Abs(_currentPositionQty) - Math.Abs(qty);
                if (_currentPositionQty == 0)
                {
                    _currentPositionEntryPrice = 0m;
                    if (EnableLogging) DebugLog.W("RM/PNL", "Position fully closed");
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"TrackPositionClose EX: {ex.Message}");
            }
        }

        private void TrackPositionEntry(decimal entryPrice, int qty, int direction)
        {
            try
            {
                if (_currentPositionQty == 0)
                {
                    // Nueva posición
                    _currentPositionEntryPrice = entryPrice;
                    _currentPositionQty = qty * direction;  // signed: +LONG / -SHORT
                    if (EnableLogging)
                        DebugLog.W("RM/PNL", $"Position entry: Price={entryPrice:F2} Qty={qty} Dir={direction} → Tracking started");
                }
                else
                {
                    // Incremento de posición existente (promedio ponderado)
                    var totalQty = Math.Abs(_currentPositionQty) + Math.Abs(qty);
                    _currentPositionEntryPrice = (_currentPositionEntryPrice * Math.Abs(_currentPositionQty) + entryPrice * Math.Abs(qty)) / totalQty;
                    _currentPositionQty = totalQty * direction;
                    if (EnableLogging)
                        DebugLog.W("RM/PNL", $"Position add: NewEntry={entryPrice:F2} Qty={qty} → AvgEntry={_currentPositionEntryPrice:F2} TotalQty={totalQty}");
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"TrackPositionEntry EX: {ex.Message}");
            }
        }

    }
}