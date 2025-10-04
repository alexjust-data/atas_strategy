# 🔍 ANÁLISIS FORENSE: Secuencia Trades 18:00 - BUG Crítico en SplitQtyForTPs

**Timestamp logs**: 07:33:13 - 07:33:32 (19 segundos)
**Barras involucradas**: 5286 (N) → 5287 (N+1 BUY + Signal SELL) → 5288 (N+1 SELL)
**Trades analizados**:
- Trade #1: BUY 3 @ 19830.75 (SL hit @ 19815.50) → P&L: -$91.50
- Trade #2: SELL 2 @ 19815.00 (SL hit @ 19832.50) → P&L: -$70.00
**Session P&L final**: -$161.50

---

## 📋 RESUMEN EJECUTIVO

### **🔥 BUG CRÍTICO IDENTIFICADO**
**Archivo**: `FourSixEightSimpleStrategy.Execution.cs:175`
**Función**: `SplitQtyForTPs(int totalQty, int nTps)`
**Problema**: Crea más SLs que contratos en posición cuando `totalQty < nTps`

### **Evidencia en logs:**
```
[07:33:32.362] ExecuteEntry: qty=2         ← Position sizing calculó 2 contratos
[07:33:32.379] BRACKETS: Split=[1,1,1]     ← Creó 3 SLs (debería ser 2)
                         Total=2            ← Inconsistencia: sum([1,1,1])=3 != 2
```

### **Consecuencia:**
- Entry: SELL 2 contratos
- Brackets: 3 pares TP/SL creados (cada SL = 1 contrato)
- **Riesgo**: Si los 3 SLs ejecutan → Inversión no intencional (SHORT → LONG)
- **Salvado por**: OCO + cancel timing (solo 2 SLs ejecutaron)

---

## 📋 TRADE #1: BUY 3 @ 19830.75 (CORRECTO)

### **FASE 1A: SEÑAL CAPTURADA (N=5286, 17:59:47)**
```
GENIAL CROSS UP at bar=5286
Close=19830.50 > GenialLine=19824.9158
→ SEÑAL ALCISTA ✅
```

### **FASE 1B: EJECUCIÓN (N+1=5287)**
```
[07:33:13.329] Position Sizing:
              entry=19830.75, stop=19815.50, stopTicks=61
              equity=10000.00 → qty=3
              Risk: $100 / $30.50 = 3.27 → 3 ✅

[07:33:13.340] MARKET ORDER SENT: BUY 3 @ 19830.75
[07:33:13.353] FILLED ✅
```

### **FASE 1C: BRACKETS (qty=3 → 3 TPs correcto)**
```
[07:33:13.363] SplitQtyForTPs(3, 3) → [1,1,1] sum=3 ✅
              3 pares TP/SL creados correctamente
```

### **FASE 1D: SL FILLS (3 ejecutados)**
```
[07:33:32.299-330] 3 SLs filled @ 19815.50
                   P&L: -$91.50 ✅
[07:33:32.345] SessionPnL=-91.50
```

---

## 📋 TRADE #2: SELL 2 @ 19815.00 (🚨 BUG AQUÍ)

### **FASE 2A: SEÑAL (N=5287 - Flip inmediato)**
```
[07:33:32.342] GENIAL CROSS DOWN at bar=5287
              (Misma barra que fue N+1 del BUY anterior)
              → SEÑAL BAJISTA ✅
```

### **FASE 2B: POSITION SIZING (qty=2 calculado)**
```
[07:33:32.362] CalculatePositionSize:
              entry=19815.00, stop=19832.50, stopTicks=70
              equity=10000.00 → qty=2 ✅
              Risk: $100 / $35.00 = 2.857 → 2

[07:33:32.362] ExecuteEntry: qty=2 ✅
[07:33:32.364] MARKET ORDER SENT: SELL 2
[07:33:32.365] FILLED: SELL 2 @ 19815.00 ✅
[07:33:32.367] netByFills=-2 ✅
```

### **FASE 2C: 🚨 BUG CRÍTICO - SplitQtyForTPs crea 3 brackets cuando qty=2**

