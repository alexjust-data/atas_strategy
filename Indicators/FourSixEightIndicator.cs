using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Reflection;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Globalization;

using ATAS.Indicators;
using ATAS.Types;

using DColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace MyAtasIndicator.Indicators
{
    [DisplayName("04 + 06 + 08 (ATAS) - Minimal")]
    public class FourSixEightIndicator : Indicator
    {
        // ============================== PARÁMETROS ==============================

        // --- Zona de corrección (EMA8 vs Wilder8) ---
        private int _ema8Len = 8;
        [Category("Drawing"), DisplayName("EMA 8")]
        public int Ema8Len { get => _ema8Len; set { var v = Math.Max(1, value); if (_ema8Len == v) return; _ema8Len = v; RecalculateValues(); } }

        // --- Genial Line ---
        private int _velasBanda = 20;
        [Category("Genial Line"), DisplayName("Velas Banda")]
        public int VelasBanda { get => _velasBanda; set { var v = Math.Max(1, value); if (_velasBanda == v) return; _velasBanda = v; RecalculateValues(); } }

        private decimal _desviacio = 3.14159265358979m;
        [Category("Genial Line"), DisplayName("Desviación")]
        public decimal Desviacio { get => _desviacio; set { if (_desviacio == value) return; _desviacio = value; RecalculateValues(); } }

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

        // --- Agotamiento ---
        private bool _showExhaustionDots = true;
        [Category("Agotamiento"), DisplayName("Mostrar Puntos de Agotamiento")]
        public bool ShowExhaustionDots { get => _showExhaustionDots; set { if (_showExhaustionDots == value) return; _showExhaustionDots = value; RecalculateValues(); } }

        private int _dotOffsetTicks = 80;
        [Category("Agotamiento"), DisplayName("Offset mínimo (ticks)")]
        public int DotOffsetTicks { get => _dotOffsetTicks; set { var v = Math.Max(0, value); if (_dotOffsetTicks == v) return; _dotOffsetTicks = v; RecalculateValues(); } }

        private decimal _dotOffsetRangeMul = 0.6m;
        [Category("Agotamiento"), DisplayName("Offset % rango barra")]
        public decimal DotOffsetRangeMul { get => _dotOffsetRangeMul; set { if (_dotOffsetRangeMul == value) return; _dotOffsetRangeMul = value; RecalculateValues(); } }

        private int _periodeCond1 = 30, _permitjDesv = 120, _cooldownBars = 0;
        [Category("Agotamiento"), DisplayName("Periodo Condición 1")]          public int PeriodeCond1 { get => _periodeCond1; set { var v = Math.Max(1, value); if (_periodeCond1 == v) return; _periodeCond1 = v; RecalculateValues(); } }
        [Category("Agotamiento"), DisplayName("Periodo Desviación Estándar")]  public int PermitjDesv  { get => _permitjDesv;  set { var v = Math.Max(1, value); if (_permitjDesv  == v) return; _permitjDesv  = v; RecalculateValues(); } }
        [Category("Agotamiento"), DisplayName("Cooldown (barras)")]            public int CooldownBars { get => _cooldownBars; set { var v = Math.Max(0, value); if (_cooldownBars == v) return; _cooldownBars = v; RecalculateValues(); } }

        private decimal _k1 = 1m, _k2 = 1m;
        [Category("Agotamiento"), DisplayName("Multiplicador Desv.Est. (K1)")] public decimal K1 { get => _k1; set { if (_k1 == value) return; _k1 = value; RecalculateValues(); } }
        [Category("Agotamiento"), DisplayName("Multiplicador Desv.Est. (K2)")] public decimal K2 { get => _k2; set { if (_k2 == value) return; _k2 = value; RecalculateValues(); } }

        private MediaColor _exhaustionDotColor = MediaColor.FromArgb(0x97, 0x00, 0x00, 0x00);
        [Category("Agotamiento"), DisplayName("Color Punto")]
        public MediaColor ExhaustionDotColor { get => _exhaustionDotColor; set { if (_exhaustionDotColor.Equals(value)) return; _exhaustionDotColor = value; UpdateExhaustionDotStyle(); } }

        private int _exhaustionDotSize = 6;
        [Category("Agotamiento"), DisplayName("Tamaño Punto")]
        public int ExhaustionDotSize { get => _exhaustionDotSize; set { var v = Math.Max(1, value); if (_exhaustionDotSize == v) return; _exhaustionDotSize = v; UpdateExhaustionDotStyle(); } }

        private bool _isolateExhaustionDots = false;
        [Category("Agotamiento"), DisplayName("Puntos sin línea (aislar)")]
        public bool IsolateExhaustionDots { get => _isolateExhaustionDots; set { if (_isolateExhaustionDots == value) return; _isolateExhaustionDots = value; RecalculateValues(); } }

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
        [Category("Barras de color • Colores (ALCISTA)"), DisplayName("🡻  Baja - Parada Impulso - CORRECCIÓN")] public MediaColor ColBajaCorreccionA { get => _colBajaCorreccionA; set { if (_colBajaCorreccionA.Equals(value)) return; _colBajaCorreccionA = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (ALCISTA)"), DisplayName("🡻  Baja - Sin Fuerza")] public MediaColor ColBajaSinFuerzaA { get => _colBajaSinFuerzaA; set { if (_colBajaSinFuerzaA.Equals(value)) return; _colBajaSinFuerzaA = value; RecalculateValues(); } }

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
        [Category("Barras de color • Colores (BAJISTA)"), DisplayName("🡹  Sube - Parada Impulso - CORRECCIÓN")] public MediaColor ColSubeCorreccionB { get => _colSubeCorreccionB; set { if (_colSubeCorreccionB.Equals(value)) return; _colSubeCorreccionB = value; RecalculateValues(); } }
        [Category("Barras de color • Colores (BAJISTA)"), DisplayName("🡹  Sube - Sin Fuerza")] public MediaColor ColSubeSinFuerzaB { get => _colSubeSinFuerzaB; set { if (_colSubeSinFuerzaB.Equals(value)) return; _colSubeSinFuerzaB = value; RecalculateValues(); } }

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

        // ======================= ALERTAS =======================
        private bool _enableAlerts = false;
        [Category("Alertas"), DisplayName("Activar alertas")]
        public bool EnableAlerts { get => _enableAlerts; set { if (_enableAlerts == value) return; _enableAlerts = value; } }

        private bool _alertOnCrossUp = true;
        [Category("Alertas"), DisplayName("Avisar en cruce ALCISTA (precio > GenialLine)")]
        public bool AlertOnCrossUp { get => _alertOnCrossUp; set { if (_alertOnCrossUp == value) return; _alertOnCrossUp = value; } }

        private bool _alertOnCrossDown = true;
        [Category("Alertas"), DisplayName("Avisar en cruce BAJISTA (precio < GenialLine)")]
        public bool AlertOnCrossDown { get => _alertOnCrossDown; set { if (_alertOnCrossDown == value) return; _alertOnCrossDown = value; } }

        private int _alertCooldownBars = 3;
        [Category("Alertas"), DisplayName("Cooldown (barras) para evitar duplicados")]
        public int AlertCooldownBars { get => _alertCooldownBars; set { var v = Math.Max(0, value); if (_alertCooldownBars == v) return; _alertCooldownBars = v; } }

        // --- Telegram ---
        private bool _alertTelegram = false;
        [Category("Alertas • Telegram"), DisplayName("Enviar a Telegram")]
        public bool AlertTelegram { get => _alertTelegram; set { if (_alertTelegram == value) return; _alertTelegram = value; } }
        private string _tgBotToken = "";
        [Category("Alertas • Telegram"), DisplayName("Bot Token")] public string TgBotToken { get => _tgBotToken; set { _tgBotToken = value ?? ""; } }
        private string _tgChatId = "";
        [Category("Alertas • Telegram"), DisplayName("Chat ID")] public string TgChatId { get => _tgChatId; set { _tgChatId = value ?? ""; } }

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

        private readonly ValueDataSeries _exhaustionDots = new("Puntos de Agotamiento") { VisualType = VisualMode.Dots };

        // ============================== ESTADO / CACHÉS ==============================
        private readonly List<decimal> _cond1Buf = new();
        private readonly List<decimal> _cond2Buf = new();
        private int _lastExhaustDotBar = -100000;

        private bool _defaultsApplied = false;

        // caché interno (no DataSeries) para EMAs/ADX/etc
        private readonly Dictionary<int, List<decimal>> _cache = new();

        // buffer interno del oscilador (sustituye a _osc1 DataSeries)
        private readonly List<decimal> _oscBuf = new();

        // alertas
        private int _lastAlertUpBar = -100000;
        private int _lastAlertDownBar = -100000;

        private static readonly HttpClient _http = new HttpClient();

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
            DataSeries.Add(_exhaustionDots);

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
        }

        // ============================== CICLO ==============================
        protected override void OnRecalculate()
        {
            HideDefaultSeriesIfAny();

            _paintBars.Clear();

            _ema8.Clear(); _wilder8.Clear(); _fillUp.Clear(); _fillDown.Clear();
            _genial.Clear();

            _ema123s.Clear(); _ema188s.Clear(); _ema416s.Clear(); _ema618s.Clear(); _ema882s.Clear(); _ema1223s.Clear();
            _tun1Fill.Clear(); _tun2Fill.Clear(); _tun3Fill.Clear();

            _divBull.Clear(); _divBear.Clear();

            _exhaustionDots.Clear(); _cond1Buf.Clear(); _cond2Buf.Clear(); _lastExhaustDotBar = -100000;

            _lastAlertUpBar = -100000;
            _lastAlertDownBar = -100000;

            _cache.Clear();
            _oscBuf.Clear();
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0 && !_defaultsApplied)
            {
                UpdateExhaustionDotStyle();
                _defaultsApplied = true;
            }

            var candle = GetCandle(bar);
            var c = candle.Close;
            var h = candle.High;
            var l = candle.Low;

            // limpiar trazos en esta barra
            _divBull.Colors[bar] = DColor.Transparent;
            _divBear.Colors[bar] = DColor.Transparent;
            _exhaustionDots.Colors[bar] = DColor.Transparent;

            // ================== WPR y ADX (en caché puro) ==================
            const int kWPR = 50000, kADX = 50001;
            var wpr = ComputeWpr(bar, _periodoWilliams); SetCache(kWPR, bar, wpr);
            var adx = ComputeAdxSig(bar, _adxlen);       SetCache(kADX, bar, adx);

            // ================== PINTADO DE VELAS ==================
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

            // ================== Wilder8 y EMA8 ==================
            var w8 = RmaExact(bar, 8, key: 8100);       _wilder8[bar] = w8;
            var e8 = EmaExact(bar, Ema8Len, key: 8000); _ema8[bar]    = e8;

            if (Ema8Len > 0 && e8 >= w8)
            {
                SetRange(_fillUp, bar, w8, e8);
                ClearRange(_fillDown, bar);
                _ema8.Colors[bar] = DColor.FromArgb(255, 0, 150, 0);
            }
            else
            {
                SetRange(_fillDown, bar, e8, w8);
                ClearRange(_fillUp, bar);
                _ema8.Colors[bar] = DColor.FromArgb(255, 220, 0, 0);
            }

            // ================== Genial Line ==================
            var pSma = Sma(VelasBanda, bar);
            var rSma = SmaRange(VelasBanda, bar);
            var alt1 = pSma + rSma * Desviacio;
            var baix1 = pSma - rSma * Desviacio;
            var mitjana = (alt1 + baix1) / 2m;
            var c9 = (mitjana + Sma(34, bar)) / 2m;

            _genial[bar] = c9;
            _genial.Colors[bar] = (bar > 0 && c9 <= _genial[bar - 1]) ? DColor.Red : DColor.Blue;

            // === Pintado por GenialLine + ALERTAS ===
            if (_enableCandleColors && _paintByGenial)
            {
                var o = candle.Open;
                var g9 = _genial[bar];
                var cPrev = bar > 0 ? GetCandle(bar - 1).Close : c;
                var g9Prev = bar > 0 ? _genial[bar - 1] : g9;

                bool crossUp   = bar > 0 && c > g9 && cPrev <= g9Prev;
                bool crossDown = bar > 0 && c < g9 && cPrev >= g9Prev;
                bool above     = c >= g9;
                bool upBar     = c >= o;

                MediaColor? picked = above ? _colAboveGenial : _colBelowGenial;
                if (_genialCrossOnly)      picked = crossUp ? _colCrossUp : (crossDown ? _colCrossDown : (MediaColor?)null);
                else if (_trendShading)    picked = above ? (upBar ? Shade(_colAboveGenial, 1.25) : Shade(_colAboveGenial, .75))
                                                          : (upBar ? Shade(_colBelowGenial, .75)  : Shade(_colBelowGenial, 1.25));

                if (crossUp) picked = _colCrossUp; else if (crossDown) picked = _colCrossDown;
                if (picked != null) _paintBars[bar] = picked;

                if (bar == CurrentBar)
                {
                    if (crossUp)   TriggerAlerts(bar, true,  c);
                    if (crossDown) TriggerAlerts(bar, false, c);
                }
            }

            // ================== Tunnel Domenec (rellenos) ==================
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

            // ================== Agotamiento ==================
            if (_showExhaustionDots && bar >= _permitjDesv)
            {
                var highestClosePeriode = HighestClose(bar, _periodeCond1);
                var cond1Val = highestClosePeriode == 0 ? 0 : (highestClosePeriode - c) / highestClosePeriode;
                EnsureBufSize(_cond1Buf, bar); _cond1Buf[bar] = cond1Val;

                var cond2Val = Sma(_periodeCond1, bar);
                EnsureBufSize(_cond2Buf, bar); _cond2Buf[bar] = cond2Val;

                var av1 = SmaOnBuffer(_cond1Buf, _permitjDesv, bar);
                var stdv1 = StdDevOnBuffer(_cond1Buf, _permitjDesv, bar);
                var cond1zon = av1 + K1 * stdv1;

                var av2 = SmaOnBuffer(_cond2Buf, _permitjDesv, bar);
                var stdv2 = StdDevOnBuffer(_cond2Buf, _permitjDesv, bar);
                var cond2zon = av2 - K2 * stdv2;

                if (cond1Val > cond1zon && cond2Val < cond2zon && (CooldownBars <= 0 || bar - _lastExhaustDotBar > CooldownBars))
                {
                    var tick = InstrumentInfo?.TickSize ?? 1m;
                    var offsetTicks = tick * _dotOffsetTicks;
                    var offsetRange = (h - l) * _dotOffsetRangeMul;
                    var y = l - Math.Max(offsetTicks, offsetRange);

                    _exhaustionDots[bar] = y;
                    _exhaustionDots.Colors[bar] = ToDColor(_exhaustionDotColor);
                    _lastExhaustDotBar = bar;

                    if (_isolateExhaustionDots)
                    {
                        BreakLine(_exhaustionDots, bar - 1);
                        BreakLine(_exhaustionDots, bar + 1);
                    }
                }
            }

            // ================== Divergencias (oscilador en buffer) ==================
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

        // ============================== HELPERS ==============================
        private static DColor ToDColor(MediaColor c) => DColor.FromArgb(c.A, c.R, c.G, c.B);

        private void UpdateExhaustionDotStyle()
        {
            _exhaustionDots.VisualType = VisualMode.Dots;
            _exhaustionDots.Color = _exhaustionDotColor;
            _exhaustionDots.Width = _exhaustionDotSize;
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

        // ========= EMAs / RMAs =========
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

        private static void BreakLine(ValueDataSeries s, int index)
        {
            if (index < 0) return;
            if (index > s.Count - 1) return;
            try
            {
                var m = s.GetType().GetMethod("SetPointOfBreak", BindingFlags.Public | BindingFlags.Instance);
                if (m != null) { m.Invoke(s, new object[] { index, true }); return; }
            }
            catch { }
            s.Colors[index] = DColor.Transparent;
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

        // Rellenos
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

        // HH/LL/HC/LC para divergencias
        private decimal HighestHigh(int bar, int len) { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= bar; i++) if (GetCandle(i).High > m) m = GetCandle(i).High; return m == decimal.MinValue ? GetCandle(bar).High : m; }
        private decimal LowestLow(int bar, int len)   { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= bar; i++) if (GetCandle(i).Low  < m) m = GetCandle(i).Low;  return m == decimal.MaxValue ? GetCandle(bar).Low  : m; }
        private decimal HighestClose(int bar, int len){ var s = Math.Max(0, bar - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= bar; i++) if (GetCandle(i).Close> m) m = GetCandle(i).Close;return m == decimal.MinValue ? GetCandle(bar).Close: m; }
        private decimal LowestClose(int bar, int len) { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= bar; i++) if (GetCandle(i).Close< m) m = GetCandle(i).Close;return m == decimal.MaxValue ? GetCandle(bar).Close: m; }
        private int ArgMaxCloseIndexPrev(int barEnd, int len){ if (barEnd < 0) return -1; int s = Math.Max(0, barEnd - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= barEnd; i++) if (GetCandle(i).Close > m) m = GetCandle(i).Close; for (int i = barEnd; i >= s; i--) if (GetCandle(i).Close == m) return i; return -1; }
        private int ArgMinCloseIndexPrev(int barEnd, int len){ if (barEnd < 0) return -1; int s = Math.Max(0, barEnd - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= barEnd; i++) if (GetCandle(i).Close < m) m = GetCandle(i).Close; for (int i = barEnd; i >= s; i--) if (GetCandle(i).Close == m) return i; return -1; }

        // versiones sobre buffer (para el oscilador)
        private decimal HighestSeriesBuf(List<decimal> buf, int bar, int len){ var s = Math.Max(0, bar - len + 1); decimal m = decimal.MinValue; for (int i = s; i <= bar; i++) if (buf[i] > m) m = buf[i]; return m == decimal.MinValue ? buf[bar] : m; }
        private decimal LowestSeriesBuf(List<decimal> buf, int bar, int len) { var s = Math.Max(0, bar - len + 1); decimal m = decimal.MaxValue; for (int i = s; i <= bar; i++) if (buf[i] < m) m = buf[i]; return m == decimal.MaxValue ? buf[bar] : m; }

        // ====== CACHÉ (no DataSeries) ======
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

        // ========================= Indicadores (WPR / ADX) =========================
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

        // ========================= ALERTAS =========================
        private async Task SendTelegramAsync(string text)
        {
            if (!_alertTelegram) return;
            if (string.IsNullOrWhiteSpace(_tgBotToken) || string.IsNullOrWhiteSpace(_tgChatId)) return;

            var url = $"https://api.telegram.org/bot{_tgBotToken}/sendMessage";
            var payload = $"chat_id={Uri.EscapeDataString(_tgChatId)}&text={Uri.EscapeDataString(text)}&parse_mode=HTML";
            using var content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded");
            try { using var _ = await _http.PostAsync(url, content).ConfigureAwait(false); }
            catch { }
        }

        private void TriggerAlerts(int bar, bool isUp, decimal price)
        {
            if (!_enableAlerts) return;

            if (isUp)
            {
                if (!_alertOnCrossUp) return;
                if (bar - _lastAlertUpBar < _alertCooldownBars) return;
                _lastAlertUpBar = bar;
            }
            else
            {
                if (!_alertOnCrossDown) return;
                if (bar - _lastAlertDownBar < _alertCooldownBars) return;
                _lastAlertDownBar = bar;
            }

            var side = isUp ? "ALCISTA" : "BAJISTA";
            var instr = InstrumentInfo?.Instrument ?? "Instrumento";
            var tf = ChartInfo?.TimeFrame?.ToString() ?? "";
            var msg = $"🚨 Cruce {side} GenialLine\n{instr} {tf}\nPrecio: {price.ToString(CultureInfo.InvariantCulture)}";

            Task.Run(() => SendTelegramAsync(msg));
        }
    }
}






