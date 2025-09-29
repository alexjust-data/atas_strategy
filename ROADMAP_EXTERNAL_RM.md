# Hoja de Ruta: Integración Risk Management Externo

## Objetivo General
Crear un sistema de Risk Management externo compatible con la estrategia 468 actual, sin romper ninguna funcionalidad existente. El sistema manejará position sizing, breakeven avanzado y trailing, manteniendo la lógica core de señales N→N+1 intacta.

---

## 1) Qué hace hoy la 468 (y qué NO vamos a romper)

### ✅ Funcionalidad Core que se MANTIENE:
- **Entrada**: Captura señal en **N** y **ejecuta en N+1** con confluencias (Genial, EMA8 vs Wilder8, etc.)
- **Ventana ejecución**: "armar/ejecutar/expirar" con validaciones `StrictN1Open`/`OnlyOnePosition`
- **Brackets**: Post-fill, `BuildAndSubmitBracket()` crea **SL/TPs** con splits y comentarios `468TP:/468SL:`
- **Break-even mínimo**: Touch/fill de TP → movimiento de SL a BE (+offset) como "airbag"
- **Reconciliación**: 1×/barra, ajusta cantidades SL/TP para cuadrar con net vivo
- **Estabilidad**: `_liveOrders`, `_orderFills`, `NetByFills()`, watchdog/heartbeat, anti-flat

### 📁 Archivos que NO SE TOCAN:
- `FourSixEightSimpleStrategy.Signals.cs` - Lógica N→N+1
- `FourSixEightSimpleStrategy.Execution.cs` - Submit*, BuildAndSubmitBracket
- `FourSixEightSimpleStrategy.BreakEven.cs` - BE mínimo actual
- `FourSixEightConfluencesStrategy_Simple.cs` - Ciclo principal, hooks, watchdog

---

## 2) Qué añade el Risk Management Externo

### Del documento `risk_management.md`:
1. **Position Sizing**: Manual, Fixed Risk USD, Percent of Account, tick value overrides, underfunded abort
2. **BE Avanzado**: BE virtual/real al alcanzar TP1/TP2, trailing Vela a Vela o TP→TP
3. **Activación automática**: Global/Por Estrategia/Mixto sobre entradas manuales y/o estrategia

### Arquitectura de Separación:
- **Librería común pura** (sin ATAS): Toda la lógica de cálculos
- **Adapter 468**: La 468 sigue siendo dueña de sus órdenes (`468*`) y usa librería cuando se active
- **Estrategia RM Manual**: Gestiona entradas manuales (`RM:*`) con misma librería

---

## 3) Estructura de Archivos

```
src/
├─ MyAtas.Risk/                    # NUEVO (librería común, sin ATAS)
│  ├─ Interfaces/
│  │   ├─ IRiskManager.cs          # contrato alto nivel (plan inicial + eventos)
│  │   ├─ IPositionSizer.cs        # Manual / USD / %Account
│  │   ├─ IBracketPlanner.cs       # SL/TP iniciales, splits, OCO policy
│  │   ├─ IBreakEvenCtrl.cs        # BE (touch/fill) + BE virtual
│  │   ├─ ITrailingCtrl.cs         # trailing simple / TP-to-TP
│  ├─ Engine/
│  │   ├─ RiskEngine.cs            # compone los subsistemas anteriores
│  │   ├─ PositionSizer.cs         # modos del doc (con overrides)
│  │   ├─ BracketPlanner.cs        # SL desde vela señal + offsets; TPs por R
│  │   ├─ BreakEvenController.cs   # BE real/virtual según el doc
│  │   ├─ TrailingController.cs    # "Vela a Vela" y "TP→TP"
│  ├─ Models/
│  │   ├─ EntryContext.cs          # símbolo, dir, entryPx, stopTicks, etc.
│  │   ├─ RiskPlan.cs              # qty, slPx, tpPx[], splits, ocoPolicy
│  │   ├─ ModifyPlan.cs            # acciones: mover SL, recrear OCO, etc.
│  │   ├─ PositionSnapshot.cs      # net, netByFills, hijos activos por owner
│  ├─ Utils/
│      ├─ TickMath.cs, TvOverrides.cs, Diagnostics.cs
│
├─ MyAtas.Strategies/
│  ├─ FourSixEight… (468 actual)   # sin romper lo actual
│  │   ├─ …SimpleStrategy.BreakEven.cs      # BE mínimo (airbag)
│  │   ├─ …SimpleStrategy.Execution.cs      # Submit*, BuildAndSubmitBracket
│  │   ├─ …SimpleStrategy.Signals.cs        # N→N+1 (NO tocar)
│  │   ├─ …ConfluencesStrategy_Simple.cs    # ciclo, hooks, watchdog, UI
│  │   ├─ …SimpleStrategy.Reconcile.cs      # reconcile por qty (mínimo)
│  │
│  ├─ RiskManager.Manual.cs         # NUEVA estrategia: gestiona entradas manuales
```