#### **Evidencia del Bug:**
```
[07:33:32.362] ExecuteEntry: qty=2              ← Position sizing calculó 2 contratos
[07:33:32.364] MARKET ORDER SENT: SELL 2        ← Entry order con qty=2
[07:33:32.365] FILLED: SELL 2 @ 19815.00        ← Fill confirmado: 2 contratos
[07:33:32.367] netByFills=-2                    ← Net position: -2 (SHORT 2)

[07:33:32.379] BRACKETS: Split=[1,1,1] Total=2  ← 🚨 BUG: Split suma 3, Total=2
```

**PROBLEMA IDENTIFICADO:**
- Entry qty = 2 contratos
- SplitQtyForTPs(2, 3) retornó [1,1,1] (suma = 3)
- Se crearon 3 pares TP/SL cuando solo hay 2 contratos
- **Riesgo**: 3 SLs ejecutándose en posición de 2 contratos → inversión SHORT → LONG

#### **Root Cause en Código:**
```csharp
// Archivo: FourSixEightSimpleStrategy.Execution.cs:175
private List<int> SplitQtyForTPs(int totalQty, int nTps)
{
    var q = new List<int>();
    if (nTps <= 0) { q.Add(totalQty); return q; }

    int baseQ = Math.Max(1, totalQty / nTps);  // ← 🚨 BUG AQUÍ
    int rem = totalQty - baseQ * nTps;
    for (int i = 0; i < nTps; i++)
        q.Add(baseQ + (i < rem ? 1 : 0));

    return q;
}

// Llamada: SplitQtyForTPs(2, 3)
// baseQ = Math.Max(1, 2/3) = Math.Max(1, 0) = 1
// rem = 2 - (1*3) = -1
// Loop crea: [1,1,1] cuando totalQty=2 pero nTps=3
// Resultado: sum([1,1,1]) = 3 ≠ 2 ❌
```

#### **Fix Propuesto:**
```csharp
private List<int> SplitQtyForTPs(int totalQty, int nTps)
{
    var q = new List<int>();
    if (nTps <= 0) { q.Add(totalQty); return q; }

    // ✅ LIMITAR actualTPs a qty disponible
    int actualTPs = Math.Min(totalQty, nTps);
    int baseQ = totalQty / actualTPs;
    int rem = totalQty - baseQ * actualTPs;

    for (int i = 0; i < actualTPs; i++)
        q.Add(baseQ + (i < rem ? 1 : 0));

    return q;
}

// Llamada: SplitQtyForTPs(2, 3)
// actualTPs = Math.Min(2, 3) = 2
// baseQ = 2 / 2 = 1
// rem = 2 - (1*2) = 0
// Loop crea: [1,1] ✅
// Resultado: sum([1,1]) = 2 ✅
```

#### **Verificación del Fix (Test Cases):**

| totalQty | nTps | OLD Result | OLD Sum | NEW Result | NEW Sum | Status |
|----------|------|------------|---------|------------|---------|--------|
| 3        | 3    | [1,1,1]    | 3 ✅    | [1,1,1]    | 3 ✅    | IGUAL  |
| 6        | 3    | [2,2,2]    | 6 ✅    | [2,2,2]    | 6 ✅    | IGUAL  |
| 7        | 3    | [3,2,2]    | 7 ✅    | [3,2,2]    | 7 ✅    | IGUAL  |
| **2**    | **3**| **[1,1,1]**| **3 ❌**| **[1,1]**  | **2 ✅**| **FIXED**|
| 1        | 3    | [1,1,1]    | 3 ❌    | [1]        | 1 ✅    | FIXED  |
| 4        | 5    | [1,1,1,1,1]| 5 ❌    | [1,1,1,1]  | 4 ✅    | FIXED  |

**Conclusión**: El fix solo corrige casos buggy (qty < nTps), preserva comportamiento correcto (qty >= nTps).

---

### **FASE 2D: TRACKING POST-ENTRY**
```
[07:33:32.367] 468/ORD: OnOrderChanged: 468ENTRY:053332 status=Filled
                       state=Done active=False liveCount=1

[07:33:32.367] 468/POS: Tracked fill: 468ENTRY:053332 = -2 (type: 468ENTRY)
                       ✅ netByFills=-2 registrado correctamente

[07:33:32.369] 468/BRK: ENTRY PRICE CAPTURED: 19815,00
                       ✅ Precio de entrada capturado

[07:33:32.370] 468/PNL: Position entry: Price=19815,00 Qty=2 Dir=-1
                       → Tracking started (using _entryPrice=19815,00)
                       ✅ Session P&L tracking iniciado

[07:33:32.370] 468/ORD: POST-ENTRY FLAT BLOCK armed for 1200ms
                       ✅ Protección anti-flat activada
```

