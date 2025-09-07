# 📈 ATAS FourSixEight Indicator & Strategy System

Sistema completo de indicador y estrategia automatizada para la plataforma ATAS, basado en el análisis de Williams %R y múltiples indicadores técnicos.

## 🗂️ Estructura del Proyecto

```
atas_strategy_yes/
├── Indicators/
│   └── FourSixEightIndicator.cs          # Indicador principal 468
├── Strategies/
│   └── FourSixEightStrategy.cs           # Estrategia automatizada 468
├── Shared/
│   ├── Bus.cs                           # Sistema de comunicación (TODO)
│   ├── DebugLog.cs                      # Sistema de logging
│   └── SignalModels.cs                  # Modelos de señales
├── MyAtasIndicator.csproj               # Configuración del proyecto
└── README.md                           # Este archivo
```

## 📊 FourSixEightIndicator - Análisis Técnico Completo

### Características Principales

#### 🎯 **Señales de Trading Clave**
- **Cambio Alcista** (Azul Oscuro): Williams %R cruza **ARRIBA** de -50
- **Cambio Bajista** (Magenta): Williams %R cruza **ABAJO** de -50

#### 📈 **Indicadores Incluidos**
1. **Williams %R** (período configurable, default: 40)
2. **ADX** (suavizado configurable, default: 7)
3. **EMA 8** y **Wilder 8** (zona de corrección)
4. **Genial Line** (línea personalizada con SMA y rango)
5. **Tunnel Domenec** (EMAs: 123, 188, 416, 618, 882, 1223)
6. **Divergencias** (alcistas y bajistas)

#### 🎨 **Sistema de Coloreado de Velas**
- **12 tipos diferentes** de condiciones de mercado
- Colores configurables para cada condición
- Detección automática de tendencias

### Parámetros Configurables

| Categoría | Parámetro | Default | Descripción |
|-----------|-----------|---------|-------------|
| **Corrección** | EMA 8 Len | 8 | Período EMA 8 |
| **Genial Line** | Velas Banda | 20 | Período para cálculo |
| **Genial Line** | Desviación | 3.14159 | Factor de desviación |
| **Coloreado** | Período Williams %R | 40 | Período del oscilador |
| **Coloreado** | ADX Smoothing | 7 | Suavizado del ADX |
| **Divergencias** | Lookback | 25 | Períodos para buscar divergencias |

### Señales del Indicador

#### 🟢 **Señales Alcistas**
```csharp
// Cambio Alcista - WPR cruza arriba de -50
bool crossOverWpr = bar > 0 && wpr > -50m && prevWpr <= -50m;
// Color: ColCambioAlcista (azul oscuro por defecto)
```

#### 🔴 **Señales Bajistas**
```csharp
// Cambio Bajista - WPR cruza abajo de -50
bool crossUnderWpr = bar > 0 && wpr < -50m && prevWpr >= -50m;
// Color: ColCambioBajista (magenta por defecto)
```

## 🤖 FourSixEightStrategy - Estrategia Automatizada

### Estado Actual: **FASE 2 - TRADING AUTOMÁTICO** ✅

La estrategia **detecta señales** del indicador y **ejecuta órdenes automáticas** con gestión completa de riesgo.

### Características Implementadas

#### ✅ **Sistema de Detección**
- Replica exactamente la lógica del indicador
- Detecta cruces de Williams %R en nivel -50
- Anti-duplicación de señales
- Opera solo en tiempo real (última barra)

#### ✅ **Parámetros Configurables**
```csharp
// Configuración de Trading
[Display(Name = "Volumen por Orden")]
public int OrderVolume { get; set; } = 1;

[Display(Name = "Habilitar Trading")]
public bool EnableTrading { get; set; } = false;

// Gestión de Riesgo
[Display(Name = "Stop Loss (Ticks)")]
public int StopLossTicks { get; set; } = 50;

[Display(Name = "Take Profit (Ticks)")]
public int TakeProfitTicks { get; set; } = 100;

// Configuración de Indicador
[Display(Name = "Período Williams %R")]
public int WilliamsPeriod { get; set; } = 40;

[Display(Name = "Habilitar Logs")]
public bool EnableLogging { get; set; } = true;
```

