# 🔍 ANÁLISIS FORENSE: Cancelación Inesperada de Brackets (Zombie Cancel)

**Fecha**: 2025-10-03 10:42:15-17
**Trade**: #2 - SELL 3 contratos @ 19961.50
**Problema**: Brackets SL/TP cancelados 2 segundos después de crearse, sin tocar precio

---

## 📊 RESUMEN EJECUTIVO

### **Problema Identificado**
Los brackets de Stop Loss y Take Profit fueron **cancelados automáticamente por el sistema** debido a una detección incorrecta de "zombie orders" causada por **latencia en el reporte de posición del broker**.

### **Root Cause**
```
ZOMBIE CANCEL: broker=0 but live orders present -> cancelling...
```

El sistema detectó:
- Broker reporta: `TM.Position.net = 0` (FLAT)
- Strategy tracking: `netByFills = -3` (SHORT 3 contratos)
- Órdenes vivas: 2 brackets (SL + TP)
- **Conclusión errónea**: Las órdenes son "zombies" → CANCELAR

### **Causa Real**
**Latencia del broker** en actualizar `TM.Position.net` después del fill de entrada. Durante ~2 segundos, el broker reporta `net=0` cuando la posición real es `-3`.

---

## 📋 CRONOLOGÍA DETALLADA

### **FASE 1: ENTRADA A MERCADO (10:42:15.256)**

```
[10:42:15.256] MARKET ORDER SENT: SELL 3 at N+1 (bar=5276)
[10:42:15.257] OnOrderChanged: 468ENTRY:084215 status=Filled qty=3
[10:42:15.259] Tracked fill: 468ENTRY:084215 = -3 (type: 468ENTRY)
[10:42:15.262] Position entry: Price=19961,50 Qty=3 Dir=-1 [Trade #2]
```

**Resultado FASE 1:**
- ✅ Entry orden SELL 3 ejecutada
- ✅ netByFills = -3 (SHORT 3 contratos)
- ✅ Position tracking iniciado

---

### **FASE 2: CREACIÓN DE BRACKETS (10:42:15.265-278)**

```
[10:42:15.264] POST-FILL CHECK: net=3 _entryDir=-1 status=Filled
[10:42:15.264] BRACKET-PRICES: entry~19962,00 sl=19975,00 risk=13,00
[10:42:15.265] RegisterChildSign: 468SL:084215:b94042 = 1 (dir=-1)
[10:42:15.266] RegisterChildSign: 468TP:084215:b94042 = 1 (dir=-1)
[10:42:15.267] BRACKETS ATTACHED: tp=1 sl=1 total=3 (SL=19975,00 | TPs=19949,00)
[10:42:15.268] BRACKETS: SL=19975,00 | TPs=19949,00 | Split=[3] | Total=3

[10:42:15.274] OnNewOrder: id=8 '468SL:084215:b94042' type=Stop status=Placed qty=3
[10:42:15.275] OnOrderChanged: 468SL:084215:b94042 status=Placed side=Buy qty=3
[10:42:15.277] OnNewOrder: id=9 '468TP:084215:b94042' type=Limit status=Placed qty=3
[10:42:15.278] OnOrderChanged: 468TP:084215:b94042 status=Placed side=Buy qty=3
```

**Brackets Creados:**
- ✅ **SL (id=8)**: BUY 3 @ 19975.00 (protege SHORT)
- ✅ **TP (id=9)**: BUY 3 @ 19949.00 (objetivo SHORT)
- ✅ Ambos en estado `Placed` (activos)

---

### **FASE 3: LATENCIA DEL BROKER (10:42:15.276-17.600)**

```
[10:42:15.276] TM.Position net=0 avg=19961,50  ← BROKER REPORTA NET=0 (INCORRECTO)
[10:42:15.299] TM.Position net=0 avg=19961,50
[10:42:15.316] TM.Position net=0 avg=19961,50
[10:42:15.337] TM.Position net=0 avg=19961,50
... (continúa durante 2 segundos)
[10:42:17.588] TM.Position net=0 avg=19961,50

PERO AL MISMO TIEMPO:
[10:42:15.285] 468/POS: GetNetPosition via Cache+Fills (live=True): -3
[10:42:15.289] 468/POS: GetNetPosition via Cache+Fills (live=True): -3
... (Strategy sabe que net=-3 usando fills tracking)
```