### **FASE 2E: CÁLCULO DE PRECIOS DE BRACKETS**
```
[07:33:32.371] 468/POS: GetNetPosition via Cache+Fills (live=True): -2
                       ✅ Net position confirmado vía fills

[07:33:32.372] 468/STR: BRACKET-PRICES: entry~19815,50 sl=19832,50 risk=17,00

                       CÁLCULO:
                       Entry = 19815.00 (SHORT)
                       SL    = 19832.50 (+17 points = +68 ticks @ 0.25)
                       Risk  = 17.00 points
```

### **FASE 2F: 🚨 CONSTRUCCIÓN DE BRACKETS BUGGY (3 TPs + 3 SLs para qty=2)**

#### **TP/SL Pareja 1 (OCO: dcab0fc1...)**
```
[07:33:32.372] RegisterChildSign: 468SL:053332:dcab0f = 1 (dir=-1 isEntry=False)
[07:33:32.373] STOP submitted: Buy 1 @19832,50 OCO=dcab0fc11dfe46e9832de7d9388cca24

[07:33:32.373] RegisterChildSign: 468TP:053332:dcab0f = 1 (dir=-1 isEntry=False)
[07:33:32.374] LIMIT submitted: Buy 1 @19798,50 OCO=dcab0fc11dfe46e9832de7d9388cca24
              TP1 = Entry - 17 points (1R) = 19798.50
```

#### **TP/SL Pareja 2 (OCO: efdf0d3a...)**
```
[07:33:32.374] RegisterChildSign: 468SL:053332:efdf0d = 1 (dir=-1 isEntry=False)
[07:33:32.375] STOP submitted: Buy 1 @19832,50 OCO=efdf0d3a7c4a4152b2079f3efbbf8758

[07:33:32.376] RegisterChildSign: 468TP:053332:efdf0d = 1 (dir=-1 isEntry=False)
[07:33:32.376] LIMIT submitted: Buy 1 @19781,50 OCO=efdf0d3a7c4a4152b2079f3efbbf8758
              TP2 = Entry - 34 points (2R) = 19781.50
```

#### **TP/SL Pareja 3 (OCO: 41d34230...)**
```
[07:33:32.377] RegisterChildSign: 468SL:053332:41d342 = 1 (dir=-1 isEntry=False)
[07:33:32.377] STOP submitted: Buy 1 @19832,50 OCO=41d3423021554f878f6ae49b7b7f727c

[07:33:32.378] RegisterChildSign: 468TP:053332:41d342 = 1 (dir=-1 isEntry=False)
[07:33:32.378] LIMIT submitted: Buy 1 @19764,50 OCO=41d3423021554f878f6ae49b7b7f727c
              TP3 = Entry - 51 points (3R) = 19764.50
```

### **FASE 6: CONFIRMACIÓN DE BRACKETS**
```
[07:33:32.379] 468/ORD: BRACKETS ATTACHED: tp=3 sl=1 total=2
                       (SL=19832,50 | TPs=19798,50,19781,50,19764,50)

[07:33:32.379] 468/STR: BRACKETS: SL=19832,50 | TPs=19798,50,19781,50,19764,50
                       | Split=[1,1,1] | Total=2

[07:33:32.380] 468/STR: STATE: tradeActive=True dir=-1 net=-2 netByFills=-2
                       tpActive=0 slActive=0 liveOrders=7
                       signalBar=5287 antiFlatMs=600 confirmFlatReads=3

[07:33:32.380] 468/STR: BRACKETS ATTACHED (from net=2)
                       ✅ Sistema en estado LIVE con posición SHORT 2 contratos
```

### **FASE 7: CONFIRMACIÓN DE ÓRDENES (OnNewOrder events)**
```
Todas las órdenes bracket pasan por ciclo completo:
OnNewOrder → OnOrderChanged (Placed) → state=Active

[07:33:32.384-404] 6 órdenes confirmadas:
                   - 468SL:053332:dcab0f (id=14) ACTIVE ✅
                   - 468TP:053332:dcab0f (id=15) ACTIVE ✅
                   - 468SL:053332:efdf0d (id=16) ACTIVE ✅
                   - 468TP:053332:efdf0d (id=17) ACTIVE ✅
                   - 468SL:053332:41d342 (id=18) ACTIVE ✅
                   - 468TP:053332:41d342 (id=19) ACTIVE ✅

[07:33:32.404] 468/POS: GetNetPosition via Cache+Fills (live=True): -2
                       ✅ Net confirmado múltiples veces durante attach
```

