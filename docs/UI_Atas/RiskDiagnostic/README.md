# Risk/Diagnostics - Panel UI de Solo Lectura

Documentación completa de todas las propiedades de diagnóstico en tiempo real disponibles en la interfaz ATAS para monitoreo del sistema de risk management.

## 📊 Propiedades de Solo Lectura

### Effective Tick Value (USD/tick)
- **Descripción**: Valor actual del tick en USD que está usando la estrategia para cálculos
- **Fuente de datos**: Resultado de `GetTickValue()` con sistema de prioridades
- **Prioridades**:
  1. **Override CSV**: Si existe entrada para el símbolo actual
  2. **Auto-detección**: Via reflexión de propiedades Security/InstrumentInfo
  3. **Fallback**: 5.00 USD/tick con warning crítico
- **Actualización**: En tiempo real al cambiar instrumento o modificar overrides
- **Ejemplo**: `0.50` para MNQ, `5.00` para NQ, `12.50` para ES

### Effective Account Equity (USD)
- **Descripción**: Equity de cuenta efectivo usado para cálculos de % of Account
- **Fuente de datos**: `GetAccountEquity()` con prioridad auto → override
- **Lógica**:
  - Si auto-detección > 0: usa valor auto-detectado
  - Si auto-detección falla: usa `Account Equity Override`
- **Actualización**: Se verifica en cada cálculo de position sizing
- **Ejemplo**: `25000.00` USD (auto-detected) o valor override
- **Avisos**: Log de mismatch si auto y override difieren >2%

### Last Auto Qty (contracts)
- **Descripción**: Última cantidad de contratos calculada por el sistema
- **Valor según modo**:
  - **Manual**: Refleja la cantidad manual configurada
  - **Fixed Risk USD**: Cantidad calculada por riesgo fijo
  - **Percent of Account**: Cantidad calculada por % de equity
- **Casos especiales**:
  - `0` si underfunded y Skip = true
  - Mínimo 1 si underfunded y Skip = false
- **Actualización**: Después de cada llamada a `CalculatePositionSize`

### Last Risk/Contract (USD)
- **Descripción**: Riesgo en dólares por contrato del último cálculo
- **Fórmula**: `SL_distance_ticks * tick_value_USD`
- **Ejemplo**: SL a 10 ticks con tick value $0.50 = $5.00 risk/contract
- **Uso**: Base para cálculos de cantidad automática
- **Actualización**: Se recalcula en cada signal processing

### Last Stop Distance (ticks)
- **Descripción**: Distancia del Stop Loss en ticks desde precio de entrada
- **Fuente**: Calculado en `CalculateStopLossDistance`
- **Factores**:
  - Configuración `Use SL from signal candle`
  - `SL offset (ticks)` adicional
  - High/Low de vela de señal vs precio de entrada
- **Ejemplo**: `10` ticks para un SL a 10 ticks del entry
- **Actualización**: En cada validación de señal

### Last Risk Input (USD)
- **Descripción**: Riesgo objetivo configurado que se usó en el último cálculo
- **Valor según modo**:
  - **Manual**: `risk_per_contract * qty_manual` (riesgo efectivo)
  - **Fixed Risk USD**: Valor configurado en `Risk per Trade (USD)`
  - **Percent of Account**: `equity * (Risk % / 100)`
- **Ejemplo**: $50.00 en Fixed Risk, $125.00 en 0.5% of $25k account
- **Actualización**: Con cada cálculo de position sizing

## 🔄 Refresh Diagnostics (Button)

### Funcionamiento
- **Tipo**: Propiedad booleana que actúa como botón
- **Acción**: Al marcar `true` en UI → ejecuta echo inmediato → auto-reset a `false`
- **Propósito**: Generar log instantáneo de todos los valores efectivos
- **Uso recomendado**: Para debugging, verificación de configuración, testing

### Log Generado
```
DIAG [manual-refresh] sym=MNQ tickSize=0.25 tickVal=0.50USD/t equity(auto)=25000.00USD
lastAutoQty=10 stopTicks=10 risk/ct=5.00 riskInput=50.00
```

### Información Incluida
- **sym**: Símbolo del instrumento actual
- **tickSize**: Tick size del instrumento (para órdenes)
- **tickVal**: Tick value efectivo en USD (para cálculos)
- **equity(source)**: Equity con fuente (auto/override)
- **lastAutoQty**: Última cantidad calculada
- **stopTicks**: Distancia SL en ticks
- **risk/ct**: Riesgo por contrato en USD
- **riskInput**: Riesgo objetivo configurado

## 🚀 Auto Echo at Session Start

