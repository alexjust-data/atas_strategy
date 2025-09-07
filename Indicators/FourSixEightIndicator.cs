using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Reflection;
using ATAS.Indicators;
using ATAS.Types;
using MyAtasIndicator.Shared;
using DColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace MyAtasIndicator.Indicators
{
    [DisplayName("04 + 06 + 08 (ATAS) - Minimal")]
    public class FourSixEightIndicator : Indicator
    {
        // ============================== PARÁMETROS ==============================
        // Colores para el modo bicolor de GenialLine
        private MediaColor _genialUpColor = MediaColor.FromArgb(255, 0, 170, 0);   // verde por defecto
        private MediaColor _genialDownColor = MediaColor.FromArgb(255, 210, 0, 0); // rojo por defecto

        [Category("Genial Line"), DisplayName("Bicolor: color SUBE")]
        public MediaColor GenialUpColor
        {
            get => _genialUpColor;
            set { if (_genialUpColor.Equals(value)) return; _genialUpColor = value; RecalculateValues(); }
        }

        [Category("Genial Line"), DisplayName("Bicolor: color BAJA")]
        public MediaColor GenialDownColor
        {
            get => _genialDownColor;
            set { if (_genialDownColor.Equals(value)) return; _genialDownColor = value; RecalculateValues(); }
        }


        // --- Zona de corrección (EMA8 vs Wilder8) ---
        private int _ema8Len = 8;
        [Category("Drawing"), DisplayName("EMA 8")]
        public int Ema8Len { get => _ema8Len; set { var v = Math.Max(1, value); if (_ema8Len == v) return; _ema8Len = v; RecalculateValues(); } }

        // === EMA8: color fijo y bicolor ===
        private MediaColor _ema8Color = MediaColor.FromArgb(255, 220, 0, 0);
        [Category("Drawing"), DisplayName("Color EMA 8 (fijo)")]
        public MediaColor Ema8Color
        {
            get => _ema8Color;
            set { if (_ema8Color.Equals(value)) return; _ema8Color = value; _ema8.Color = _ema8Color; RecalculateValues(); }
        }

        private bool _ema8AutoBicolor = false;
        [Category("Drawing"), DisplayName("EMA 8 • Bicolor automático (sube/baja)")]
        public bool Ema8AutoBicolor
        {
            get => _ema8AutoBicolor;
            set { if (_ema8AutoBicolor == value) return; _ema8AutoBicolor = value; RecalculateValues(); }
        }

        private MediaColor _ema8UpColor   = MediaColor.FromArgb(255, 0, 170, 0);
        private MediaColor _ema8DownColor = MediaColor.FromArgb(255, 210, 0, 0);
        [Category("Drawing"), DisplayName("EMA 8 • Bicolor: color SUBE")]
        public MediaColor Ema8UpColor   { get => _ema8UpColor;   set { if (_ema8UpColor.Equals(value)) return;   _ema8UpColor = value;   RecalculateValues(); } }
        [Category("Drawing"), DisplayName("EMA 8 • Bicolor: color BAJA")]
        public MediaColor Ema8DownColor { get => _ema8DownColor; set { if (_ema8DownColor.Equals(value)) return; _ema8DownColor = value; RecalculateValues(); } }

        // === Wilder8: color fijo y bicolor ===
        private MediaColor _wilder8Color = MediaColor.FromArgb(255, 180, 0, 0);
        [Category("Drawing"), DisplayName("Color Wilder 8 (fijo)")]
        public MediaColor Wilder8Color
        {
            get => _wilder8Color;
            set { if (_wilder8Color.Equals(value)) return; _wilder8Color = value; _wilder8.Color = _wilder8Color; RecalculateValues(); }
        }

        private bool _wilder8AutoBicolor = false;
        [Category("Drawing"), DisplayName("Wilder 8 • Bicolor automático (sube/baja)")]
        public bool Wilder8AutoBicolor
        {
            get => _wilder8AutoBicolor;
            set { if (_wilder8AutoBicolor == value) return; _wilder8AutoBicolor = value; RecalculateValues(); }
        }

        private MediaColor _w8UpColor   = MediaColor.FromArgb(255, 0, 120, 255);
        private MediaColor _w8DownColor = MediaColor.FromArgb(255, 200, 0, 140);
        [Category("Drawing"), DisplayName("Wilder 8 • Bicolor: color SUBE")]
        public MediaColor W8UpColor   { get => _w8UpColor;   set { if (_w8UpColor.Equals(value)) return;   _w8UpColor = value;   RecalculateValues(); } }
        [Category("Drawing"), DisplayName("Wilder 8 • Bicolor: color BAJA")]
        public MediaColor W8DownColor { get => _w8DownColor; set { if (_w8DownColor.Equals(value)) return; _w8DownColor = value; RecalculateValues(); } }


        // --- Genial Line ---
        private int _velasBanda = 20;
        [Category("Genial Line"), DisplayName("Velas Banda")]
        public int VelasBanda { get => _velasBanda; set { var v = Math.Max(1, value); if (_velasBanda == v) return; _velasBanda = v; RecalculateValues(); } }

        private decimal _desviacio = 3.14159265358979m;
        [Category("Genial Line"), DisplayName("Desviación")]
        public decimal Desviacio { get => _desviacio; set { if (_desviacio == value) return; _desviacio = value; RecalculateValues(); } }

        private MediaColor _genialColor = MediaColor.FromArgb(255, 0, 120, 255);
        [Category("Genial Line"), DisplayName("Color GenialLine")]
        public MediaColor GenialColor
        {
            get => _genialColor;
            set
            {
                if (_genialColor.Equals(value)) return;
                _genialColor = value;
                _genial.Color = _genialColor; // aplicar inmediatamente
                RecalculateValues();
            }
        }

        private bool _genialAutoBicolor = false;
        [Category("Genial Line"), DisplayName("Bicolor automático (sube/baja)")]
        public bool GenialAutoBicolor
        {
            get => _genialAutoBicolor;
            set { if (_genialAutoBicolor == value) return; _genialAutoBicolor = value; RecalculateValues(); }
        }

        // --- Tunnel Domenec ---
        private bool _enableTunnels = true;
        [Category("Tunnel Domenec"), DisplayName("Mostrar Túneles (todo)")]
        public bool EnableTunnels { get => _enableTunnels; set { if (_enableTunnels == value) return; _enableTunnels = value; RecalculateValues(); } }

        private bool _showTunnel1 = true, _showTunnel2 = true, _showTunnel3 = true;
        [Category("Tunnel Domenec"), DisplayName("Mostrar Túnel 1 (123-188)")]
        public bool ShowTunnel1 { get => _showTunnel1; set { if (_showTunnel1 == value) return; _showTunnel1 = value; RecalculateValues(); } }
        [Category("Tunnel Domenec"), DisplayName("Mostrar Túnel 2 (416-618)")]
        public bool ShowTunnel2 { get => _showTunnel2; set { if (_showTunnel2 == value) return; _showTunnel2 = value; RecalculateValues(); } }
        [Category("Tunnel Domenec"), DisplayName("Mostrar Túnel 3 (882-1223)")]
        public bool ShowTunnel3 { get => _showTunnel3; set { if (_showTunnel3 == value) return; _showTunnel3 = value; RecalculateValues(); } }

        private int _ema123 = 123, _ema188 = 188, _ema416 = 416, _ema618 = 618, _ema882 = 882, _ema1223 = 1223;
        [Category("Tunnel Domenec"), DisplayName("EMA-123")]  public int Ema123  { get => _ema123;  set { var v = Math.Max(1, value); if (_ema123  == v) return; _ema123  = v; RecalculateValues(); } }
        [Category("Tunnel Domenec"), DisplayName("EMA-188")]  public int Ema188  { get => _ema188;  set { var v = Math.Max(1, value); if (_ema188  == v) return; _ema188  = v; RecalculateValues(); } }
        [Category("Tunnel Domenec"), DisplayName("EMA-416")]  public int Ema416  { get => _ema416;  set { var v = Math.Max(1, value); if (_ema416  == v) return; _ema416  = v; RecalculateValues(); } }
        [Category("Tunnel Domenec"), DisplayName("EMA-618")]  public int Ema618  { get => _ema618;  set { var v = Math.Max(1, value); if (_ema618  == v) return; _ema618  = v; RecalculateValues(); } }
        [Category("Tunnel Domenec"), DisplayName("EMA-882")]  public int Ema882  { get => _ema882;  set { var v = Math.Max(1, value); if (_ema882  == v) return; _ema882  = v; RecalculateValues(); } }
        [Category("Tunnel Domenec"), DisplayName("EMA-1223")] public int Ema1223 { get => _ema1223; set { var v = Math.Max(1, value); if (_ema1223 == v) return; _ema1223 = v; RecalculateValues(); } }

        // --- Divergencias ---
        private int _divLookback = 25;
        [Category("Divergencias"), DisplayName("Periodos Divergencias (N)")]
        public int DivLookback { get => _divLookback; set { var v = Math.Max(1, value); if (_divLookback == v) return; _divLookback = v; RecalculateValues(); } }

        private int _pHL = 8;
        [Category("Divergencias"), DisplayName("Periodo Highest/Lowest (p)")]
        public int PHl { get => _pHL; set { var v = Math.Max(1, value); if (_pHL == v) return; _pHL = v; RecalculateValues(); } }

        private int _q = 3, _r2 = 5;
        [Category("Divergencias"), DisplayName("DEMA Period (q)")] public int Q  { get => _q;  set { var v = Math.Max(1, value); if (_q  == v) return; _q  = v; RecalculateValues(); } }
        [Category("Divergencias"), DisplayName("DEMA Period (r)")] public int R2 { get => _r2; set { var v = Math.Max(1, value); if (_r2 == v) return; _r2 = v; RecalculateValues(); } }

        private bool _showDivergences = true;
        [Category("Divergencias"), DisplayName("Mostrar Divergencias")]
        public bool ShowDivergences { get => _showDivergences; set { if (_showDivergences == value) return; _showDivergences = value; RecalculateValues(); } }

        // --- Barras de color por Williams %R / ADX ---
        private bool _enableCandleColors = true;
        [Category("Barras de color"), DisplayName("Colorear velas (Cambio alcista/bajista + mapa)")]
        public bool EnableCandleColors { get => _enableCandleColors; set { if (_enableCandleColors == value) return; _enableCandleColors = value; RecalculateValues(); } }

        private int _periodoWilliams = 40;
        [Category("Barras de color"), DisplayName("Período Williams %R")]
        public int PeriodoWilliams { get => _periodoWilliams; set { var v = Math.Max(1, value); if (_periodoWilliams == v) return; _periodoWilliams = v; RecalculateValues(); } }

        private int _adxlen = 7;
        [Category("Barras de color"), DisplayName("ADX Smoothing (len)")]
        public int AdxLen { get => _adxlen; set { var v = Math.Max(1, value); if (_adxlen == v) return; _adxlen = v; RecalculateValues(); } }

        // ======== Switches individuales por condición ========
        private bool _enCambioAlcista = true;
        [Category("Barras de color  •  *** ALCISTA ***"), DisplayName("🡹  Cambio ALCISTA")]
        public bool EnCambioAlcista { get => _enCambioAlcista; set { if (_enCambioAlcista == value) return; _enCambioAlcista = value; RecalculateValues(); } }

        private bool _enSubeFuerzaGrandeA = true;
        [Category("Barras de color  •  *** ALCISTA ***"), DisplayName("🡹🡹🡹  Sube - FUERZA GRANDE")]
        public bool EnSubeFuerzaGrandeA { get => _enSubeFuerzaGrandeA; set { if (_enSubeFuerzaGrandeA == value) return; _enSubeFuerzaGrandeA = value; RecalculateValues(); } }

        private bool _enSubeFuerzaMediaA = true;
        [Category("Barras de color  •  *** ALCISTA ***"), DisplayName("🡹🡹  Sube - FUERZA MEDIA")]
        public bool EnSubeFuerzaMediaA { get => _enSubeFuerzaMediaA; set { if (_enSubeFuerzaMediaA == value) return; _enSubeFuerzaMediaA = value; RecalculateValues(); } }

        private bool _enSubeSinFuerzaA = true;
        [Category("Barras de color  •  *** ALCISTA ***"), DisplayName("🡹  Sube - Sin Fuerza")]
        public bool EnSubeSinFuerzaA { get => _enSubeSinFuerzaA; set { if (_enSubeSinFuerzaA == value) return; _enSubeSinFuerzaA = value; RecalculateValues(); } }

        private bool _enBajaCorreccionA = true;
        [Category("Barras de color  •  *** ALCISTA ***"), DisplayName("🡻  Baja - Parada Impulso - CORRECCIÓN")]
        public bool EnBajaCorreccionA { get => _enBajaCorreccionA; set { if (_enBajaCorreccionA == value) return; _enBajaCorreccionA = value; RecalculateValues(); } }

        private bool _enBajaSinFuerzaA = true;
        [Category("Barras de color  •  *** ALCISTA ***"), DisplayName("🡻  Baja - Sin Fuerza")]
        public bool EnBajaSinFuerzaA { get => _enBajaSinFuerzaA; set { if (_enBajaSinFuerzaA == value) return; _enBajaSinFuerzaA = value; RecalculateValues(); } }

        private bool _enCambioBajista = true;
        [Category("Barras de color  •  *** BAJISTA ***"), DisplayName("🡻  Cambio BAJISTA")]
        public bool EnCambioBajista { get => _enCambioBajista; set { if (_enCambioBajista == value) return; _enCambioBajista = value; RecalculateValues(); } }

        private bool _enBajaFuerzaGrandeB = true;
        [Category("Barras de color  •  *** BAJISTA ***"), DisplayName("🡻🡻🡻  Baja - FUERZA GRANDE")]
        public bool EnBajaFuerzaGrandeB { get => _enBajaFuerzaGrandeB; set { if (_enBajaFuerzaGrandeB == value) return; _enBajaFuerzaGrandeB = value; RecalculateValues(); } }

        private bool _enBajaFuerzaMediaB = true;
        [Category("Barras de color  •  *** BAJISTA ***"), DisplayName("🡻🡻  Baja - FUERZA MEDIA")]
        public bool EnBajaFuerzaMediaB { get => _enBajaFuerzaMediaB; set { if (_enBajaFuerzaMediaB == value) return; _enBajaFuerzaMediaB = value; RecalculateValues(); } }

        private bool _enBajaSinFuerzaB = true;
        [Category("Barras de color  •  *** BAJISTA ***"), DisplayName("🡻  Baja - Sin Fuerza")]
        public bool EnBajaSinFuerzaB { get => _enBajaSinFuerzaB; set { if (_enBajaSinFuerzaB == value) return; _enBajaSinFuerzaB = value; RecalculateValues(); } }

        private bool _enSubeCorreccionB = true;
        [Category("Barras de color  •  *** BAJISTA ***"), DisplayName("🡹  Sube - Parada Impulso - CORRECCIÓN")]
        public bool EnSubeCorreccionB { get => _enSubeCorreccionB; set { if (_enSubeCorreccionB == value) return; _enSubeCorreccionB = value; RecalculateValues(); } }

        private bool _enSubeSinFuerzaB = true;
        [Category("Barras de color  •  *** BAJISTA ***"), DisplayName("🡹  Sube - Sin Fuerza")]
        public bool EnSubeSinFuerzaB { get => _enSubeSinFuerzaB; set { if (_enSubeSinFuerzaB == value) return; _enSubeSinFuerzaB = value; RecalculateValues(); } }

        // ======== Colores (usuario) ========
        private MediaColor _colCambioAlcista     = MediaColor.FromArgb(255, 0,   0, 150);
        private MediaColor _colSubeFuerzaGrandeA = MediaColor.FromArgb(255, 70, 100,  70);
        private MediaColor _colSubeFuerzaMediaA  = MediaColor.FromArgb(255,120, 185,  70);
        private MediaColor _colSubeSinFuerzaA    = MediaColor.FromArgb(255, 30, 180, 230);
        private MediaColor _colBajaCorreccionA   = MediaColor.FromArgb(255,255,   0,   0);
        private MediaColor _colBajaSinFuerzaA    = MediaColor.FromArgb(255,255, 250,   0);
        [Category("Barras de color • Colores (ALCISTA)"), DisplayName("🡹  Cambio ALCISTA")] public MediaColor ColCambioAlcista { get => _colCambioAlcista; set { if (_colCambioAlcista.Equals(value)) return; _colCambioAlcista = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (ALCISTA)"), DisplayName("🡹🡹🡹  Sube - FUERZA GRANDE")] public MediaColor ColSubeFuerzaGrandeA { get => _colSubeFuerzaGrandeA; set { if (_colSubeFuerzaGrandeA.Equals(value)) return; _colSubeFuerzaGrandeA = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (ALCISTA)"), DisplayName("🡹🡹  Sube - FUERZA MEDIA")] public MediaColor ColSubeFuerzaMediaA { get => _colSubeFuerzaMediaA; set { if (_colSubeFuerzaMediaA.Equals(value)) return; _colSubeFuerzaMediaA = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (ALCISTA)"), DisplayName("🡹  Sube - Sin Fuerza")] public MediaColor ColSubeSinFuerzaA { get => _colSubeSinFuerzaA; set { if (_colSubeSinFuerzaA.Equals(value)) return; _colSubeSinFuerzaA = value; RecalculateValues(); } }

        private MediaColor _colCambioBajista     = MediaColor.FromArgb(255,255,   0, 255);
        private MediaColor _colBajaFuerzaGrandeB = MediaColor.FromArgb(255,100,  40,  50);
        private MediaColor _colBajaFuerzaMediaB  = MediaColor.FromArgb(255,210,  60, 130);
        private MediaColor _colBajaSinFuerzaB    = MediaColor.FromArgb(255,250, 140,   0);
        private MediaColor _colSubeCorreccionB   = MediaColor.FromArgb(255,  0, 255, 255);
        private MediaColor _colSubeSinFuerzaB    = MediaColor.FromArgb(255,  0, 255,   0);
        [Category("Barras de color • Colores (BAJISTA)"), DisplayName("🡻  Cambio BAJISTA")] public MediaColor ColCambioBajista { get => _colCambioBajista; set { if (_colCambioBajista.Equals(value)) return; _colCambioBajista = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (BAJISTA)"), DisplayName("🡻🡻🡻  Baja - FUERZA GRANDE")] public MediaColor ColBajaFuerzaGrandeB { get => _colBajaFuerzaGrandeB; set { if (_colBajaFuerzaGrandeB.Equals(value)) return; _colBajaFuerzaGrandeB = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (BAJISTA)"), DisplayName("🡻🡻  Baja - FUERZA MEDIA")] public MediaColor ColBajaFuerzaMediaB { get => _colBajaFuerzaMediaB; set { if (_colBajaFuerzaMediaB.Equals(value)) return; _colBajaFuerzaMediaB = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (BAJISTA)"), DisplayName("🡻  Baja - Sin Fuerza")] public MediaColor ColBajaSinFuerzaB { get => _colBajaSinFuerzaB; set { if (_colBajaSinFuerzaB.Equals(value)) return; _colBajaSinFuerzaB = value; RecalculateValues(); } }

        // --- Sombreado por tendencia (GenialLine) ---
        private bool _trendShading = false;
        [Category("Barras de color • GenialLine"), DisplayName("Bicolor por tendencia (claro/oscuro)")]
        public bool TrendShading { get => _trendShading; set { if (_trendShading == value) return; _trendShading = value; RecalculateValues(); } }

        private bool _trendShadingAuto = false;
        [Category("Barras de color • GenialLine"), DisplayName("Derivar tonos automáticamente")]
        public bool TrendShadingAuto { get => _trendShadingAuto; set { if (_trendShadingAuto == value) return; _trendShadingAuto = value; RecalculateValues(); } }

        private MediaColor _upTrendUpBarLight     = MediaColor.FromArgb(255, 120, 210, 120);
        private MediaColor _upTrendDownBarDark    = MediaColor.FromArgb(255,  30, 140,  30);
        private MediaColor _downTrendDownBarLight = MediaColor.FromArgb(255, 240, 110, 110);
        private MediaColor _downTrendUpBarDark    = MediaColor.FromArgb(255, 160,  30,  30);

        [Category("Barras de color • GenialLine"), DisplayName("Alcista: vela UP (claro)")] public MediaColor UpTrendUpBarLight { get => _upTrendUpBarLight; set { if (_upTrendUpBarLight.Equals(value)) return; _upTrendUpBarLight = value; RecalculateValues(); } }
        [Category("Barras de color • GenialLine"), DisplayName("Alcista: vela DOWN (oscuro)")] public MediaColor UpTrendDownBarDark { get => _upTrendDownBarDark; set { if (_upTrendDownBarDark.Equals(value)) return; _upTrendDownBarDark = value; RecalculateValues(); } }
        [Category("Barras de color • GenialLine"), DisplayName("Bajista: vela DOWN (claro)")] public MediaColor DownTrendDownBarLight { get => _downTrendDownBarLight; set { if (_downTrendDownBarLight.Equals(value)) return; _downTrendDownBarLight = value; RecalculateValues(); } }
        [Category("Barras de color • GenialLine"), DisplayName("Bajista: vela UP (oscuro)")] public MediaColor DownTrendUpBarDark { get => _downTrendUpBarDark; set { if (_downTrendUpBarDark.Equals(value)) return; _downTrendUpBarDark = value; RecalculateValues(); } }

        private bool _paintByGenial = false;
        [Category("Barras de color • GenialLine"), DisplayName("Colorear por GenialLine (arriba/abajo)")]
        public bool PaintByGenial { get => _paintByGenial; set { if (_paintByGenial == value) return; _paintByGenial = value; RecalculateValues(); } }

        private bool _genialCrossOnly = false;
        [Category("Barras de color • GenialLine"), DisplayName("Solo cruces de GenialLine")]
        public bool GenialCrossOnly { get => _genialCrossOnly; set { if (_genialCrossOnly == value) return; _genialCrossOnly = value; RecalculateValues(); } }

        private MediaColor _colAboveGenial = MediaColor.FromArgb(255, 0, 170, 0);
        private MediaColor _colBelowGenial = MediaColor.FromArgb(255, 210, 0, 0);
        private MediaColor _colCrossUp     = MediaColor.FromArgb(255, 0, 90, 200);
        private MediaColor _colCrossDown   = MediaColor.FromArgb(255, 200, 0, 140);
        [Category("Barras de color • GenialLine"), DisplayName("Color ARRIBA (estado)")] public MediaColor ColAboveGenial { get => _colAboveGenial; set { if (_colAboveGenial.Equals(value)) return; _colAboveGenial = value; RecalculateValues(); } }
        [Category("Barras de color • GenialLine"), DisplayName("Color ABAJO (estado)")] public MediaColor ColBelowGenial { get => _colBelowGenial; set { if (_colBelowGenial.Equals(value)) return; _colBelowGenial = value; RecalculateValues(); } }
        [Category("Barras de color • GenialLine"), DisplayName("Color CRUCE ALCISTA")] public MediaColor ColCrossUp { get => _colCrossUp; set { if (_colCrossUp.Equals(value)) return; _colCrossUp = value; RecalculateValues(); } }
        [Category("Barras de color • GenialLine"), DisplayName("Color CRUCE BAJISTA")] public MediaColor ColCrossDown { get => _colCrossDown; set { if (_colCrossDown.Equals(value)) return; _colCrossDown = value; RecalculateValues(); } }

        // ============================== SERIES VISIBLES ==============================
        private readonly PaintbarsDataSeries _paintBars = new("PAINT_BARS", "Paint Bars");

        private readonly ValueDataSeries _ema8    = new("EMA 8")    { VisualType = VisualMode.Line,  Width = 2, Color = MediaColor.FromArgb(255, 220, 0, 0) };
        private readonly ValueDataSeries _wilder8 = new("Wilder 8") { VisualType = VisualMode.Line,  Width = 2, Color = MediaColor.FromArgb(255, 180, 0, 0) };
        private readonly RangeDataSeries _fillUp   = new("ZC Fill Up");
        private readonly RangeDataSeries _fillDown = new("ZC Fill Down");

        private readonly ValueDataSeries _genial  = new("GENIAL LINE (c9)") { VisualType = VisualMode.Cross, Width = 2 };

        private readonly ValueDataSeries _ema123s  = new("__ema123")  { IsHidden = true };
        private readonly ValueDataSeries _ema188s  = new("__ema188")  { IsHidden = true };
        private readonly ValueDataSeries _ema416s  = new("__ema416")  { IsHidden = true };
        private readonly ValueDataSeries _ema618s  = new("__ema618")  { IsHidden = true };
        private readonly ValueDataSeries _ema882s  = new("__ema882")  { IsHidden = true };
        private readonly ValueDataSeries _ema1223s = new("__ema1223") { IsHidden = true };

        private readonly RangeDataSeries _tun1Fill = new("COLOR TUNEL 1");
        private readonly RangeDataSeries _tun2Fill = new("COLOR TUNEL 2");
        private readonly RangeDataSeries _tun3Fill = new("COLOR TUNEL 3");

        private readonly ValueDataSeries _divBull = new("Divergencia Alcista") { VisualType = VisualMode.Line, Width = 1, Color = MediaColor.FromArgb(255, 0, 150, 0) };
        private readonly ValueDataSeries _divBear = new("Divergencia Bajista") { VisualType = VisualMode.Line, Width = 1, Color = MediaColor.FromArgb(255, 180, 0, 0) };

        // ============================== ESTADO / CACHÉS ==============================
        private bool _defaultsApplied = false;
        private readonly Dictionary<int, List<decimal>> _cache = new();
        private readonly List<decimal> _oscBuf = new();

        [Category("SignalBus"), DisplayName("ChannelKey")]
        public string ChannelKey { get; set; } = "default";

        public enum TriggerKind { WPR_11_12, GenialLine }
        [Category("SignalBus"), DisplayName("TriggerSource")]
        public TriggerKind TriggerSource { get; set; } = TriggerKind.WPR_11_12;

        private long _lastPublishedBar = -1;

        private static MediaColor MC(byte a, byte r, byte g, byte b) => MediaColor.FromArgb(a, r, g, b);

        public FourSixEightIndicator()
        {
            if (DataSeries.Count == 0) DataSeries.Add(_paintBars);
            else DataSeries[0] = _paintBars;

            HideDefaultSeriesIfAny();

            DataSeries.Add(_ema8); DataSeries.Add(_wilder8);
            DataSeries.Add(_fillUp); DataSeries.Add(_fillDown);

            DataSeries.Add(_genial);

            DataSeries.Add(_ema123s); DataSeries.Add(_ema188s);
            DataSeries.Add(_ema416s); DataSeries.Add(_ema618s);
            DataSeries.Add(_ema882s); DataSeries.Add(_ema1223s);
            DataSeries.Add(_tun1Fill); DataSeries.Add(_tun2Fill); DataSeries.Add(_tun3Fill);

            DataSeries.Add(_divBull); DataSeries.Add(_divBear);

            SetFillColor(_fillUp,   MC(0x58, 0x58, 0x78, 0x16));
            SetFillColor(_fillDown, MC(0x72, 0xC0, 0x50, 0x4D));
            SetFillColor(_tun1Fill, MC(0x3E, 0x64, 0x95, 0xED));
            SetFillColor(_tun2Fill, MC(0x30, 0xFF, 0xFF, 0x00));
            SetFillColor(_tun3Fill, MC(0x27, 0x8B, 0x00, 0x00));

            TrySetDrawAbove(_fillUp, true);  TryHideRangeOutline(_fillUp);
            TrySetDrawAbove(_fillDown, true); TryHideRangeOutline(_fillDown);
            TrySetDrawAbove(_tun1Fill, true); TryHideRangeOutline(_tun1Fill);
            TrySetDrawAbove(_tun2Fill, true); TryHideRangeOutline(_tun2Fill);
            TrySetDrawAbove(_tun3Fill, true); TryHideRangeOutline(_tun3Fill);

            _paintBars.DrawAbovePrice = true;

            // 👉 Aplica los colores base definidos por el usuario
            _ema8.Color    = Ema8Color;     // o _ema8Color, si prefieres el campo
            _wilder8.Color = Wilder8Color;  // o _wilder8Color

        }

        protected override void OnRecalculate()
        {
            HideDefaultSeriesIfAny();

            _paintBars.Clear();

            _ema8.Clear(); _wilder8.Clear(); _fillUp.Clear(); _fillDown.Clear();
            _genial.Clear();

            _ema123s.Clear(); _ema188s.Clear(); _ema416s.Clear(); _ema618s.Clear(); _ema882s.Clear(); _ema1223s.Clear();
            _tun1Fill.Clear(); _tun2Fill.Clear(); _tun3Fill.Clear();

            _divBull.Clear(); _divBear.Clear();

            _cache.Clear();
            _oscBuf.Clear();
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0 && !_defaultsApplied)
            {
                _defaultsApplied = true;
            }

            var candle = GetCandle(bar);
            var c = candle.Close;
            var h = candle.High;
            var l = candle.Low;

            _divBull.Colors[bar] = DColor.Transparent;
            _divBear.Colors[bar] = DColor.Transparent;

            const int kWPR = 50000, kADX = 50001;
            var wpr = ComputeWpr(bar, _periodoWilliams); SetCache(kWPR, bar, wpr);
            var adx = ComputeAdxSig(bar, _adxlen);       SetCache(kADX, bar, adx);

            if (!_enableCandleColors) _paintBars[bar] = null;
            else
            {
                int rule = 0;
                var wprPrev = bar > 0 ? GetPrevCached(kWPR, bar - 1) : wpr;
                var sigPrev = bar > 0 ? GetPrevCached(kADX, bar - 1) : adx;

                if (wpr < -50m && adx >= sigPrev && wpr > wprPrev) rule = 1;
                if (wpr > -50m && wpr < -27m && adx >= sigPrev && wpr > wprPrev) rule = 2;
                if (wpr > -27m && adx >= sigPrev && wpr > wprPrev) rule = 3;
                if (wpr > -50m && adx <= sigPrev && wpr < wprPrev) rule = 4;
                if (wpr < -50m && adx <= sigPrev && wpr < wprPrev) rule = 5;
                if (wpr > -50m && adx >= sigPrev && wpr < wprPrev) rule = 6;
                if (wpr < -50m && wpr > -72m && adx >= sigPrev && wpr < wprPrev) rule = 7;
                if (wpr < -72m && adx >= sigPrev && wpr < wprPrev) rule = 8;
                if (wpr < -50m && adx <= sigPrev && wpr > wprPrev) rule = 9;
                if (wpr > -50m && adx <= sigPrev && wpr > wprPrev) rule = 10;
                bool crossUnder = bar > 0 && wpr < -50m && wprPrev >= -50m;
                bool crossOver  = bar > 0 && wpr > -50m && wprPrev <= -50m;
                if (crossUnder) rule = 11;
                if (crossOver)  rule = 12;

                MediaColor? picked = null;
                switch (rule)
                {
                    case 1:  picked = _colSubeSinFuerzaB;    break;
                    case 2:  picked = _colSubeFuerzaMediaA; break;
                    case 3:  picked = _colSubeFuerzaGrandeA;break;
                    case 4:  picked = _colBajaSinFuerzaA;   break;
                    case 5:  picked = _colBajaSinFuerzaB;   break;
                    case 6:  picked = _colBajaCorreccionA;  break;
                    case 7:  picked = _colBajaFuerzaMediaB; break;
                    case 8:  picked = _colBajaFuerzaGrandeB;break;
                    case 9:  picked = _colSubeCorreccionB;  break;
                    case 10: picked = _colSubeSinFuerzaA;   break;
                    case 11: picked = _colCambioBajista;    break;
                    case 12: picked = _colCambioAlcista;    break;
                }
                _paintBars[bar] = picked;
            }

            var w8 = RmaExact(bar, 8, key: 8100);       _wilder8[bar] = w8;
            var e8 = EmaExact(bar, Ema8Len, key: 8000); _ema8[bar]    = e8;

            // === Colores por barra para EMA8 / Wilder8 ===
            // (usamos la pendiente frente al valor previo en cache)
            var prevE8 = bar > 0 ? GetPrevCached(8000, bar - 1) : e8;
            var prevW8 = bar > 0 ? GetPrevCached(8100, bar - 1) : w8;

            if (Ema8AutoBicolor)
                _ema8.Colors[bar] = (e8 >= prevE8) ? ToD(Ema8UpColor) : ToD(Ema8DownColor);
            else
                _ema8.Colors[bar] = ToD(Ema8Color); // respeta el color fijo del panel

            if (Wilder8AutoBicolor)
                _wilder8.Colors[bar] = (w8 >= prevW8) ? ToD(W8UpColor) : ToD(W8DownColor);
            else
                _wilder8.Colors[bar] = ToD(Wilder8Color); // color fijo del panel


            // Rellenos Up/Down sin tocar el color por-barra de la EMA8
            if (Ema8Len > 0 && e8 >= w8)
            {
                SetRange(_fillUp, bar, w8, e8);
                ClearRange(_fillDown, bar);
            }
            else
            {
                SetRange(_fillDown, bar, e8, w8);
                ClearRange(_fillUp, bar);
            }

            var pSma = Sma(VelasBanda, bar);
            var rSma = SmaRange(VelasBanda, bar);
            var alt1 = pSma + rSma * Desviacio;
            var baix1 = pSma - rSma * Desviacio;
            var mitjana = (alt1 + baix1) / 2m;
            var c9 = (mitjana + Sma(34, bar)) / 2m;

            _genial[bar] = c9;

            // Color de GenialLine:
            if (GenialAutoBicolor)
            {
                // ahora: tus colores configurables
                bool falling = bar > 0 && c9 <= _genial[bar - 1];
                _genial.Colors[bar] = falling ? ToD(_genialDownColor) : ToD(_genialUpColor);
            }
            else
            {
                // color fijo elegido por el usuario
                if (_genial.Color != _genialColor) _genial.Color = _genialColor;
            }

            var o = candle.Open;
            var g9 = _genial[bar];
            var cPrev = bar > 0 ? GetCandle(bar - 1).Close : c;
            var g9Prev = bar > 0 ? _genial[bar - 1] : g9;

            bool crossUpGL   = bar > 0 && c > g9 && cPrev <= g9Prev;
            bool crossDownGL = bar > 0 && c < g9 && cPrev >= g9Prev;

            bool crossUnderWpr = bar > 0 && wpr < -50m && GetPrevCached(50000, bar - 1) >= -50m;
            bool crossOverWpr  = bar > 0 && wpr > -50m && GetPrevCached(50000, bar - 1) <= -50m;

            // === PUBLICACIÓN DE SEÑAL DEL 468 ===
            if (bar == CurrentBar - 1 && bar > _lastPublishedBar)
            {
                bool fire;
                Trend trend;
                string src;

                if (TriggerSource == TriggerKind.WPR_11_12)
                {
                    fire  = crossUnderWpr || crossOverWpr;
                    trend = crossOverWpr ? Trend.Up : (crossUnderWpr ? Trend.Down : Trend.None);
                    src = "WPR";
                }
                else
                {
                    fire  = crossUpGL || crossDownGL;
                    trend = crossUpGL ? Trend.Up : (crossDownGL ? Trend.Down : Trend.None);
                    src = "GenialLine";
                }

                if (fire && trend != Trend.None)
                {
                    var sig = new Signal468
                    {
                        Uid = Guid.NewGuid(),
                        Ts = DateTime.UtcNow,
                        BarId = bar,
                        Dir = trend == Trend.Up ? +1 : -1,
                        Trend = trend,
                        Source = src,
                        PriceRef = c
                    };
                    // Bus.WriteLatest(Bus.StatePath(ChannelKey), sig); // TODO: Implementar Bus
                    _lastPublishedBar = bar;
                }
            }

            if (_enableCandleColors && _paintByGenial)
            {
                bool crossUp = crossUpGL;
                bool crossDown = crossDownGL;
                bool above = c >= g9;
                bool upBar = c >= o;

                MediaColor? picked = above ? _colAboveGenial : _colBelowGenial;
                if (_genialCrossOnly) picked = crossUp ? _colCrossUp : (crossDown ? _colCrossDown : (MediaColor?)null);
                else if (_trendShading) picked = above ? (upBar ? Shade(_colAboveGenial, 1.25) : Shade(_colAboveGenial, .75))
                                                        : (upBar ? Shade(_colBelowGenial, .75) : Shade(_colBelowGenial, 1.25));

                if (crossUp) picked = _colCrossUp; else if (crossDown) picked = _colCrossDown;
                if (picked != null) _paintBars[bar] = picked;
            }

            if (!_enableTunnels)
            {
                ClearRange(_tun1Fill, bar); ClearRange(_tun2Fill, bar); ClearRange(_tun3Fill, bar);
            }
            else
            {
                var v123  = EmaExact(bar, _ema123, 10123);   _ema123s[bar]  = v123;
                var v188  = EmaExact(bar, _ema188, 10188);   _ema188s[bar]  = v188;
                var v416  = EmaExact(bar, _ema416, 10416);   _ema416s[bar]  = v416;
                var v618  = EmaExact(bar, _ema618, 10618);   _ema618s[bar]  = v618;
                var v882  = EmaExact(bar, _ema882, 10882);   _ema882s[bar]  = v882;
                var v1223 = EmaExact(bar, _ema1223, 11223);  _ema1223s[bar] = v1223;

                if (_showTunnel1) SetRange(_tun1Fill, bar, v123, v188); else ClearRange(_tun1Fill, bar);
                if (_showTunnel2) SetRange(_tun2Fill, bar, v416, v618); else ClearRange(_tun2Fill, bar);
                if (_showTunnel3) SetRange(_tun3Fill, bar, v882, v1223); else ClearRange(_tun3Fill, bar);
            }

            var hiP = HighestHigh(bar, _pHL);
            var loP = LowestLow(bar, _pHL);
            var denom = hiP - loP;
            var oscRaw = denom == 0 ? 0 : (c - loP) / denom * 100m;

            var osc1 = Dema(oscRaw, _q, bar, key: 9000);
            EnsureBufSize(_oscBuf, bar); _oscBuf[bar] = osc1;

            if (_showDivergences && bar >= _divLookback + 12 && bar >= 2)
            {
                var osc0  = _oscBuf[bar];
                var osc_1 = _oscBuf[bar - 1];
                var osc_2 = _oscBuf[bar - 2];

                if (osc_1 > osc0 && osc_1 > osc_2)
                {
                    var extremum2     = osc_1;
                    var extremum1     = HighestSeriesBuf(_oscBuf, bar, _divLookback);
                    var priceMaxPrevN = HighestClose(bar - 2, _divLookback);
                    if (extremum2 < extremum1 && GetCandle(bar - 1).Close > priceMaxPrevN)
                    {
                        var idxPrev = ArgMaxCloseIndexPrev(bar - 2, _divLookback);
                        if (idxPrev >= 0) DrawDivLine(_divBear, idxPrev, bar - 1, DColor.FromArgb(255, 180, 0, 0));
                    }
                }
                if (osc_1 < osc0 && osc_1 < osc_2)
                {
                    var extremum22    = osc_1;
                    var extremum11    = LowestSeriesBuf(_oscBuf, bar, _divLookback);
                    var priceMinPrevN = LowestClose(bar - 2, _divLookback);
                    if (extremum22 > extremum11 && GetCandle(bar - 1).Close < priceMinPrevN)
                    {
                        var idxPrev = ArgMinCloseIndexPrev(bar - 2, _divLookback);
                        if (idxPrev >= 0) DrawDivLine(_divBull, idxPrev, bar - 1, DColor.FromArgb(255, 0, 150, 0));
                    }
                }
            }
        }

        private static void TryHideRangeOutline(RangeDataSeries s)
        {
            var t = s.GetType();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite) continue;
                if (p.PropertyType.FullName == "System.Windows.Media.Color")
                {
                    var n = p.Name.ToLowerInvariant();
                    if (n.Contains("border") || n.Contains("stroke") || n.Contains("outline") || n.Contains("line"))
                        p.SetValue(s, MediaColor.FromArgb(0, 0, 0, 0));
                }
            }
        }
        private static void TrySetDrawAbove(RangeDataSeries s, bool v)
        {
            var p = s.GetType().GetProperty("DrawAbovePrice", BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                p.SetValue(s, v);
        }

        private static void EnsureBufSize(List<decimal> buf, int index) { while (buf.Count <= index) buf.Add(0m); }
        private static decimal SmaOnBuffer(List<decimal> buf, int len, int bar)
        {
            if (len <= 0 || bar < 0) return 0;
            var start = Math.Max(0, bar - len + 1);
            decimal sum = 0; int n = 0;
            for (int i = start; i <= bar; i++) { sum += buf[i]; n++; }
            return n == 0 ? 0 : sum / n;
        }
        private static decimal StdDevOnBuffer(List<decimal> buf, int len, int bar)
        {
            if (len <= 1 || bar < 0) return 0;
            var start = Math.Max(0, bar - len + 1);
            int n = bar - start + 1; if (n <= 1) return 0;
            var mean = SmaOnBuffer(buf, n, bar);
            decimal ss = 0; for (int i = start; i <= bar; i++) { var d = buf[i] - mean; ss += d * d; }
            return (decimal)Math.Sqrt((double)(ss / n));
        }

        private decimal Sma(int len, int bar)
        {
            if (len <= 1) return GetCandle(bar).Close;
            var start = Math.Max(0, bar - len + 1);
            decimal sum = 0;
            for (int i = start; i <= bar; i++) sum += GetCandle(i).Close;
            return sum / (bar - start + 1);
        }
        private decimal SmaRange(int len, int bar)
        {
            if (len <= 1) { var c = GetCandle(bar); return c.High - c.Low; }
            var start = Math.Max(0, bar - len + 1);
            decimal sum = 0;
            for (int i = start; i <= bar; i++) { var c = GetCandle(i); sum += (c.High - c.Low); }
            return sum / (bar - start + 1);
        }

        private static MediaColor Shade(MediaColor c, double factor)
        {
            int r = Math.Max(0, Math.Min(255, (int)Math.Round(c.R * factor)));
            int g = Math.Max(0, Math.Min(255, (int)Math.Round(c.G * factor)));
            int b = Math.Max(0, Math.Min(255, (int)Math.Round(c.B * factor)));
            return MediaColor.FromArgb(c.A, (byte)r, (byte)g, (byte)b);
        }
        private static DColor ToD(MediaColor c) => DColor.FromArgb(c.A, c.R, c.G, c.B);

        private decimal EmaExact(int bar, int len, int key)
        {
            var c = GetCandle(bar).Close;
            if (bar == 0) { SetCache(key, 0, c); return c; }

            var prev = GetPrevCached(key, bar - 1);
            if (bar == 1 && prev == 0m) prev = GetCandle(0).Close;

            var k = len <= 1 ? 1m : 2m / (len + 1m);
            var cur = prev + k * (c - prev);
            SetCache(key, bar, cur);
            return cur;
        }

        private decimal RmaExact(int bar, int len, int key)
        {
            var c = GetCandle(bar).Close;
            if (bar == 0) { SetCache(key, 0, c); return c; }

            var prev = GetPrevCached(key, bar - 1);
            if (bar == 1 && prev == 0m) prev = GetCandle(0).Close;

            var cur = len <= 1 ? c : (prev * (len - 1) + c) / len;
            SetCache(key, bar, cur);
            return cur;
        }

        private decimal Dema(decimal v, int len, int bar, int key)
        {
            SetCache(key, bar, v);
            var ema1 = EmaOnCachedSeries(len, bar, key, key + 1);
            SetCache(key + 1, bar, ema1);
            var ema2 = EmaOnCachedSeries(len, bar, key + 1, key + 2);
            return 2m * ema1 - ema2;
        }

        private decimal EmaOnCachedSeries(int len, int bar, int inKey, int outKey)
        {
            var v = GetPrevCached(inKey, bar);
            if (len <= 1) { SetCache(outKey, bar, v); return v; }

            var prevEma = bar == 0 ? v : GetPrevCached(outKey, bar - 1);
            if (bar == 1 && prevEma == 0m) prevEma = GetPrevCached(inKey, 0);

            var k = 2m / (len + 1m);
            var cur = prevEma + k * (v - prevEma);
            SetCache(outKey, bar, cur);
            return cur;
        }

        private decimal RmaOnCachedSeries(int len, int bar, int inKey, int outKey)
        {
            var v = GetPrevCached(inKey, bar);
            if (len <= 1) { SetCache(outKey, bar, v); return v; }

            var prev = bar == 0 ? v : GetPrevCached(outKey, bar - 1);
            if (bar == 1 && prev == 0m) prev = GetPrevCached(inKey, 0);

            var cur = (prev * (len - 1) + v) / len;
            SetCache(outKey, bar, cur);
            return cur;
        }

        private void DrawDivLine(ValueDataSeries series, int idxA, int idxB, DColor color)
        {
            if (idxA < 0 || idxB <= idxA) return;

            var yA = GetCandle(idxA).Close;
            var yB = GetCandle(idxB).Close;

            if (idxA - 1 >= 0) { series[idxA - 1] = yA; series.Colors[idxA - 1] = DColor.Transparent; }
            var len = idxB - idxA;
            for (int i = idxA; i <= idxB; i++)
            {
                var t = len == 0 ? 1m : (decimal)(i - idxA) / len;
                var y = yA + (yB - yA) * t;
                series[i] = y;
                series.Colors[i] = color;
            }
            if (idxB + 1 <= CurrentBar) { series[idxB + 1] = yB; series.Colors[idxB + 1] = DColor.Transparent; }
        }

        private void SetRange(RangeDataSeries s, int bar, decimal a, decimal b)
        {
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            var rv = new RangeValue();
            SetMemberNumeric(rv, "Lower", lo); SetMemberNumeric(rv, "Upper", hi);
            SetMemberNumeric(rv, "Low", lo);   SetMemberNumeric(rv, "High", hi);
            SetMemberNumeric(rv, "Bottom", lo);SetMemberNumeric(rv, "Top", hi);
            SetMemberBool(rv, "IsEmpty", false); SetMemberBool(rv, "Empty", false); SetMemberBool(rv, "HasValue", true);
            s[bar] = rv;
        }
        private void ClearRange(RangeDataSeries s, int bar)
        {
            var rv = new RangeValue();
            SetMemberBool(rv, "IsEmpty", true); SetMemberBool(rv, "Empty", true); SetMemberBool(rv, "HasValue", false);
            SetMemberNumeric(rv, "Lower", 0m); SetMemberNumeric(rv, "Upper", 0m);
            SetMemberNumeric(rv, "Low", 0m);   SetMemberNumeric(rv, "High", 0m);
            SetMemberNumeric(rv, "Bottom", 0m);SetMemberNumeric(rv, "Top", 0m);
            s[bar] = rv;
        }
        private static void SetFillColor(RangeDataSeries s, MediaColor color)
        {
            var t = s.GetType();
            var pColor = t.GetProperty("Color", BindingFlags.Public | BindingFlags.Instance);
            if (pColor != null && pColor.CanWrite && pColor.PropertyType.FullName == "System.Windows.Media.Color")
            { pColor.SetValue(s, color); return; }
            var pBrush = t.GetProperty("Brush", BindingFlags.Public | BindingFlags.Instance);
            if (pBrush != null && pBrush.CanWrite)
            {
                var brushType = Type.GetType("System.Windows.Media.SolidColorBrush, PresentationCore");
                if (brushType != null)
                {
                    var brush = Activator.CreateInstance(brushType, color);
                    pBrush.SetValue(s, brush);
                }
            }
        }
        private static void SetMemberNumeric(object obj, string name, decimal val)
        {
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                if (p.PropertyType == typeof(decimal)) { p.SetValue(obj, val); return; }
                if (p.PropertyType == typeof(double))  { p.SetValue(obj, (double)val); return; }
                if (p.PropertyType == typeof(float))   { p.SetValue(obj, (float)val);  return; }
            }
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                if (f.FieldType == typeof(decimal)) { f.SetValue(obj, val); return; }
                if (f.FieldType == typeof(double))  { f.SetValue(obj, (double)val); return; }
                if (f.FieldType == typeof(float))   { f.SetValue(obj, (float)val);  return; }
            }
        }
        private static void SetMemberBool(object obj, string name, bool val)
        {
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite && p.PropertyType == typeof(bool)) { p.SetValue(obj, val); return; }
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(bool)) f.SetValue(obj, val);
        }

        private decimal HighestHigh(int bar, int len) { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= bar; i++) if (GetCandle(i).High > m) m = GetCandle(i).High; return m == decimal.MinValue ? GetCandle(bar).High : m; }
        private decimal LowestLow(int bar, int len)   { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= bar; i++) if (GetCandle(i).Low  < m) m = GetCandle(i).Low;  return m == decimal.MaxValue ? GetCandle(bar).Low  : m; }
        private decimal HighestClose(int bar, int len){ var s = Math.Max(0, bar - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= bar; i++) if (GetCandle(i).Close> m) m = GetCandle(i).Close;return m == decimal.MinValue ? GetCandle(bar).Close: m; }
        private decimal LowestClose(int bar, int len) { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= bar; i++) if (GetCandle(i).Close< m) m = GetCandle(i).Close;return m == decimal.MaxValue ? GetCandle(bar).Close: m; }
        private int ArgMaxCloseIndexPrev(int barEnd, int len){ if (barEnd < 0) return -1; int s = Math.Max(0, barEnd - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= barEnd; i++) if (GetCandle(i).Close > m) m = GetCandle(i).Close; for (int i = barEnd; i >= s; i--) if (GetCandle(i).Close == m) return i; return -1; }
        private int ArgMinCloseIndexPrev(int barEnd, int len){ if (barEnd < 0) return -1; int s = Math.Max(0, barEnd - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= barEnd; i++) if (GetCandle(i).Close < m) m = GetCandle(i).Close; for (int i = barEnd; i >= s; i--) if (GetCandle(i).Close == m) return i; return -1; }

        private decimal HighestSeriesBuf(List<decimal> buf, int bar, int len){ var s = Math.Max(0, bar - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= bar; i++) if (buf[i] > m) m = buf[i]; return m == decimal.MinValue ? buf[bar] : m; }
        private decimal LowestSeriesBuf(List<decimal> buf, int bar, int len) { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= bar; i++) if (buf[i] < m) m = buf[i]; return m == decimal.MaxValue ? buf[bar] : m; }

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

        private void HideDefaultSeriesIfAny()
        {
            if (DataSeries.Count > 0 && DataSeries[0] is ValueDataSeries def)
                def.IsHidden = true;
        }

        private decimal ComputeWpr(int bar, int len)
        {
            var hh = HighestHigh(bar, len);
            var ll = LowestLow(bar, len);
            var denom = hh - ll;
            if (denom == 0) return 0m;
            var c = GetCandle(bar).Close;
            return -100m * (hh - c) / denom;
        }

        private decimal ComputeAdxSig(int bar, int len)
        {
            if (bar == 0) return 0m;

            var cur = GetCandle(bar);
            var prev = GetCandle(bar - 1);

            decimal tr = Math.Max(cur.High - cur.Low, Math.Max(Math.Abs(cur.High - prev.Close), Math.Abs(cur.Low - prev.Close)));

            decimal up = cur.High - prev.High;
            decimal down = prev.Low - cur.Low;
            decimal plusDM = (up > down && up > 0) ? up : 0m;
            decimal minusDM = (down > up && down > 0) ? down : 0m;

            const int kTR = 20020, kTRr = 20021;
            const int kPDM = 20022, kPDMr = 20023;
            const int kMDM = 20024, kMDMr = 20025;
            const int kDX  = 20026, kADX  = 20027;

            SetCache(kTR,  bar, tr);
            SetCache(kPDM, bar, plusDM);
            SetCache(kMDM, bar, minusDM);

            var rTR   = RmaOnCachedSeries(len, bar, kTR,  kTRr);
            var rPDM  = RmaOnCachedSeries(len, bar, kPDM, kPDMr);
            var rMDM  = RmaOnCachedSeries(len, bar, kMDM, kMDMr);

            decimal plus  = rTR == 0 ? 0 : 100m * rPDM / rTR;
            decimal minus = rTR == 0 ? 0 : 100m * rMDM / rTR;

            var sum = plus + minus;
            var dx = 100m * Math.Abs(plus - minus) / (sum == 0 ? 1 : sum);

            SetCache(kDX, bar, dx);
            var adx = RmaOnCachedSeries(len, bar, kDX, kADX);
            return adx;
        }
    }
}