### **FASE 8: 🚨 PROBLEMA DETECTADO - TradingManager reporta net=0**
```
[07:33:32.407] RM/SNAP: TM.Position net=0 avg=19815,00
                       ⚠️ BROKER/ATAS reporta net=0 (INCORRECTO)

[07:33:32.407] RM/SNAP: TM.Position net=0 avg=19815,00
                       ⚠️ Lectura duplicada confirma net=0

[07:33:32.407] RM/GATE: ResetAttachState: flat idle
                       ⚠️ RiskManager cree que estamos flat y resetea su estado
```

**ANÁLISIS:**
- La estrategia 468 tiene `netByFills=-2` (CORRECTO)
- El TradingManager.Position reporta `net=0` (LATENCIA/BUG)
- Avg price 19815.00 está presente (prueba de que hubo posición)
- El RiskManager confía en TM.Position y resetea estado prematuramente

### **FASE 9: SPAM DE DETECCIÓN DE POSICIÓN**
```
[07:33:32.408-422] 468/POS: GetNetPosition via Cache+Fills (live=True): -2

                   ✅ 21 lecturas consecutivas confirmando net=-2
                   ✅ Sistema 468 usando fallback (cache+fills) correctamente
                   ✅ Posición REAL existe, broker aún no la reporta
```

### **FASE 2G: FILLS DE STOP LOSS (Consecuencia del Bug)**

#### **Primera SL Fill (41d342)**
```
[07:33:32.477] RM/EVT: OnOrderChanged: id=18 comment='468SL:053332:41d342'
                      status=Filled side=Buy qty=1

[07:33:32.478] 468/POS: Tracked fill: 468SL:053332:41d342 = 1 (type: 468SL:05)
                       netByFills = -2 + 1 = -1 ✅

[07:33:32.479] 468/PNL: TP/SL FILL detected: 468SL:053332:41d342
                       exitPrice=19832.50 filledQty=1
                       P&L = -$17.50 (SHORT perdió)
```

#### **Cancelación Automática de TPs (Failsafe)**
```
[07:33:32.481] 468/ORD: SL filled -> TP failsafe CANCEL ALL: 3
                       ✅ Sistema intenta cancelar 3 TPs

[07:33:32.482-489] Cancelación de órdenes:
                   - 468TP:053332:dcab0f (Canceled) ✅
                   - 468TP:053332:41d342 (Canceled) ✅
                   - 468SL:053332:dcab0f (Canceled) 🚨 Pero quedaba 1 SL más
                   - 468TP:053332:efdf0d (Canceled) ✅
```

#### **Segunda SL Fill (efdf0d)**
```
[07:33:32.491] RM/EVT: OnOrderChanged: id=16 comment='468SL:053332:efdf0d'
                      status=Filled side=Buy qty=1

[07:33:32.494] 468/POS: Tracked fill: 468SL:053332:efdf0d = 1 (type: 468SL:05)
                       netByFills = -1 + 1 = 0 ✅ (Position flat correctamente)

[07:33:32.495] 468/PNL: TP/SL FILL detected: 468SL:053332:efdf0d
                       exitPrice=19832.50 filledQty=1
                       P&L adicional = -$17.50

                       🚨 Total P&L = 2 SLs * -$17.50 = -$35.00
```

#### **🚨 PROBLEMA: Sistema creó 3 SLs pero solo llenó 2**
```
ANÁLISIS:
- Entry: SELL 2 contratos
- SLs creados: 3 (debido al bug SplitQtyForTPs)
- SLs filled: 2 (correcto, matchea el qty de la posición)
- SL restante: 1 (quedó activo pero se canceló a tiempo)

RIESGO EVITADO:
- Si el 3er SL hubiera ejecutado → netByFills = 0 + 1 = +1 (LONG 1)
- Inversión no intencional de posición (SHORT → LONG)
- OCO + timing de cancelación evitó este riesgo
```

---

## 🔍 BUG IDENTIFICADO: SplitQtyForTPs