#### ✅ **Lógica de Trading Automático**
```csharp
// Señal Alcista - Entrada LONG
if (crossOverWpr && !_lastSignalBullish)
{
    ProcessBullishSignal(bar);
    if (EnableTrading && !_hasPosition && _currentOrder == null)
    {
        ExecuteBuyOrder(); // Market Order BUY
    }
}

// Señal Bajista - Entrada SHORT
if (crossUnderWpr && !_lastSignalBearish)
{
    ProcessBearishSignal(bar);
    if (EnableTrading && !_hasPosition && _currentOrder == null)
    {
        ExecuteSellOrder(); // Market Order SELL
    }
}
```

### ✅ **FASE 2 COMPLETADA - TRADING AUTOMÁTICO**

#### **Sistema de Trading Implementado**
1. **✅ Entrada en Mercado**
   - Órdenes Market BUY/SELL automáticas
   - Volumen configurable por operación
   - Ejecución inmediata al detectar señal
   - Control de posición única activa

2. **✅ Gestión de Riesgo**
   - Stop Loss automático (configurable en ticks)
   - Take Profit automático (configurable en ticks)
   - Una sola posición simultánea máximo
   - Cancelación de órdenes al cerrar posición

3. **✅ Lógica de Salida**
   - Por Stop Loss ejecutado
   - Por Take Profit ejecutado
   - Gestión automática de estados de órdenes
   - Limpieza de posición al cerrar

#### **🎯 Flujo de Trading Automático**
```
🔵 Señal Alcista (WPR > -50)
    ↓
💰 Orden Market BUY
    ↓
📉 Stop Loss (-50 ticks) + 📈 Take Profit (+100 ticks)
    ↓
⏳ Espera ejecución SL o TP
    ↓
🔄 Sistema listo para nueva señal
```

## 🚀 CÓMO USAR EL SISTEMA COMPLETO

### 📋 **PASO 1: Recompilar con FASE 2**
```bash
cd "C:\Users\AlexJ\Desktop\atas_strategy_yes"
dotnet build MyAtasIndicator.csproj -c Release
```

### 🔄 **PASO 2: Reiniciar ATAS**
- Cierra ATAS completamente
- Vuelve a abrirlo para cargar la versión actualizada

### 📊 **PASO 3: Cargar Indicador**
1. Abre ATAS y ve a un chart
2. Clic derecho → "Add Indicator" o "Añadir Indicador"
3. Busca: "04 + 06 + 08 (ATAS) - Minimal" o "FourSixEight"
4. Aplica al chart

### 🤖 **PASO 4: Cargar Estrategia**
1. En el panel de "Strategies" o "Estrategias"
2. Clic "Add Strategy" → Busca: "468 Strategy - Simple"
3. Aplica la estrategia

### ⚙️ **PASO 5: Configurar Parámetros**

#### **🛡️ Configuración de Seguridad (Recomendada)**
| Parámetro | Valor Inicial | Descripción |
|-----------|---------------|-------------|
| **Habilitar Trading** | ❌ **false** | Para pruebas sin riesgo |
| Volumen por Orden | 1 | Contratos por operación |
| Stop Loss (Ticks) | 50 | Pérdida máxima permitida |
| Take Profit (Ticks) | 100 | Objetivo de ganancia |
| Período Williams %R | 40 | Debe coincidir con indicador |
| Habilitar Logs | ✅ true | Para monitorear señales |

#### **⚠️ Configuración de Trading Real (Solo cuando estés seguro)**
| Parámetro | Valor Sugerido | Descripción |
|-----------|----------------|-------------|
| **Habilitar Trading** | ✅ **true** | ⚠️ ACTIVA ÓRDENES REALES |
| Volumen por Orden | 1-3 | Según tu capital |
| Stop Loss (Ticks) | 30-100 | Según tolerancia al riesgo |
| Take Profit (Ticks) | 60-200 | Ratio 1:2 o 1:3 |

### 🧪 **PASO 6: Modo de Prueba**
1. **Primero** deja `Habilitar Trading = false`
2. Ejecuta en replay de ATAS
3. Observa las velas coloreadas:
   - 🔵 **Azul Oscuro**: Señal Alcista (WPR > -50)
   - 🟣 **Magenta**: Señal Bajista (WPR < -50)
4. Revisa logs para ver mensajes de la estrategia

### 💰 **PASO 7: Trading Real** ⚠️
**Solo cuando hayas confirmado que funciona perfectamente:**
1. Cambia `Habilitar Trading = true`
2. Configura SL/TP según tu plan de trading
3. **Monitorea de cerca** las primeras operaciones
4. Ajusta parámetros según resultados