---

## 4) Interfaz de Usuario

### 4.1 Estrategia 468 (modificaciones mínimas)
- **Se mantiene**: Validaciones/confluencias, Targets (TP1/TP2 por R), BE mínimo
- **Position Sizing**: Ya oculto con `[Browsable(false)]`
- **Toggle futuro**: "Use RiskEngine for sizing" [OFF por defecto]
  - OFF: Comportamiento exacto actual
  - ON: Qty calculado por librería (Manual/USD/% según parámetros)

### 4.2 RM Manual (nueva estrategia)
- **Position Sizing**: Mode Manual/Fixed USD/%Account, Tick Value Overrides, Equity Override, Underfunded rules
- **Breakeven**: BE real/virtual, desde TP1 o TP2
- **Trailing**: Vela a Vela, TP→TP, Distancia/ATR, Confirm Bars
- **Activation**: Global/Por Estrategia/Mixto para entradas manuales
- **Comportamiento**: Click BUY/SELL manual → adjunta SL/TP (prefijo `RM:`) + BE/Trailing

---

## 5) Reglas de Convivencia (cero interferencias)

### Separación por Prefijo:
- **468**: Sólo toca órdenes `468*`
- **RM Manual**: Sólo toca órdenes `RM:*`
- **Detección dueños**: Si hay `468*` vivo → RM Manual no gestiona esa posición

### BE/Trailing Independiente:
- **468**: Mantiene BE mínimo interno (airbag)
- **RM Manual**: Aplica BE avanzado a sus posiciones

### OCO Seguro:
- Cuando una pata se afecte → **reconstruir OCO completo** (TP+SL nuevo OCO)
- Evita desaparición de TP superviviente (lección aprendida)

---

## 6) Fases de Implementación (sin riesgo de roturas)

### ✅ Fase 0 – Baseline (COMPLETADO)
- Tag estable de 468: `v4.0-external-rm-ready`
- Sizing UI oculto con `[Browsable(false)]`
- Risk.cs y QTrail.cs en backup
- Flag `ExternalRiskControlsStops` preparado

### 📋 Fase 1 – Esqueleto
**Objetivo**: Estructura base sin lógica
- Crear proyecto `MyAtas.Risk` con interfaces + modelos vacíos
- Compilación exitosa, 468 sin cambios
- **Criterio**: Build sin errores, 468 funciona igual

### 📋 Fase 2 – RM Manual (UI vacía)
**Objetivo**: Nueva estrategia con UI completa
- `RiskManager.Manual.cs` con pestañas según documento
- UI completa pero sin enviar órdenes
- **Criterio**: UI visible en ATAS, no interfiere con 468

### 📋 Fase 3 – Sizing + Brackets
**Objetivo**: Lógica de position sizing y bracket planning
- `PositionSizer`: Manual/FixedUSD/%Account con overrides
- `BracketPlanner`: SL desde vela señal + offset, TPs por R/splits
- RM Manual adjunta SL/TP (prefijo `RM:`) a entradas manuales
- **Criterio**: Entradas manuales → SL/TP correctos, 468 intacta

### 📋 Fase 4 – BE Real/Virtual + OCO Seguro
**Objetivo**: Breakeven avanzado en librería
- `BreakEvenController`: BE real/virtual según configuración
- OCO reconstruction seguro (evitar TP perdidos)
- Solo activo en RM Manual, 468 mantiene BE mínimo
- **Criterio**: BE avanzado funciona, no rompe OCO, 468 intacta