### **🚨 BUG CRÍTICO: Math.Max(1, ...) fuerza mínimo 1 contrato por bracket**

#### **Código Buggy:**
```csharp
// FourSixEightSimpleStrategy.Execution.cs:175
private List<int> SplitQtyForTPs(int totalQty, int nTps)
{
    var q = new List<int>();
    if (nTps <= 0) { q.Add(totalQty); return q; }

    int baseQ = Math.Max(1, totalQty / nTps);  // ← PROBLEMA AQUÍ
    int rem = totalQty - baseQ * nTps;
    for (int i = 0; i < nTps; i++)
        q.Add(baseQ + (i < rem ? 1 : 0));

    return q;
}
```

#### **Por Qué Falla:**
```
Cuando totalQty < nTps:
- totalQty / nTps = 0 (división entera)
- Math.Max(1, 0) = 1 (fuerza mínimo 1)
- Loop crea nTps brackets de 1 contrato cada uno
- Resultado: nTps contratos cuando solo hay totalQty disponibles

Ejemplo del bug (Trade #2):
- totalQty = 2, nTps = 3
- baseQ = Math.Max(1, 2/3) = Math.Max(1, 0) = 1
- rem = 2 - (1*3) = -1
- Loop: for (i=0; i<3; i++) → crea [1, 1, 1]
- Suma = 3 ≠ 2 ❌
```

#### **Consecuencia en Trade #2:**
```
Entry: SELL 2 contratos @ 19815.00
Brackets creados:
- SL#1: Buy 1 @ 19832.50 (OCO dcab0fc1)
- SL#2: Buy 1 @ 19832.50 (OCO efdf0d3a)
- SL#3: Buy 1 @ 19832.50 (OCO 41d34230) ← 🚨 EXTRA

Fills ejecutados:
- SL#3 filled → net = -2 + 1 = -1 ✅
- SL#2 filled → net = -1 + 1 = 0 ✅
- SL#1 canceled (a tiempo) → RIESGO EVITADO

RIESGO: Si SL#1 hubiera ejecutado:
- net = 0 + 1 = +1 (inversión SHORT → LONG no intencional)
```

### **Problemas Secundarios Observados (No Críticos)**

#### **1. LATENCIA DEL BROKER/ATAS (net=0 fantasma)**
```
SÍNTOMA: TradingManager.Position reporta net=0 inmediatamente después del fill
CAUSA:   Latencia en actualización del objeto Position en ATAS
IMPACTO: RiskManager resetea estado prematuramente
MITIGACIÓN: Estrategia 468 usa fallback cache+fills ✅ (FUNCIONA)
```

#### **2. CAMBIO DE ESTADO: Canceled → Filled**
```
SÍNTOMA: Orden cancelada puede reportar Filled después
CAUSA:   Race condition: Cancel request vs broker execution
IMPACTO: Menor - sistema usa fallback con _entryDir
ESTADO: Working as designed (race conditions inevitables)
```

---

## 📊 RESUMEN DE EJECUCIÓN

### **Órdenes Creadas:**
| Comment           | Type  | Qty | Price     | OCO         | Estado Final |
|-------------------|-------|-----|-----------|-------------|--------------|
| 468ENTRY:053332   | Market| 2   | 19815.00  | -           | Filled ✅    |
| 468SL:053332:dcab0f| Stop | 1   | 19832.50  | dcab0fc1... | Filled ⚠️    |
| 468TP:053332:dcab0f| Limit| 1   | 19798.50  | dcab0fc1... | Canceled ✅  |
| 468SL:053332:efdf0d| Stop | 1   | 19832.50  | efdf0d3a... | Filled ✅    |
| 468TP:053332:efdf0d| Limit| 1   | 19781.50  | efdf0d3a... | Canceled ✅  |
| 468SL:053332:41d342| Stop | 1   | 19832.50  | 41d34230... | Filled ✅    |
| 468TP:053332:41d342| Limit| 1   | 19764.50  | 41d34230... | Canceled ✅  |

