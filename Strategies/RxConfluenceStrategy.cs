// =====compliar==== 
//PS C:\Users\AlexJ\Desktop\MyAtasIndicator> dotnet clean
//>> dotnet build -c Release -f net8.0-windows>>  
//>> Copy-Item ".\bin\Release\net8.0-windows\MyAtasIndicator.dll" "$env:APPDATA\ATAS\Indicators\" -Force
//>> Copy-Item ".\bin\Release\net8.0-windows\MyAtasIndicator.dll" "$env:APPDATA\ATAS\Strategies\" -Force
//>> 
//

using System;
using System.Diagnostics;
using ATAS.Strategies.Chart;
using ATAS.Types;

namespace MyAtasIndicator.Strategies
{
    public class RxConfluenceStrategy : ChartStrategy
    {
        // ===== Parámetros (públicos, sin atributos) =====
        public int    RiskTicks       { get; set; } = 40;   // SL = 40 ticks
        public int    Tp2Ticks        { get; set; } = 120;  // TP total = 120 ticks
        public bool   UseDeltaFilter  { get; set; } = true;
        public decimal MinAbsorption  { get; set; } = 100m;
        public bool   AutoTrade       { get; set; } = false; // desactivado por defecto
        public int    Quantity        { get; set; } = 2;

        // ===== Estado interno =====
        private decimal _tick = 0m;

        private bool _armed     = false; // listo tras flip
        private bool _flipBull  = false; // dirección del flip
        private int  _flipBar   = -1;    // índice barra flip

        private decimal _trigger = 0m;
        private decimal _sl      = 0m;
        private decimal _tp1     = 0m;
        private decimal _tp2     = 0m;

        private bool _hasEntry = false;
        private bool _tp1Hit   = false;

        public RxConfluenceStrategy()
        {
            Name = "Rx Confluence Strategy";
        }

        protected override void OnInitialize()
        {
            _tick = InstrumentInfo?.TickSize ?? 0m;
        }