**Situación Crítica:**
- ❌ Broker: `net=0` (latencia, no actualizado)
- ✅ Strategy: `netByFills=-3` (correcto vía tracking)
- ⚠️ Sistema de guards detecta discrepancia

---

### **FASE 4: SEÑAL CONTRARIA (10:42:15.507)**

```
[10:42:15.504] GENIAL CROSS detected: Up at bar=5276 (trigger=WPR_11_12)
[10:42:15.506] GENIAL CROSS detected: Up at bar=5276 (trigger=GenialLine)
[10:42:15.507] PUBLISH GENIAL SIGNAL: bar=5276 dir=BUY uid=ae723fc7

[10:42:15.528] SIGNAL_CHECK: bar=5276 dir=BUY (new signal)
[10:42:15.529] IGNORE signal at N=5276 (close did not confirm; avoids intrabar flip-flop)
```

**Nueva señal BUY** apareció en la misma barra de entrada, pero fue **ignorada correctamente** (protección anti-flip-flop).

---

### **FASE 5: 🚨 ZOMBIE CANCEL (10:42:17.600)**

```
[10:42:17.594] OnCalculate: bar=5278 pending=YES tradeActive=True
[10:42:17.594] GetNetPosition via Cache+Fills (live=True): -3
[10:42:17.594] STATE PING: net=-3 activeOrders=2 brkPlaced=True

[10:42:17.595] PROCESSING PENDING @N+1: bar=5278, execBar=5278
[10:42:17.596] First-tick missed but within tolerance -> proceed
[10:42:17.597] CONF#1 (GL slope @N+1) -> OK
[10:42:17.598] CONF#2 (EMA8 vs W8 @N+1) -> OK

[10:42:17.599] GUARD OnlyOnePosition: portfolio=0 positions=0 net=-3 fills=-3 active=True
[10:42:17.600] 🚨 ZOMBIE CANCEL: broker=0 but live orders present -> cancelling...

[10:42:17.606] OnOrderChanged: id=8 '468SL:084215:b94042' status=Canceled
[10:42:17.608] OnOrderChanged: 468SL:084215:b94042 status=Canceled state=Done
[10:42:17.608] Removed fill tracking: 468SL:084215:b94042

[10:42:17.613] OnOrderChanged: id=9 '468TP:084215:b94042' status=Canceled
[10:42:17.615] OnOrderChanged: 468TP:084215:b94042 status=Canceled state=Done
[10:42:17.615] Removed fill tracking: 468TP:084215:b94042
```

**¿Qué pasó?**

El sistema estaba procesando la señal BUY pendiente (N+1 execution) y verificó los guards. El guard "OnlyOnePosition" detectó:

```csharp
portfolio = 0  (TM.Position.Portfolio.NetQty)
positions = 0  (TM.Position.Positions.Count)
net = -3       (via Cache+Fills tracking)
fills = -3     (via fills tracking)
activeOrders = 2 (SL + TP)
```

**Lógica del guard:**
- Si `portfolio=0` Y `positions=0` (broker dice FLAT)
- PERO `activeOrders > 0` (hay brackets vivos)
- ENTONCES son "zombie orders" (órden huérfanas) → CANCELAR

**Error fatal:**
- El broker **SÍ tenía** la posición SHORT 3
- Pero por **latencia**, reportaba `net=0`
- Los brackets **NO eran zombies**, eran legítimos

---

## 📊 CONTABILIDAD DE CONTRATOS

### **Al momento del Entry**
```
Entry ORDER: SELL 3 @ 19961.50
├─ Filled: SELL 3
├─ netByFills: -3 (SHORT 3)
└─ TM.Position: net=0 (LATENCIA, incorrecto)
```

### **Brackets Creados**
```
SL (id=8): BUY 3 @ 19975.00
TP (id=9): BUY 3 @ 19949.00
Total protection: 3 contratos (correcto)
```

### **Después de Zombie Cancel**
```
Posición real: SHORT 3 contratos @ 19961.50
SL: CANCELADO ❌
TP: CANCELADO ❌
Protección: NINGUNA ⚠️
```

### **Estado Final**
```
Contratos en posición: -3 (SHORT sin protección)
Brackets activos: 0
Riesgo: ILIMITADO (no hay SL)
```

---

## 🐛 ROOT CAUSE ANALYSIS

### **Causa Directa**
Guard "ZOMBIE CANCEL" disparó cancelación basándose en `TM.Position.net=0`.