### 📊 **Identificar las Señales en ATAS**

#### **🎯 Señales del Indicador**
- **Líneas EMA/Wilder**: Zona de corrección
- **Genial Line**: Línea de tendencia personalizada
- **Velas Coloreadas**: 12 condiciones de mercado diferentes
- **Túneles**: Zonas de EMAs largas (123, 188, 416, etc.)

#### **🚦 Señales de la Estrategia**
Busca estos colores específicos:
- 🔵 **Azul Oscuro**: CAMBIO ALCISTA → Sistema ejecutará BUY
- 🟣 **Magenta**: CAMBIO BAJISTA → Sistema ejecutará SELL

### 📈 **Monitoreo de Operaciones**

#### **Lo que verás con `EnableTrading = false`:**
- ✅ Detección de señales en logs
- ✅ Velas coloreadas en chart
- ❌ No se ejecutan órdenes

#### **Lo que verás con `EnableTrading = true`:**
- ✅ Detección de señales en logs
- ✅ Órdenes Market automáticas
- ✅ Stop Loss y Take Profit automáticos
- ✅ Gestión completa de posición

## 🛠️ Configuración de Desarrollo

### Requisitos
- **Visual Studio 2022** (Community o superior)
- **.NET 8.0 Windows**
- **ATAS Platform** instalado

### Referencias DLL Requeridas
```xml
<Reference Include="ATAS.Indicators">
    <HintPath>C:\Program Files (x86)\ATAS Platform\ATAS.Indicators.dll</HintPath>
</Reference>
<Reference Include="ATAS.Strategies">
    <HintPath>C:\Program Files (x86)\ATAS Platform\ATAS.Strategies.dll</HintPath>
</Reference>
<Reference Include="ATAS.Types">
    <HintPath>C:\Program Files (x86)\ATAS Platform\ATAS.Types.dll</HintPath>
</Reference>
```

### Auto-Deploy Configurado
El proyecto incluye auto-deploy después de compilar:

```xml
<!-- Para Indicadores -->
<AtasIndicatorsDir>$(APPDATA)\ATAS\Indicators</AtasIndicatorsDir>

<!-- Para Estrategias -->  
<AtasStrategiesDir>$(APPDATA)\ATAS\Strategies</AtasStrategiesDir>
```

### Comandos de Compilación
```bash
# Compilar proyecto
dotnet build MyAtasIndicator.csproj -c Release

# Los archivos se despliegan automáticamente a:
# Indicadores: %APPDATA%\ATAS\Indicators\
# Estrategias: %APPDATA%\ATAS\Strategies\
```

## 📋 Checklist de Estado

### ✅ **Completado**
- [x] Indicador FourSixEight funcional
- [x] Sistema de detección de señales
- [x] 12 condiciones de mercado implementadas
- [x] Coloreado de velas automático
- [x] Sistema de divergencias
- [x] Estructura básica de estrategia
- [x] Detección de señales en estrategia
- [x] Auto-deploy configurado
- [x] Compilación sin errores

### ✅ **FASE 2 - COMPLETADA**
- [x] Implementación de órdenes Market
- [x] Sistema de entrada/salida automático
- [x] Gestión de riesgo (SL/TP)
- [x] Control de posición única
- [x] Manejo de eventos de órdenes
- [x] Sistema de cancelación automática
- [x] Estados de trading completos

### 🧪 **Testing y Optimización**
- [ ] Testing extensivo en replay mode
- [ ] Backtesting con datos históricos
- [ ] Optimización de parámetros SL/TP
- [ ] Testing con dinero real (pequeñas cantidades)
- [ ] Logging avanzado y métricas
- [ ] Alertas y notificaciones
- [ ] Dashboard de rendimiento

## 🎯 Objetivos de la Estrategia

### **Filosofía de Trading**
1. **Simplicidad**: Basada únicamente en cambios de tendencia del Williams %R
2. **Baja Latencia**: Ejecución inmediata al detectar señal
3. **Gestión de Riesgo**: Parámetros claros y configurables
4. **Siguiendo Cánones ATAS**: Implementación profesional según estándares

### **Timing de Entrada**
- **Detección**: Cuando Williams %R cruza -50
- **Confirmación**: Al cierre completo de la vela
- **Ejecución**: Al abrir la siguiente vela (Market Order)

### **Dirección de Trading**
- **LONG**: Williams %R cruza **arriba** de -50 (cambio alcista)
- **SHORT**: Williams %R cruza **abajo** de -50 (cambio bajista)