### 📋 Fase 5 – Trailing (Opcional)
**Objetivo**: Sistema de trailing
- `TrailingController`: Vela a Vela, TP→TP
- Solo para RM Manual
- **Criterio**: Trailing funciona según especificación

### 📋 Fase 6 – Conectar Sizing en 468
**Objetivo**: Toggle opcional para 468 use librería
- "Use RiskEngine for sizing" toggle en UI 468
- OFF: Comportamiento actual exacto
- ON: Qty calculado por PositionSizer (Manual/USD/%)
- **Criterio**: Toggle OFF = comportamiento actual, Toggle ON = sizing correcto

### 📋 Fase 7 – Delegación BE/Trailing 468 (Opcional)
**Objetivo**: 468 puede usar BE/Trailing avanzado
- Respeta `ExternalRiskControlsStops` flag
- Mantiene BE mínimo como airbag desactivable
- **Criterio**: Funciona con/sin delegación, airbag preservado

---

## 7) Criterios de Aceptación

### Pruebas 468:
- ✅ Entrada 5 contratos → TP1 → SL a BE con qty restante
- ✅ TP2 no desaparece tras BE move
- ✅ Reconcile no interfiere con BE
- ✅ Señales N→N+1 funcionan exactamente igual

### Pruebas RM Manual:
- ✅ Entrada manual 5 contratos → adjunta SL/TP (`RM:*`)
- ✅ TP1 touch → BE (real/virtual) según config
- ✅ Trailing funciona según configuración
- ✅ NO toca órdenes `468*` jamás

### Pruebas Convivencia:
- ✅ Ambas estrategias activas: cada una gestiona solo lo suyo
- ✅ Portfolio net=0 transitorio: fallback a `NetByFills()`
- ✅ Latencia/phantom positions: manejo robusto

### Pruebas Performance:
- ✅ Sin degradación de performance en 468
- ✅ RM Manual responde en <50ms a entradas manuales
- ✅ Logging detallado `468/RISK` disponible

---

## 8) Archivos por Fase

### Fase 1 (Esqueleto):
- **AGREGAR**: `src/MyAtas.Risk/` (proyecto completo)
- **NO TOCAR**: Ningún archivo 468 existente

### Fase 2 (RM Manual UI):
- **AGREGAR**: `RiskManager.Manual.cs`
- **NO TOCAR**: Ningún archivo 468 existente

### Fase 3-5 (Lógica RM):
- **MODIFICAR**: Archivos en `MyAtas.Risk/Engine/`
- **NO TOCAR**: Archivos 468 core

### Fase 6 (Toggle 468):
- **MODIFICAR**: Solo UI properties en `FourSixEightConfluencesStrategy_Simple.cs`
- **NO TOCAR**: Lógica core 468

### Fase 7 (Delegación opcional):
- **MODIFICAR**: Solo puntos de integración ya preparados
- **NO TOCAR**: Lógica fundamental 468

---

## 9) Configuración de Logging

### Nuevos canales de log:
- `468/RISK` - Decisiones de position sizing y risk management
- `RM/ENTRY` - Detección y procesamiento entradas manuales
- `RM/BE` - Breakeven y trailing decisions
- `RM/OCO` - OCO reconstruction y bracket management

### Logging existente preservado:
- Todos los canales `468/*` actuales se mantienen
- Sin cambios en verbosidad o formato actual

---

## 10) Próximos Pasos

### Inmediato (Fase 1):
1. Crear estructura `MyAtas.Risk` con interfaces vacías
2. Definir modelos base (`EntryContext`, `RiskPlan`, etc.)
3. Compilar y verificar que 468 no se afecta
4. Commit de esqueleto base

### Seguimiento:
- Implementación iterativa por fases
- Build y pruebas después de cada fase
- Rollback fácil en cualquier momento
- Usuario aprueba cada fase antes de continuar

---

**Estado Actual**: ✅ Fase 0 completada, listo para Fase 1
**Siguiente**: Crear esqueleto `MyAtas.Risk` sin romper funcionalidad existente