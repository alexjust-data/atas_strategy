using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.Types;
using MyAtas.Shared;
using MyAtas.Indicators;

namespace MyAtas.Strategies
{
    // Señales GL, captura en N, validación N+1, confluencias y tolerancias.
    public partial class FourSixEightSimpleStrategy
    {
        // ====================== SIGNAL CAPTURE & EXECUTION LOGIC ======================
        /// <summary>
        /// Captura en N (cruce GL confirmado en cierre) y ejecuta en N+1 con confluencias.
        /// Llama a esto desde OnCalculate(bar, value) si quieres "adelgazar" ese método.
        /// </summary>
        private void ProcessSignalLogic(int bar)
        {
            var sig = _ind?.LastSignal;
            if (sig.HasValue)
            {
                DebugLog.W("468/STR",
                    $"SIGNAL_CHECK: bar={bar} sigBar={sig.Value.BarId} dir={(sig.Value.Dir > 0 ? "BUY" : "SELL")} " +
                    $"uid={sig.Value.Uid.ToString().Substring(0, 8)} lastUid={(_lastUid != Guid.Empty ? _lastUid.ToString().Substring(0, 8) : "NONE")} " +
                    $"condition_barMatch={sig.Value.BarId == bar} condition_uidNew={sig.Value.Uid != _lastUid}");
            }

            // 1) CAPTURE AT N (GL-cross en ESTA vela) + confirmación por cierre
            if (sig.HasValue && sig.Value.BarId == bar && sig.Value.Uid != _lastUid)
            {
                bool closeConfirmed = ValidateCloseConfirmation(sig.Value, bar);
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

            // 2) EXECUTE EXACTLY AT N+1 — ventana (armar/ejecutar/expirar)
            if (_pending.HasValue)
            {
                ProcessPendingExecution(bar);
            }
        }

        private bool ValidateCloseConfirmation(Signal468 signal, int bar)
        {
            bool closeConfirmed = true;

            if (ValidateGenialCrossLocally && bar >= 1)
            {
                var cN  = GetCandle(bar).Close;
                var gN  = GenialAt(bar);
                var cN1 = GetCandle(bar - 1).Close;
                var gN1 = GenialAt(bar - 1);
                var eps = Ticks(Math.Max(0, HysteresisTicks));

                // Histeresis en N y N-1
                if (signal.Dir > 0) // BUY
                    closeConfirmed = (cN > gN + eps) && (cN1 <= gN1 + eps);
                else                // SELL
                    closeConfirmed = (cN < gN - eps) && (cN1 >= gN1 - eps);
            }
            return closeConfirmed;
        }

        private void ProcessPendingExecution(int bar)
        {
            var execBar = _pending.Value.BarId + 1;
            if (bar < execBar)
            {
                if ((bar % 50) == 0)
                    DebugLog.W("468/STR", $"PENDING ARMED: now={bar}, execBar={execBar}");
                return;
            }
            if (bar > execBar)
            {
                DebugLog.W("468/STR", $"PENDING EXPIRED: now={bar}, execBar={execBar}");
                _pending = null;
                return;
            }

            // bar == execBar → ejecutar en N+1
            DebugLog.W("468/STR", $"PROCESSING PENDING @N+1: bar={bar}, execBar={execBar}");
            var s = _pending.Value;

            // anti-dup por UID
            if (s.Uid == _lastExecUid)
            {
                _pending = null;
                DebugLog.W("468/STR", "SKIP: Already executed this UID");
                return;
            }

            int dir = s.Dir;
            int qty = Math.Max(1, Quantity);

            // Validaciones de N+1
            if (!ValidateN1Execution(bar, s, dir))
            {
                _pending = null;
                return;
            }

            // Ejecutar
            ExecuteEntry(dir, qty, bar, s.BarId);
        }

        private bool ValidateN1Execution(int bar, Pending signal, int dir)
        {
            if (!ValidateStrictN1Open(bar))
                return false;

            if (!ValidateSignalCandleDirection(signal, dir))
                return false;

            if (!ValidateGenialSlopeConfluence(dir, bar))
                return false;

            if (!ValidateEmaVsWilderConfluence(dir, bar))
                return false;

            if (!ValidateOnlyOnePosition(bar))
                return false;

            return true;
        }

        private bool ValidateStrictN1Open(int bar)
        {
            if (StrictN1Open)
            {
                if (!IsFirstTickOf(bar))
                {
                    var openN1 = GetCandle(bar).Open;
                    var lastPx = GetCandle(bar).Close;
                    var tol    = Ticks(Math.Max(0, OpenToleranceTicks));
                    if (Math.Abs(lastPx - openN1) > tol)
                    {
                        DebugLog.W("468/STR", $"EXPIRE: missed first tick and |{lastPx-openN1}| > {tol}");
                        return false;
                    }
                    DebugLog.W("468/STR", $"First-tick missed but within tolerance ({lastPx}~{openN1}) -> proceed");
                }
            }
            return true;
        }

        private bool ValidateSignalCandleDirection(Pending signal, int dir)
        {
            var sigCandle = GetCandle(signal.BarId);
            bool candleDirOk = dir > 0 ? (sigCandle.Close > sigCandle.Open)
                                       : (sigCandle.Close < sigCandle.Open);
            if (!candleDirOk)
            {
                DebugLog.W("468/STR", "ABORT ENTRY: Candle direction at N does not match signal");
                return false;
            }
            return true;
        }

        private bool ValidateGenialSlopeConfluence(int dir, int bar)
        {
            if (RequireGenialSlope)
            {
                // Usa el CheckGenialSlope que ya tienes en la clase principal
                bool glOk = CheckGenialSlope(dir, bar);  // ya existe
                if (!glOk)
                {
                    DebugLog.W("468/STR", "ABORT ENTRY: Conf#1 failed");
                    return false;
                }
            }
            return true;
        }

        private bool ValidateEmaVsWilderConfluence(int dir, int bar)
        {
            if (RequireEmaVsWilder)
            {
                bool emaOk = CheckEmaVsWilderAtExec(dir, bar); // ya existe
                if (!emaOk)
                {
                    DebugLog.W("468/STR", "ABORT ENTRY: Conf#2 failed");
                    return false;
                }
            }
            return true;
        }

        private bool ValidateOnlyOnePosition(int bar)
        {
            if (OnlyOnePosition)
            {
                // *** PHANTOM FIX: Verificar broker como autoridad máxima ***
                int portfolioNet = TryGetPositionFromPortfolio();
                int positionsNet = TryGetPositionFromPositions();
                int net = GetNetPosition(); // este puede estar contaminado por phantom
                int fillsSum = NetByFills();
                bool live = HasAnyActiveOrders();
                bool inCooldown = EnableCooldown && CooldownBars > 0 && _cooldownUntilBar >= 0 && bar <= _cooldownUntilBar;

                DebugLog.W("468/STR",
                    $"GUARD OnlyOnePosition: portfolio={portfolioNet} positions={positionsNet} net={net} fills={fillsSum} " +
                    $"active={_tradeActive} live={live} cooldown={(inCooldown ? $"YES(until={_cooldownUntilBar})" : "NO")}");

                // *** PHANTOM FIX: Si broker dice 0 y no hay órdenes vivas, NO bloquees por residuos en fills ***
                if (portfolioNet == 0 && positionsNet == 0 && !live)
                {
                    if (net != 0 || fillsSum != 0)
                    {
                        DebugLog.W("468/STR", $"PHANTOM RECONCILE: broker=0, live=NO, net={net}, fillsSum={fillsSum} → forcing cleanup");
                        _orderFills.Clear();
                        _childSign.Clear();
                        _cachedNetPosition = 0;
                    }
                    DebugLog.W("468/STR", "GUARD OnlyOnePosition: PASS (flat by broker, no live orders)");
                    return true;
                }

                // Si net=0 pero hay órdenes activas "zombie", CANCELA en broker
                if (portfolioNet == 0 && positionsNet == 0 && live)
                {
                    DebugLog.W("468/STR", "ZOMBIE CANCEL: broker=0 but live orders present -> cancelling...");
                    CancelAllLiveActiveOrders();
                    DebugLog.W("468/STR", "RETRY NEXT TICK: keeping pending for re-check after zombie cancel");
                    return false; // reintenta en el próximo tick
                }

                // Caso normal: bloquea si realmente hay net ≠ 0 o hay actividad
                bool busy = _tradeActive || portfolioNet != 0 || positionsNet != 0 || live || inCooldown;
                if (busy)
                {
                    // Detailed block reason for debugging
                    var reasons = new List<string>();
                    if (_tradeActive) reasons.Add("tradeActive");
                    if (portfolioNet != 0) reasons.Add($"portfolio={portfolioNet}");
                    if (positionsNet != 0) reasons.Add($"positions={positionsNet}");
                    if (live) reasons.Add($"liveOrders={CountActiveOrders()}");
                    if (inCooldown) reasons.Add($"cooldown(until={_cooldownUntilBar})");

                    DebugLog.W("468/STR", $"ABORT ENTRY: OnlyOnePosition guard BLOCKED by: {string.Join(", ", reasons)}");
                    return false;
                }

                DebugLog.W("468/STR", "GUARD OnlyOnePosition: PASS");
            }
            return true;
        }

        private void ExecuteEntry(int dir, int qty, int bar, int signalBar)
        {
            _lastExecUid = _pending.Value.Uid;
            _pending = null;

            // === Position Sizing: Calcular quantity basado en riesgo ===
            // Necesitamos entry price y stop price para calcular
            var entryPx = GetCandle(bar).Open; // Entry en apertura de N+1
            var (slPx, tpList) = BuildBracketPrices(dir, signalBar, bar);

            // Calcular quantity usando Position Sizing
            var calculatedQty = CalculatePositionSize(entryPx, slPx, dir);

            // Usar el qty calculado (overridea el qty que venía del parámetro)
            qty = calculatedQty;

            if (EnableDetailedRiskLogging)
                DebugLog.W("468/SIZE", $"ExecuteEntry: mode={PositionSizingMode} entryPx={entryPx:F2} slPx={slPx:F2} dir={dir} → qty={qty}");

            DebugLog.Critical("468/ORD",
                $"ENTRY SENT: dir={(dir>0?"Buy":"Sell")} qty={qty} bar={bar} (signal N={signalBar})");

            SubmitMarket(dir, qty, bar, signalBar); // está en Execution.cs
            _tradeActive = true;

            // armar cooldown igual que haces en OnCalculate/OnOrderChanged
            if (EnableCooldown && CooldownBars > 0)
                _cooldownUntilBar = bar + Math.Max(1, CooldownBars); // ← sin SetCooldownUntil
        }
    }
}