## ⚠️ CONSIDERACIONES IMPORTANTES DE TRADING

### 🛡️ **Seguridad y Gestión de Riesgo**

- **💰 Capital de Riesgo**: Usa SOLO dinero que puedas permitirte perder completamente
- **🧪 Backtesting Obligatorio**: Prueba extensivamente en replay antes de dinero real
- **📊 Parámetros Conservadores**: Comienza con SL/TP conservadores y ajústalos gradualmente
- **👀 Monitoreo Activo**: Supervisa las primeras 10-20 operaciones manualmente
- **📈 Volumenes Pequeños**: Comienza con 1 contrato hasta confirmar rentabilidad

### ⚡ **Flujo de Trading del Sistema**

```
🔍 Williams %R cruza -50
       ↓
🚦 Confirmación de señal
       ↓
📋 ¿EnableTrading = true?
   ↙️         ↘️
 ❌ No        ✅ Sí
 Log only    Market Order
             ↓
       🎯 SL + TP colocados
             ↓
       ⏳ Esperar ejecución
             ↓
       🔄 Cerrar posición
             ↓
       🆕 Lista para nueva señal
```

### 📊 **Configuración Recomendada por Experiencia**

#### **🔰 Principiante**
- Volumen: 1 contrato
- Stop Loss: 100-150 ticks
- Take Profit: 150-200 ticks
- EnableTrading: false (solo observar)

#### **📈 Intermedio**
- Volumen: 1-2 contratos
- Stop Loss: 50-100 ticks
- Take Profit: 100-150 ticks
- EnableTrading: true (con supervisión)

#### **🏆 Avanzado**
- Volumen: Según capital y plan
- Stop Loss: 30-80 ticks
- Take Profit: 60-120 ticks
- Ratio SL:TP = 1:2 mínimo

## 🚨 PROBLEMAS CRÍTICOS DETECTADOS - PENDIENTE SOLUCIÓN

### ⚠️ **Estado Actual: FALLOS EN EJECUCIÓN DE ÓRDENES**

**Fecha Detección**: 7 de Septiembre 2025  
**Severidad**: 🔴 CRÍTICA  
**Estado**: 🚧 REQUIERE DEPURACIÓN INMEDIATA  

#### **🔍 Problemas Identificados:**

#### **🐛 PROBLEMA 1: LÓGICA DE FLAGS DEFECTUOSA**
```csharp
// Código problemático actual:
if (crossOverWpr && !_lastSignalBullish)
    ProcessBullishSignal(bar);

if (!crossOverWpr) _lastSignalBullish = false; // ❌ SE RESETEA INMEDIATAMENTE
```
**Síntoma**: Los flags se resetean inmediatamente después de la detección, impidiendo redetecciones y causando comportamiento errático.

#### **🐛 PROBLEMA 2: DESINCRONIZACIÓN TEMPORAL**
- **Indicador**: Calcula Williams %R al **cierre de vela**
- **Estrategia**: Calcula Williams %R en **tiempo real** (tick by tick)
- **Resultado**: Valores diferentes entre indicador y estrategia

#### **🐛 PROBLEMA 3: DETECCIÓN vs EJECUCIÓN**
**Síntomas observados**:
- ✅ Vela **verde fosforito** (señal ALCISTA) detectada por indicador
- ❌ Estrategia **NO ejecuta BUY** inmediatamente
- ❌ **5 velas después** ejecuta **SELL** (dirección opuesta)
- 🔴 **Panel trading**: Muestra posición SHORT (-2) cuando debería ser LONG

#### **📊 Evidencia Visual:**
- **Imagen 05**: Vela verde fosforito sin ejecución de compra, seguida de venta errónea
- **Comportamiento**: Sistema detecta correctamente señales del indicador pero no las ejecuta o las ejecuta mal

### **🔧 PLAN DE CORRECCIÓN PROPUESTO:**

#### **FASE 1: DEPURACIÓN DETALLADA** 📝
- [ ] Implementar sistema de logging completo para rastrear:
  - Detección de señales en tiempo real
  - Valores de Williams %R (actual vs anterior)
  - Estados de flags en cada tick
  - Decisiones de ejecución de órdenes
  - Timestamps exactos de cada evento

#### **FASE 2: CORRECCIÓN DE LÓGICA** 🔧
- [ ] Eliminar lógica de flags problemática
- [ ] Implementar detección directa sin estados persistentes
- [ ] Sincronizar cálculo Williams %R con el indicador
- [ ] Asegurar ejecución solo al cierre de vela