### **Causa Raíz**
**Latencia del broker** en actualizar `TM.Position` después de fills:
- Fill ocurre: `10:42:15.257`
- Strategy detecta: `10:42:15.259` (via fills tracking)
- Broker actualiza `TM.Position`: **>2 segundos después** ⚠️

### **Por qué el guard falló**
El guard asume que si `TM.Position.net=0` es confiable. Pero en brokers con latencia alta, puede ser **temporalmente incorrecto**.

### **Timing del problema**
```
T+0.000s: Entry fill
T+0.002s: Strategy tracking OK (net=-3)
T+0.009s: Brackets creados
T+0.018s: Broker aún reporta net=0
T+2.343s: Nueva señal pendiente ejecuta N+1
T+2.344s: Guard verifica → broker SIGUE en net=0
T+2.344s: ZOMBIE CANCEL disparado ❌
```

---

## 🔧 SOLUCIÓN PROPUESTA

### **Opción 1: Confiar en netByFills durante grace period**
```csharp
// En lugar de usar TM.Position inmediatamente, usar fills tracking
var brokerNet = TM.Position?.Portfolio?.NetQty ?? 0;
var fillsNet = NetByFills();

// Si hay discrepancia Y estamos en grace period → confiar en fills
if (brokerNet == 0 && fillsNet != 0 && IsInPostEntryGracePeriod())
{
    // NO cancelar brackets, hay latencia del broker
    LogW("ZOMBIE CHECK: Broker reports flat but fills tracking shows position, skipping cancel (grace period)");
    return;
}
```

### **Opción 2: Aumentar grace period para zombie checks**
```csharp
// No verificar zombie orders hasta X segundos después del último fill
var timeSinceLastFill = DateTime.Now - _lastFillTime;
if (timeSinceLastFill.TotalMilliseconds < 5000) // 5 segundos
{
    // Demasiado pronto para verificar zombies
    return;
}
```

### **Opción 3: Verificar consistencia antes de cancelar**
```csharp
// Antes de cancelar, verificar si las órdenes son coherentes con fills
if (brokerNet == 0 && liveOrders > 0)
{
    // Verificar si los fills recientes justifican las órdenes
    if (HasRecentFills(2000)) // fills en últimos 2 segundos
    {
        LogW("ZOMBIE CHECK: Recent fills detected, skipping cancel (likely broker latency)");
        return;
    }

    // Si no hay fills recientes, entonces SÍ son zombies
    CancelZombieOrders();
}
```

---

## 🎯 RECOMENDACIONES

### **Inmediato (Hot Fix)**
1. **Deshabilitar zombie cancel** durante post-entry grace period (primeros 3-5 segundos)
2. **Loggear advertencia** cuando broker reporta net=0 pero fills tracking muestra posición
3. **No cancelar brackets** si `netByFills != 0`, independientemente de lo que diga el broker

### **Corto Plazo**
1. **Implementar verificación de consistencia** entre broker y fills antes de cancelar
2. **Aumentar grace period** del zombie check a 5 segundos post-fill
3. **Añadir logging** detallado cuando se detecten discrepancias broker vs fills

### **Medio Plazo**
1. **Unified position tracking**: Consolidar toda la lógica de posición en un solo lugar
2. **Broker latency detection**: Medir y adaptar grace periods según latencia observada
3. **Zombie detection mejorada**: Solo cancelar órdenes que sean verdaderos zombies (sin fills recientes asociados)

---

## 📈 IMPACTO

### **Trade Afectado**
- **Trade #2**: SELL 3 @ 19961.50
- **Sin protección**: SL cancelado, posición expuesta a riesgo ilimitado
- **Estado actual**: SHORT 3 contratos sin SL ni TP

### **Riesgo**
- ⚠️ **ALTO**: Posición sin stop loss
- ⚠️ Si el precio sube, pérdidas ilimitadas
- ⚠️ No hay salida automática programada

### **Mitigación Manual Necesaria**
Usuario debe:
1. Cerrar manualmente la posición SHORT 3
2. O colocar SL/TP manual vía ChartTrader
3. Monitorear precio activamente

---

**Análisis completado**: 2025-10-03 10:56
**Analista**: Claude (Forensic Analysis)
**Archivo**: FORENSIC_ZOMBIE_CANCEL.md
**Severidad**: 🔴 CRÍTICA
**Estado**: IDENTIFICADO - Pendiente implementación de fix