### Trigger Automático
- **Cuándo**: Primera barra procesada en nueva sesión ATAS
- **Log ID**: `DIAG [init]`
- **Propósito**: Registro automático de configuración efectiva al iniciar
- **Frecuencia**: Una sola vez por sesión (flag `_diagEchoLoggedInit`)

### Beneficios
- **Audit Trail**: Registro de configuración al inicio de cada sesión
- **Troubleshooting**: Verificación inmediata de valores efectivos
- **Consistency Check**: Confirma que overrides y auto-detección funcionan

## 📈 Actualización en Tiempo Real

### Triggers de Actualización
1. **Position Size Calculation**: Cada vez que se calcula cantidad para señal
2. **Instrument Change**: Al cambiar de instrumento en ATAS
3. **Configuration Change**: Al modificar parámetros de risk management
4. **Manual Refresh**: Al usar el botón Refresh Diagnostics

### Latencia
- **UI Properties**: Actualización inmediata (binding directo)
- **Log Echo**: Tiempo de escritura de log (~ms)
- **Cache Refresh**: Respect 5-minute cache para tick values

## 🔍 Interpretación de Valores

### Valores Normales
- **Effective Tick Value**: Coincide con valores conocidos del mercado
- **Effective Account Equity**: Valor razonable de cuenta activa
- **Last Auto Qty**: Entre 1-1000 contratos típicamente
- **Risk/Contract**: Proporcional al tick value y SL distance
- **Stop Distance**: 5-50 ticks típicamente
- **Risk Input**: Coherente con configuración

### Valores Problemáticos
- **Tick Value = 5.00**: Usando fallback (configurar override)
- **Account Equity = 0**: Auto-detección falló (configurar override)
- **Auto Qty = 0**: Underfunded protection activa
- **Risk/Contract muy alto**: SL muy amplio o tick value incorrecto
- **Stop Distance = 0**: Error en cálculo de SL

### Warnings en Logs
- **CRITICAL ... FALLBACK**: Tick value usando fallback, configurar override
- **MISMATCH**: Override vs auto-detección difieren, verificar valores
- **ABORT ENTRY: Underfunded**: Protección underfunded activa
- **Auto-detected equity**: Verificar que valor es correcto

## 🛠️ Troubleshooting Guide

### Tick Value Incorrecto
1. Verificar override CSV para símbolo actual
2. Revisar logs de auto-detección
3. Confirmar formato correcto (`SYM=VAL` con punto decimal)
4. Usar Refresh Diagnostics para verificar cambios

### Account Equity No Detectado
1. Verificar conexión con broker/datos
2. Configurar `Account Equity Override` manualmente
3. Revisar logs de Portfolio API access
4. Confirmar permisos de lectura de cuenta

### Auto Qty Inesperado
1. Verificar modo de position sizing activo
2. Confirmar valores de risk input vs risk/contract
3. Revisar cálculo de SL distance
4. Verificar protección underfunded settings

### Diagnósticos No Actualizan
1. Verificar que estrategia está activa
2. Confirmar que hay señales siendo procesadas
3. Revisar logs de `CalculatePositionSize`
4. Usar Refresh Diagnostics para forzar actualización

## 📋 Checklist de Verificación

### Setup Inicial
- [ ] Effective Tick Value muestra valor correcto para instrumento
- [ ] Effective Account Equity muestra valor razonable (si usa % mode)
- [ ] Auto Echo aparece en logs al iniciar sesión
- [ ] Refresh Diagnostics genera log manual correctamente

### Durante Trading
- [ ] Last Auto Qty actualiza con cada señal
- [ ] Risk/Contract es coherente con SL distance
- [ ] Stop Distance refleja configuración SL actual
- [ ] Risk Input coincide con modo y configuración

### Troubleshooting
- [ ] No warnings CRITICAL de fallback en logs
- [ ] No mensajes ABORT ENTRY inesperados
- [ ] Valores se actualizan en tiempo real
- [ ] Logs muestran cálculos detallados (si enabled)

## 💡 Consejos de Uso

### Para Desarrollo/Testing
- Activar `Enable detailed risk logging`
- Usar Refresh Diagnostics frecuentemente
- Monitorear logs de `468/RISK` en tiempo real
- Verificar coherencia entre UI y logs

### Para Trading en Vivo
- Verificar diagnósticos al inicio de sesión
- Monitorear Auto Qty vs expectativas
- Revisar Risk/Contract vs configuración
- Confirmar Effective values correctos

### Para Auditoría
- Usar Auto Echo para trail de configuración
- Exportar logs con grep de `DIAG [...]`
- Verificar coherencia temporal de valores
- Documentar configuraciones efectivas por sesión