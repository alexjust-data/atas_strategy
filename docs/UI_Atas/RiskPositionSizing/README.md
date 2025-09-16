# Risk/Position Sizing - Configuración UI

Documentación completa de todos los parámetros de gestión de riesgo y dimensionamiento de posición disponibles en la interfaz ATAS.

## 📊 Position Sizing Mode

### Manual
- **Descripción**: Usa cantidad fija definida por el usuario
- **Funcionamiento**: La cantidad enviada a mercado es **exactamente** la configurada en *General → Quantity*
- **Campos usados**: Solo `Quantity` del panel General
- **Campos ignorados**: `Risk per trade (USD)` y `Risk % of account`
- **Diagnósticos**: Aun así calcula y guarda diagnósticos (distancia SL, valor tick, riesgo por contrato) para mostrar en UI y logs
- **Código**: Se ejecuta en `CalculatePositionSize` rama Manual, rellenando `_lastAutoQty`, `_lastStopTicks`, `_lastRiskPerContractUsd`

### Fixed Risk USD
- **Descripción**: Calcula cantidad automáticamente por riesgo fijo en dólares
- **Funcionamiento**:
  1. Calcula **distancia al SL** desde vela de señal + `SL offset (ticks)`
  2. Obtiene **valor del tick** (auto-detección o override)
  3. Calcula **riesgo por contrato** = `SL_distance * tick_value`
  4. Resultado: `qty = floor(riskUsd / riskPerContract)` con mínimo 1 contrato
- **Campos usados**: `Risk per trade (USD)`
- **Campos ignorados**: `Quantity` del panel General
- **Logs**: `468/RISK … AUTOQTY: …` y `Final position size: …`

### Percent of Account
- **Descripción**: Calcula cantidad por porcentaje de equity de cuenta
- **Funcionamiento**:
  1. Obtiene **equity de cuenta** (auto-detección o override manual)
  2. Calcula `targetRisk = equity * (Risk % / 100)`
  3. Resultado: `qty = floor(targetRisk / riskPerContract)` con mínimo 1 contrato
- **Campos usados**: `Risk % of account` + `Account equity` (auto o override)
- **Campos ignorados**: `Quantity` del panel General
- **Logs**: Incluye detección de equity y cálculos en `468/RISK`

## 💰 Risk per Trade (USD)
- **Modo aplicable**: Solo Fixed Risk USD
- **Descripción**: Cantidad fija en dólares que estás dispuesto a arriesgar por operación
- **Ejemplo**: Si configuras $50 y el riesgo por contrato es $5, se calculan 10 contratos
- **Valor por defecto**: 50 USD
- **Validación**: Debe ser > 0

## 📈 Risk % of Account
- **Modo aplicable**: Solo Percent of Account
- **Descripción**: Porcentaje del equity de cuenta a arriesgar por operación
- **Ejemplo**: Con equity de $25,000 y 0.5%, el riesgo objetivo es $125
- **Valor por defecto**: 0.5%
- **Validación**: Debe estar entre 0.1% y 10%

## 🎯 Tick Value Overrides (CSV)
- **Descripción**: Valores de tick personalizados por símbolo para cálculos precisos
- **Formato**: `SYM=VAL;SYM=VAL;...` usando **punto** como separador decimal
- **Preset recomendado**:
  ```
  MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10
  ```
- **Prioridad**: Override → Auto-detección → Fallback (5.00 USD/tick)
- **Cache**: Los valores se cachean por 5 minutos para optimizar rendimiento
- **Logs**: Avisos de mismatch si override difiere de auto-detección

## 💼 Manual Account Equity Override
- **Modo aplicable**: Solo Percent of Account cuando auto-detección falla
- **Descripción**: Equity manual de la cuenta para cálculos de porcentaje
- **Cuándo se usa**: Solo si la auto-detección por API/Portfolio falla
- **Validación**: Debe ser > 0
- **Logs**: Se indica si usa auto-detección o override

