# 📋 PLAN DETALLADO DE IMPLEMENTACIÓN - RISK MANAGEMENT

Analizando la imagen de configuración objetivo, necesitamos llegar a un sistema completo con **6 secciones principales**. Este plan prioriza la **seguridad del código base** sobre la velocidad de desarrollo.

---

## 🎯 **PASO 1: FOUNDATION - Enums y Properties Básicos**

### ¿Por qué empezar aquí?

- ✅ **Zero riesgo** para el código existente - solo agregamos propiedades
- ✅ **No modificamos lógica** de ejecución actual
- ✅ **UI funcional** inmediatamente para testing
- ✅ **Base sólida** para todos los pasos siguientes

### Qué modificamos del código original:

- **Agregar enums**: `PositionSizingMode`, `BreakevenMode`
- **Agregar properties** con `[Category]` y `[DisplayName]`
- **Mantener valores default** que no cambien comportamiento actual
- **NO tocar** ninguna lógica de `OnCandle()`, `SubmitMarket()`, etc.

### Resultado esperado:

- La estrategia funciona **exactamente igual** que antes
- UI muestra las nuevas opciones pero en modo "dummy"
- Base preparada para siguientes pasos

---

## 🎯 **PASO 2: AUTO-DETECTION - Tick Value y Account Equity**

### ¿Por qué este orden?

- ✅ **Funciones helper independientes** - no afectan flujo principal
- ✅ **Testing aislado** - podemos verificar detección sin cambiar trades
- ✅ **Diagnósticos inmediatos** - vemos en UI si detecta correctamente
- ✅ **Fundación crítica** para cálculos posteriores

### Qué modificamos:

- **Agregar métodos**: `GetEffectiveTickValue()`, `GetEffectiveAccountEquity()`
- **Implementar CSV overrides** para instrumentos conocidos
- **Agregar properties diagnósticas** (solo lectura)
- **NO cambiar** cantidad de contratos aún - seguimos usando `Quantity` original

### Validaciones de seguridad:

- Todos los métodos tienen **fallbacks** robustos
- **Try-catch** en detección de Portfolio
- **Valores default** seguros (0.5 USD/tick, 10000 USD equity)

---

## 🎯 **PASO 3: CALCULATION ENGINE - Lógica de Cálculo**

### ¿Por qué separar el cálculo?

- ✅ **Función pura** - fácil de testear aisladamente
- ✅ **No afecta trades** hasta que la conectemos
- ✅ **Debugging sencillo** - podemos loggear cálculos sin ejecutar
- ✅ **Rollback fácil** si algo falla

### Qué modificamos:

- **Crear `CalculateQuantity()`** con toda la lógica
- **Implementar 3 modos**: Manual (sin cambios), FixedRiskUSD, PercentOfAccount
- **Agregar logging** detallado de todos los cálculos
- **Update properties diagnósticas** en tiempo real
- **IMPORTANTE**: Aún no usamos el resultado - solo calculamos y loggeamos

### Protecciones incluidas:

- **Underfunded protection** desde el inicio
- **Validación de inputs** (risk > 0, equity > 0, etc.)
- **Limits sensatos** (max 1000 contratos, min 1 contrato)

---

## 🎯 **PASO 4: INTEGRATION - Conectar con Sistema de Trading**

### ¿Por qué este es el paso más crítico?

- ⚠️ **Único punto** donde tocamos lógica de ejecución existente
- ⚠️ **Mayor riesgo** de romper funcionalidad
- ✅ **Cambio mínimo** - solo reemplazar `Quantity` por `CalculateQuantity()`
- ✅ **Reversible** inmediatamente con 1 línea

### Modificación quirúrgica:

```csharp
// BEFORE: SubmitMarket(dir, Quantity, bar + 1);
// AFTER:  SubmitMarket(dir, CalculateQuantity(), bar + 1);
```

### Testing exhaustivo requerido:

- ✅ **Manual mode** = comportamiento idéntico al original
- ✅ **FixedRiskUSD** con valores conocidos
- ✅ **PercentOfAccount** con equity simulado
- ✅ **Underfunded scenarios**
- ✅ **Edge cases** (SL = 0, risk = 0, etc.)

---

## 🎯 **PASO 5: BREAKEVEN SYSTEM - TP1 Trigger**

### ¿Por qué separar breakeven?

- ✅ **Sistema independiente** - no afecta entrada de trades
- ✅ **Event-driven** - solo actúa en `OnOrderChanged()`
- ✅ **Fácil disable** - si falla, simplemente no se ejecuta
- ✅ **Testing incremental** - necesitamos trades reales para validar

### Qué modificamos:

- **Enhance `OnOrderChanged()`** para detectar TP1 fills
- **Agregar entry price tracking** con fallbacks robustos
- **Implementar breakeven logic** cuando `BreakevenMode = OnTPFill`
- **Mantener backward compatibility** - si está disabled, no hace nada

### Consideraciones técnicas:

- **Entry price fallback**: `Order.FillPrice` → `GetCandle().Open` → plannedPrice
- **TP detection**: Pattern matching en order labels (`468TP1`)
- **SL adjustment**: Modificar órdenes SL existentes a breakeven

---

## 🎯 **PASO 6: ADVANCED FEATURES - Diagnostics y Refinements**

### ¿Por qué al final?

- ✅ **Nice-to-have** no críticos para funcionalidad
- ✅ **Quality of life** improvements
- ✅ **No risk** - solo mejoran UX y debugging
- ✅ **Iterative improvement** basado en uso real

### Features incluidas:

- **Refresh diagnostics** button
- **Advanced logging** controls
- **Configuration validation** warnings
- **Performance optimizations**
- **Error recovery** mechanisms

---

## 🚨 **PROTOCOLO DE SEGURIDAD EN CADA PASO**

### Pre-implementation:

1. **Git commit** del estado actual
2. **Backup** del archivo de estrategia
3. **Clear test plan** definido

### Post-implementation:

1. **Compile + Deploy** sin errores
2. **UI verification** - parámetros visibles y funcionando
3. **Core functionality** - estrategia base sigue funcionando
4. **New feature testing** - solo la nueva funcionalidad
5. **Edge cases** - inputs inválidos, scenarios extremos
6. **Git commit** con resultados de testing

### Rollback triggers:

- ❌ **Compilation errors**
- ❌ **Core strategy malfunction**
- ❌ **UI corruption**
- ❌ **Unexpected trade behavior**
- ❌ **ANY doubt** about stability

---

## 🎯 **RESULTADO FINAL**

Con este plan, llegamos a **exactamente** lo que muestra la imagen objetivo:

- ✅ **Risk/Position Sizing** completo con 3 modos
- ✅ **Auto-detection** de tick values y equity
- ✅ **Breakeven system** con OnTPFill mode
- ✅ **Risk/Diagnostics** con valores en tiempo real
- ✅ **Underfunded protection** y validaciones
- ✅ **Backward compatibility** total

### Principio fundamental:

> **"La clave es que cada paso es reversible y el código base nunca queda vulnerable."**

---

## 📊 **PROGRESS TRACKING**

- [ ] **Paso 1**: Foundation (Enums + Properties)
- [ ] **Paso 2**: Auto-Detection (Tick Value + Account Equity)
- [ ] **Paso 3**: Calculation Engine (CalculateQuantity Logic)
- [ ] **Paso 4**: Integration (Connect to Trading System)
- [ ] **Paso 5**: Breakeven System (TP1 Trigger Logic)
- [ ] **Paso 6**: Advanced Features (Diagnostics + Refinements)

---

*Última actualización: 2025-09-17 - Rollback Strategy + Incremental Development Plan*