### **P&L del Trade #2:**
```
Entry:  SELL 2 @ 19815.00
Exit:   BUY 2 @ 19832.50 (2 SLs filled, 1 canceled)

Loss:   19832.50 - 19815.00 = +17.50 points (SHORT pierde cuando precio sube)
Ticks:  17.50 / 0.25 = 70 ticks
USD:    70 ticks * $0.25/tick * 2 contracts = -$35.00 ✅

🚨 Bug Impact:
- Sistema creó 3 SLs cuando solo debía crear 2
- Solo 2 SLs ejecutaron (correcto por qty de posición)
- 1 SL fue cancelado (evitando inversión SHORT → LONG)
```

### **Estado de Session P&L:**
```
Antes del Trade #2:  -$91.50 (Trade #1 BUY 3 perdió)
Trade #2 Loss:       -$35.00 (SELL 2 perdió)
Después:             -$126.50 (esperado)
```

---

## ✅ LO QUE FUNCIONÓ CORRECTAMENTE

1. ✅ **Detección de señal**: GenialLine cross DOWN detectado correctamente
2. ✅ **Ejecución N+1**: Market order enviado en bar correcto
3. ✅ **Fill inmediato**: Market order filled instantáneamente en replay
4. ✅ **Entry price tracking**: 19815.00 capturado correctamente
5. ✅ **Session P&L tracking**: TrackPositionEntry llamado con valores correctos
6. ✅ **Bracket construction**: 3 pares TP/SL creados con precios correctos
7. ✅ **OCO groups**: Cada pareja tiene OCO único
8. ✅ **RegisterChildSign**: Todos los signos registrados antes de submit
9. ✅ **Failsafe TP cancel**: Al llenar SL, cancela TPs automáticamente
10. ✅ **Fallback position detection**: netByFills funciona cuando broker reporta net=0

---

## 🚨 PROBLEMA CRÍTICO DETECTADO

### **P1: 🔥 SplitQtyForTPs crea más brackets que contratos disponibles**
**Severidad**: **CRÍTICA**
**Archivo**: `FourSixEightSimpleStrategy.Execution.cs:175`
**Impacto**: Inversión no intencional de posición cuando qty < nTps

#### **Evidencia:**
```
Logs línea 54020:
[07:33:32.362] ExecuteEntry: qty=2              ← Position sizing calculó 2
[07:33:32.379] BRACKETS: Split=[1,1,1] Total=2  ← Bug: sum([1,1,1])=3 ≠ 2
```

#### **Root Cause:**
```csharp
int baseQ = Math.Max(1, totalQty / nTps);  // ← BUG
// Cuando totalQty=2 y nTps=3:
// baseQ = Math.Max(1, 0) = 1 (fuerza mínimo)
// Loop crea 3 brackets cuando solo hay 2 contratos
```

#### **Fix:**
```csharp
private List<int> SplitQtyForTPs(int totalQty, int nTps)
{
    var q = new List<int>();
    if (nTps <= 0) { q.Add(totalQty); return q; }

    // LIMITAR actualTPs a qty disponible
    int actualTPs = Math.Min(totalQty, nTps);
    int baseQ = totalQty / actualTPs;
    int rem = totalQty - baseQ * actualTPs;

    for (int i = 0; i < actualTPs; i++)
        q.Add(baseQ + (i < rem ? 1 : 0));

    return q;
}
```

#### **Logging Adicional Recomendado:**
```csharp
var split = SplitQtyForTPs(totalQty, nTps);
int sumSplit = split.Sum();

if (sumSplit != totalQty)
{
    LogE($"468/BRK: ⚠️ VALIDATION FAIL: Split sum={sumSplit} != totalQty={totalQty}");
    LogE($"468/BRK: Split={string.Join(",", split)} | Adjusting to match totalQty");
    // Fallback: usar totalQty en un solo bracket
    split = new List<int> { totalQty };
}
else
{
    LogI($"468/BRK: ✅ Split validation OK: sum={sumSplit} == totalQty={totalQty}");
}
```

---

### **Problemas Secundarios (No Críticos)**

#### **P2: RiskManager resetea estado por net=0 fantasma**
**Severidad**: MEDIA
**Impacto**: RiskManager pierde tracking de entrada manual por latencia ATAS
**Solución**: Usar misma lógica que 468 (cache+fills con anti-flat protection)
**Estado**: Working as designed con fallback

#### **P3: Orden cancelada puede reportar Filled después**
**Severidad**: BAJA
**Impacto**: Race condition entre Cancel request y broker execution
**Solución**: Mantener childSign registry con grace period post-cancel
**Estado**: Working as designed (fallback con _entryDir funciona)