## ⚠️ Skip Trade if Underfunded
- **Descripción**: Aborta operación si el riesgo por contrato excede el riesgo objetivo
- **Funcionamiento**: Si `riskPerContract > targetRisk` → `qty = 0` → ABORT ENTRY
- **Valor por defecto**: true (recomendado para protección)
- **Logs**: `ABORT ENTRY: Underfunded (risk/ct=$X.XX > target=$Y.YY)`
- **Alternativa**: Si false, usa `Min Qty if Underfunded`

## 🔢 Min Qty if Underfunded
- **Descripción**: Cantidad mínima a usar si se fuerza entrada cuando underfunded
- **Cuándo se usa**: Solo si `Skip Trade if Underfunded = false`
- **Valor por defecto**: 1 contrato
- **Advertencia**: Puede exceder el riesgo máximo deseado
- **Validación**: Debe ser ≥ 1

## 📝 Enable Detailed Risk Logging
- **Descripción**: Activa logging detallado de todos los cálculos de risk management
- **Información logged**:
  - Distancia SL en ticks y precio
  - Valor del tick usado (fuente: override/auto/fallback)
  - Riesgo por contrato calculado
  - Equity/riesgo objetivo (según modo)
  - Cantidad final (AUTOQTY)
- **Logs**: Categoría `468/RISK` con líneas detalladas
- **Recomendación**: Activar durante setup y testing

## 🔄 Interacciones Importantes

### Con SL/TP System
- **SL Distance**: Se calcula desde vela de señal + `SL offset (ticks)`
- **Bracket Orders**: Se crean sobre la cantidad neta real post-fill
- **TP Split**: Los TPs se reparten según `SplitQtyForTPs(totalQty, enabled)`
- **Reconciliation**: Si hay más TPs que contratos, se cancelan TPs sobrantes

### Con General → Quantity
- **Manual Mode**: Usa exactamente el valor configurado
- **Auto Modes**: Ignora el valor, calcula dinámicamente
- **UI Update**: No reescribe el spinner, pero logs muestran cantidad real enviada

### Con Instrumentos
- **Symbol Detection**: Usa símbolo actual del Security para overrides
- **Micros/Minis**: Funciona con MNQ/NQ, MES/ES, MGC/GC automáticamente
- **Tick Size**: Respeta MinStep del instrumento para redondeo de órdenes

## 🎮 Flujo de Trabajo Recomendado

1. **Configurar Overrides**: Añadir instrumentos principales al CSV
2. **Seleccionar Modo**: Manual para testing, Auto para trading sistemático
3. **Activar Logs**: Enable detailed risk logging = true
4. **Verificar Cálculos**: Revisar logs `468/RISK … AUTOQTY:` vs `MARKET ORDER SENT`
5. **Monitorear UI**: Usar diagnostics panel para verificación en tiempo real

## ⚡ Ejemplos Prácticos

### Fixed Risk USD con MNQ
```
Config: PositionSizingMode = FixedRiskUSD, RiskPerTradeUsd = $50
Detección: tick_value = $0.50 (override), SL_distance = 10 ticks
Cálculo: risk_per_contract = 10 * $0.50 = $5
Resultado: qty = floor($50 / $5) = 10 contratos
Log: AUTOQTY mode=FixedRiskUSD riskUsd=50.00 stopTicks=10 tickValue=0.50 -> qty=10
```

### Percent of Account con ES
```
Config: PositionSizingMode = PercentOfAccount, RiskPercentOfAccount = 0.5%
Detección: account_equity = $25,000 (auto), tick_value = $12.50
Cálculo: target_risk = $25,000 * 0.5% = $125, SL_distance = 4 ticks
Risk per contract: 4 * $12.50 = $50
Resultado: qty = floor($125 / $50) = 2 contratos
```

### Underfunded Protection
```
Config: RiskPerTradeUsd = $20, SkipIfUnderfunded = true
Cálculo: SL_distance = 50 ticks, tick_value = $0.50
Risk per contract: 50 * $0.50 = $25 > target $20
Resultado: qty = 0, ABORT ENTRY: Underfunded
```