        protected override void OnRecalculate()
        {
            _armed = false;
            _hasEntry = false;
            _tp1Hit = false;
            _flipBar = -1;
            _trigger = _sl = _tp1 = _tp2 = 0m;
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (_tick <= 0 || bar < 2)
                return;

            // procesamos sólo al cerrar la barra previa
            if (bar != CurrentBar - 1)
                return;

            var c = GetCandle(bar);
            var p = GetCandle(bar - 1);
            if (c == null || p == null)
                return;

            // (1) FLIP: cambio de sesgo + ruptura del extremo previo (idea del 468)
            bool upNow   = c.Close >= c.Open;
            bool upPrev  = p.Close >= p.Open;
            bool downNow = c.Close <  c.Open;
            bool downPrev= p.Close <  p.Open;

            bool flipToUp   = upNow && downPrev && c.Close > p.High;
            bool flipToDown = downNow && upPrev && c.Close < p.Low;

            if (flipToUp || flipToDown)
            {
                _flipBull = flipToUp;
                _flipBar  = bar;
                _armed    = true;
                _hasEntry = false;
                _tp1Hit   = false;

                if (_flipBull)
                {
                    _trigger = RoundToTick(c.High + _tick);
                    _sl      = RoundToTick(c.Low  - RiskTicks * _tick);
                    _tp2     = RoundToTick(_trigger + Tp2Ticks * _tick);
                }
                else
                {
                    _trigger = RoundToTick(c.Low  - _tick);
                    _sl      = RoundToTick(c.High + RiskTicks * _tick);
                    _tp2     = RoundToTick(_trigger - Tp2Ticks * _tick);
                }

                _tp1 = RoundToTick(_flipBull
                    ? (_trigger + (Tp2Ticks / 2m) * _tick)
                    : (_trigger - (Tp2Ticks / 2m) * _tick));

                // Marca “triángulo” (placeholder visual; aquí sólo log)
                DrawTriangle(bar, _flipBull ? c.Low - 1 * _tick : c.High + 1 * _tick, _flipBull);

                // Nota (placeholder visual; aquí sólo log)
                WriteNote(bar, _flipBull
                    ? $"Flip▲  Trg:{_trigger:F5}  SL:{_sl:F5}  TP1:{_tp1:F5}  TP2:{_tp2:F5}"
                    : $"Flip▼  Trg:{_trigger:F5}  SL:{_sl:F5}  TP1:{_tp1:F5}  TP2:{_tp2:F5}");

                return;
            }

            // (2) Entrada: ruptura del trigger + (opcional) delta/absorción a favor
            if (_armed && !_hasEntry)
            {
                bool broke =
                    (_flipBull && c.High >= _trigger) ||
                    (!_flipBull && c.Low  <= _trigger);

                if (broke)
                {
                    bool passAbsorption = true;
                    if (UseDeltaFilter)
                    {
                        // si tu build no expone Delta, pon UseDeltaFilter=false en el panel
                        decimal delta = c.Delta;
                        passAbsorption = _flipBull ? (delta >= MinAbsorption) : (delta <= -MinAbsorption);
                    }

                    if (passAbsorption)
                    {
                        _hasEntry = true;

                        // Marca “flecha” en la vela de entrada (placeholder)
                        DrawArrow(bar, _flipBull ? c.Low - 1 * _tick : c.High + 1 * _tick, _flipBull);

                        if (AutoTrade && Quantity > 0)
                            TryPlaceBracket(_flipBull, Quantity); // plantilla
                    }
                }
            }

            // (3) Gestión virtual para anotar TP/SL alcanzados
            if (_hasEntry)
            {
                if (!_tp1Hit)
                {
                    bool hitTp1 = _flipBull ? c.High >= _tp1 : c.Low <= _tp1;
                    if (hitTp1)
                    {
                        _tp1Hit = true;
                        WriteNote(bar, "TP1 hit → SL a BE");
                    }
                }

                bool hitTp2 = _flipBull ? c.High >= _tp2 : c.Low <= _tp2;
                bool hitSl  = _flipBull ? c.Low  <= _sl  : c.High >= _sl;

                if (hitTp2)
                {
                    WriteNote(bar, "TP2 hit ✔");
                    ResetStateAfterExit();
                }
                else if (hitSl)
                {
                    WriteNote(bar, "SL hit ✖");
                    ResetStateAfterExit();
                }
            }
        }

        private decimal RoundToTick(decimal price)
            => _tick <= 0m ? price
                           : Math.Round(price / _tick, MidpointRounding.AwayFromZero) * _tick;

        private void ResetStateAfterExit()
        {
            _armed = false;
            _hasEntry = false;
            _tp1Hit = false;
            _flipBar = -1;
            _trigger = _sl = _tp1 = _tp2 = 0m;
        }

        // ===== “Dibujo” placeholder: ahora mismo sólo escribe al Output/Log de depuración =====
        private void DrawTriangle(int bar, decimal price, bool bull)
            => Debug.WriteLine($"[RX] ▲▼ triangle {(bull ? "UP" : "DOWN")} @bar {bar} price {price:F5}");

        private void DrawArrow(int bar, decimal price, bool bull)
            => Debug.WriteLine($"[RX] ↗↘ arrow {(bull ? "UP" : "DOWN")} @bar {bar} price {price:F5}");

        private void WriteNote(int bar, string note)
            => Debug.WriteLine($"[RX] note @bar {bar}: {note}");

        // ===== Órdenes: plantilla no operativa (evita BuyMarket/SellMarket inexistentes en tu build) =====
        private void TryPlaceBracket(bool bull, int qty)
        {
            // Aquí iría la llamada real a tus APIs de órdenes si tu ChartStrategy las expone en tu versión.
            Debug.WriteLine($"[RX] AutoTrade (demo) → {(bull ? "BUY" : "SELL")} qty {qty} @trigger {_trigger:F5} SL {_sl:F5} TP1 {_tp1:F5} TP2 {_tp2:F5}");
        }
    }
}