#### **FASE 3: VALIDACIÓN VISUAL** 🎯
- [ ] Agregar marcas en el chart para visualizar detecciones
- [ ] Implementar indicadores de debug en tiempo real
- [ ] Crear sistema de alertas para depuración

### **🎯 PRÓXIMOS PASOS CRÍTICOS:**

1. **📊 LOGGING INMEDIATO**: Implementar logs detallados para identificar punto exacto de fallo
2. **🔍 ANÁLISIS TEMPORAL**: Determinar timing exacto de detección vs ejecución
3. **⚡ CORRECCIÓN URGENTE**: Arreglar lógica de flags y sincronización
4. **✅ VALIDACIÓN**: Probar exhaustivamente antes de uso en producción

### **⚠️ ADVERTENCIA DE SEGURIDAD:**
**🔴 NO USAR EN TRADING REAL** hasta resolver estos problemas críticos. El sistema puede:
- Ejecutar órdenes en dirección opuesta
- Perder señales válidas
- Ejecutar con timing incorrecto
- Causar pérdidas financieras

---

## 🔧 Troubleshooting

### Problemas Técnicos

#### **1. Estrategia no aparece en ATAS**
```bash
# Verificar archivos desplegados
ls "C:\Users\[USER]\AppData\Roaming\ATAS\Strategies\"

# Recompilar
cd "C:\Users\AlexJ\Desktop\atas_strategy_yes"
dotnet build MyAtasIndicator.csproj -c Release

# Reiniciar ATAS completamente
```

#### **2. Sistema no ejecuta órdenes**
- ✅ Verificar `EnableTrading = true`
- ✅ Confirmar que no hay posición activa
- ✅ Revisar logs para detectar errores
- ✅ Verificar conexión con broker
- ✅ Confirmar saldo suficiente en cuenta

#### **3. Señales no se detectan**
- ✅ Verificar período Williams %R = 40
- ✅ Confirmar historial de datos suficiente (100+ barras)
- ✅ Revisar timeframe del chart
- ✅ Verificar que indicador está aplicado

#### **4. Stop Loss / Take Profit no funciona**
- ✅ Verificar que SL/TP > 0 ticks
- ✅ Confirmar que broker acepta órdenes stop
- ✅ Revisar configuración de cuenta
- ✅ Verificar logs de error de órdenes

### Comandos de Verificación

```bash
# Ver archivos desplegados
ls "C:\Users\AlexJ\AppData\Roaming\ATAS\Indicators\"
ls "C:\Users\AlexJ\AppData\Roaming\ATAS\Strategies\"

# Recompilar todo
cd "C:\Users\AlexJ\Desktop\atas_strategy_yes"
dotnet clean
dotnet build MyAtasIndicator.csproj -c Release

# Verificar logs de ATAS
ls "C:\Users\AlexJ\AppData\Roaming\ATAS\Logs\"
```

## 📝 Notas para Claude (Sesiones Futuras)

### **Contexto Técnico**
- El indicador está **completo y funcional** ✅
- La estrategia está en **FASE 2 - Trading automático completo** ✅
- Sistema **LISTO PARA TRADING REAL** con todas las protecciones ✅

### **Archivos Clave**
- **Indicador**: `Indicators/FourSixEightIndicator.cs` (líneas 498-499 para señales)
- **Estrategia**: `Strategies/FourSixEightStrategy.cs`
  - Líneas 114-118: Ejecución BUY automática
  - Líneas 131-135: Ejecución SELL automática
  - Líneas 138-184: Métodos ExecuteBuyOrder/ExecuteSellOrder
  - Líneas 186-270: Stop Loss y Take Profit automáticos
  - Líneas 276-368: Gestión completa de eventos de órdenes
- **Señales**: Williams %R cruces en -50 (cambio alcista/bajista)

### **Estado del Sistema**
- ✅ Compila sin errores
- ✅ Auto-deploy funcional
- ✅ Indicador probado en ATAS
- ✅ Estrategia: FASE 2 completada - Trading automático
- ✅ OpenOrder() implementado para Market Orders
- ✅ Gestión completa de posición y órdenes
- ✅ Sistema Stop Loss / Take Profit operativo