---

## 🔧 RECOMENDACIONES

### **🔥 INMEDIATO (Hot Fix - CRÍTICO):**

1. **Fix SplitQtyForTPs**:
   ```csharp
   // En FourSixEightSimpleStrategy.Execution.cs:175
   int actualTPs = Math.Min(totalQty, nTps);  // ← ADD THIS LINE
   int baseQ = totalQty / actualTPs;           // ← REMOVE Math.Max(1, ...)
   int rem = totalQty - baseQ * actualTPs;
   for (int i = 0; i < actualTPs; i++)        // ← USE actualTPs, not nTps
       q.Add(baseQ + (i < rem ? 1 : 0));
   ```

2. **Validación Post-Split**:
   ```csharp
   // Después de llamar SplitQtyForTPs, validar:
   if (split.Sum() != totalQty)
   {
       LogE($"468/BRK: CRITICAL - Split sum mismatch!");
       split = new List<int> { totalQty };  // Fallback seguro
   }
   ```

3. **Testing del Fix**:
   - Test con qty=1, nTps=3 → esperado [1]
   - Test con qty=2, nTps=3 → esperado [1,1]
   - Test con qty=3, nTps=3 → esperado [1,1,1]
   - Test con qty=7, nTps=3 → esperado [3,2,2]

### **Corto plazo:**

1. **Logging mejorado en bracket construction**:
   - Log split array ANTES de crear brackets
   - Log cada SL con su qty individual
   - Log validación sum(split) == totalQty

2. **Test regression**:
   - Crear test cases para qty < nTps (1,2,4 vs 3 TPs)
   - Verificar que casos normales (qty >= nTps) no se rompan
   - Replay de escenarios históricos para validar

### **Medio plazo:**

1. **Refactor bracket management**:
   - Separar lógica de split de lógica de submission
   - Validación en múltiples capas (split → submit → confirm)
   - Reconciliación post-attach (sum(active SLs) == net position)

---

## 📝 LOGS CLAVE PARA DEBUGGING

### **Confirmar P&L final:**
```bash
grep "SessionPnL\|Session P&L" ATAS_SESSION_LOG.txt | tail -n 20
```

### **Verificar over-fill:**
```bash
grep "468SL:053332\|netByFills" ATAS_SESSION_LOG.txt | grep -A 2 "Tracked fill"
```

### **Tracking de net position:**
```bash
grep "TM.Position net=\|GetNetPosition via" ATAS_SESSION_LOG.txt | tail -n 50
```

---

## 🎯 CONCLUSIÓN

### **Bug Identificado:**
**Archivo**: `FourSixEightSimpleStrategy.Execution.cs:175`
**Función**: `SplitQtyForTPs(int totalQty, int nTps)`
**Problema**: `Math.Max(1, totalQty / nTps)` crea más brackets que contratos cuando `totalQty < nTps`

### **Evidencia en Logs:**
- Línea 54020: `ExecuteEntry: qty=2`
- Línea 54038: `BRACKETS: Split=[1,1,1] Total=2` ← sum([1,1,1]) = 3 ≠ 2

### **Impacto:**
- Severidad: **CRÍTICA**
- Riesgo: Inversión no intencional de posición (SHORT → LONG)
- Mitigación actual: OCO + cancel timing evitó ejecución del 3er SL

### **Fix Propuesto:**
```csharp
int actualTPs = Math.Min(totalQty, nTps);  // Limitar a qty disponible
int baseQ = totalQty / actualTPs;           // No forzar mínimo de 1
int rem = totalQty - baseQ * actualTPs;
for (int i = 0; i < actualTPs; i++)        // Loop solo actualTPs veces
    q.Add(baseQ + (i < rem ? 1 : 0));
```

### **Próximos Pasos:**
1. ✅ Implementar fix en `SplitQtyForTPs`
2. ✅ Añadir validación `sum(split) == totalQty`
3. ✅ Testing con casos qty < nTps (1, 2, 4 vs 3 TPs)
4. ✅ Regression test para casos normales (qty >= nTps)
5. ✅ Deploy y replay para validación

---

**Análisis completado**: 2025-10-03 18:00
**Archivo**: FORENSIC_18_00.md
**Bug Root Cause**: Math.Max(1, ...) en SplitQtyForTPs
**Estado**: IDENTIFICADO - Pendiente implementación de fix