### **Sistema de Trading Listo**
**FASE 2 COMPLETADA** - El sistema puede:
1. ✅ Detectar señales automáticamente (Williams %R cruces en -50)
2. ✅ Ejecutar órdenes Market instantáneas (BUY/SELL)
3. ✅ Gestionar Stop Loss y Take Profit automáticamente
4. ✅ Controlar una posición activa por vez
5. ✅ Manejar todos los estados de órdenes (Placed, Filled, Cancelled, Rejected)
6. ✅ Cancelar órdenes pendientes al cerrar posición
7. ✅ Manejo completo de errores y excepciones
8. ✅ Control de seguridad con EnableTrading switch

### **Implementación Técnica Completada**
- **ExecuteBuyOrder()**: Crea y ejecuta orden Market BUY
- **ExecuteSellOrder()**: Crea y ejecuta orden Market SELL  
- **PlaceStopLoss()**: Calcula y coloca SL automático
- **PlaceTakeProfit()**: Calcula y coloca TP automático
- **OnOrderChanged()**: Maneja estados de órdenes en tiempo real
- **ClosePosition()**: Limpia posición y cancela órdenes pendientes

### **⚠️ ESTADO ACTUAL DE PRODUCCIÓN**
**🚨 SISTEMA NO ESTÁ LISTO PARA TRADING REAL**

#### **🔴 PROBLEMAS CRÍTICOS ACTIVOS:**
1. 🧪 **Replay mode**: ❌ FALLOS en ejecución de órdenes
2. 📊 **Detección de señales**: ⚠️ DESINCRONIZADA con indicador  
3. ⚙️ **Lógica de trading**: 🐛 FLAGS defectuosos
4. 💰 **Trading real**: 🔴 **PROHIBIDO hasta corrección**
5. 📈 **Rendimiento**: ❌ Órdenes opuestas a señales

---

## 📝 Notas Técnicas para Claude (Sesiones Futuras)

### **🚨 ESTADO CRÍTICO - REQUIERE ATENCIÓN INMEDIATA**

#### **Contexto de Emergencia**
- **Fecha del problema**: 7 Septiembre 2025
- **Evidencia**: Archivo `references/05.png`
- **Síntoma principal**: Vela verde fosforito → Sistema ejecuta SELL (opuesto)

#### **Problemas Específicos Identificados**

**🐛 BUG 1: Lógica de Flags (FourSixEightStrategy.cs:95-97)**
```csharp
// PROBLEMÁTICO - Se resetea inmediatamente:
if (!crossOverWpr) _lastSignalBullish = false;
if (!crossUnderWpr) _lastSignalBearish = false;
```

**🐛 BUG 2: Desincronización Williams %R**
- Indicador: `GetPrevCached(kWPR, bar-1)` (cacheado)
- Estrategia: `GetPrevCached(kWPR, bar-1)` (implementado pero valores diferentes)

**🐛 BUG 3: Timing Real-time vs Cierre**
- Indicador: Calcula al cierre de vela
- Estrategia: Calcula en cada tick

#### **Archivos Críticos Afectados**
- **FourSixEightStrategy.cs**: Líneas 74-98 (OnCalculate)
- **FourSixEightStrategy.cs**: Líneas 104-136 (ProcessBullishSignal/ProcessBearishSignal)  
- **Cache System**: Líneas 375-395 (implementado pero no funcional)

#### **Próxima Implementación URGENTE**
1. **📊 LOGGING DETALLADO**: Agregar debug completo en OnCalculate
2. **🔧 CORREGIR FLAGS**: Eliminar lógica problemática de reseteo
3. **⏰ SINCRONIZAR TIMING**: Calcular solo al cierre de vela
4. **🎯 VALIDAR DIRECCIONES**: Asegurar BUY→verde, SELL→roja

#### **Evidencia de Fallos**
- `references/05.png`: Vela verde fosforito sin BUY, seguida de SELL errónea
- Position panel: SHORT (-2) cuando debería ser LONG
- Timing: 5 velas de retraso en ejecución

---

**Última actualización**: 7 Septiembre 2025 (FALLO CRÍTICO DETECTADO)  
**Versión**: 2.0-BUGFIX-REQUIRED (Fase 2 - Con Fallos Críticos)  
**Estado**: 🚨 **SISTEMA DEFECTUOSO - REQUIERE CORRECCIÓN INMEDIATA**

**Funcionalidades**: ✅ Indicador funcional, ❌ Estrategia con fallos críticos en ejecución

**Próximo Paso OBLIGATORIO**: Depuración completa del sistema de detección y ejecución de señales.