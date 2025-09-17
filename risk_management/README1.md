# Risk Management Testing - Version 2.2

Framework de testing para el sistema avanzado de gestión de riesgo económico de la estrategia ATAS 468.

## 🎯 Objetivo

Validar el sistema completo de position sizing automático que incluye:
- **Riesgo fijo en USD** por operación con protección underfunded
- **Porcentaje de cuenta** a arriesgar con auto-detección de equity
- **Auto-detección robusta de tick values** via MinStepPrice + fallbacks
- **Sistema de overrides** con parser mejorado (SYM=VAL y SYM,VAL)
- **Diagnósticos en tiempo real** con refresh manual y echo automático
- **Protección inteligente** contra entradas forzadas cuando underfunded

## 📋 Funcionalidades a Probar

### 1. Position Sizing Modes
- **Manual**: Cantidad fija definida por usuario
- **FixedRiskUSD**: Cantidad calculada por riesgo fijo en dólares
- **PercentOfAccount**: Cantidad calculada por % de equity de cuenta

### 2. Enhanced Tick Value Detection (v2.2)
- **Priority 1**: MinStepPrice (ATAS standard $/tick property)
- **Priority 2**: Auto-detección via reflexión de Security/InstrumentInfo
- **Priority 3**: CSV overrides con parser mejorado (acepta = y ,)
- **Priority 4**: Fallback con warnings críticos
- **Preset incluido**: `MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10`

### 3. Advanced Account Equity Detection
- Auto-detección de saldo via Portfolio API con error handling
- Override manual cuando auto-detección falla
- Avisos de mismatch cuando difieren auto vs override
- Logging detallado del proceso con fuente identificada

### 4. Underfunded Protection System (NEW v2.2)
- **Skip if Underfunded**: Aborta trade cuando risk/contract > target risk
- **Min Qty if Underfunded**: Cantidad mínima si fuerza entrada
- **Early Abort**: Validación en OnCalculate antes de SubmitMarket
- **Comprehensive Logging**: Detalle completo de por qué se aborta

### 5. Real-time Diagnostics UI (NEW v2.2)
- **Read-only Properties**: Effective tick value, equity, last auto qty, etc.
- **Refresh Diagnostics Button**: Log instantáneo de valores actuales
- **Auto Echo**: Log automático al inicio de sesión una sola vez
- **Live Updates**: Valores se actualizan en tiempo real

## 🧪 Enhanced Test Scenarios (v2.2)

### G1 - Fixed Risk USD with Auto-detection
- **Config**: PositionSizing = FixedRiskUSD, RiskPerTradeUsd = $50
- **Instrumento**: MNQ (Micro NASDAQ) - tick value auto-detected via MinStepPrice
- **Objetivo**: Validar nueva prioridad MinStepPrice y cálculo automático de qty
- **Expected**: tick value = $0.50, qty calculado basado en SL distance

### G2 - Percent of Account with Equity Detection
- **Config**: PositionSizing = PercentOfAccount, RiskPercentOfAccount = 0.5%
- **Instrumento**: ES (E-mini S&P)
- **Objetivo**: Validar detección automática de equity y avisos de mismatch
- **Expected**: Auto-detected equity vs manual override comparison

### G3 - Enhanced Override System
- **Config**: Tick value overrides con formato `MNQ=0.5;NQ=5`
- **Objetivo**: Validar parser mejorado que acepta = y ,
- **Test Cases**: `SYM=VAL`, `SYM,VAL`, mixed formats, InvariantCulture

### G4 - Underfunded Protection
- **Config**: SkipIfUnderfunded = true, low risk amount vs high SL distance
- **Objetivo**: Validar abort inteligente cuando risk/contract > target
- **Expected**: `ABORT ENTRY: Underfunded` en logs + qty = 0 en diagnostics

### G5 - Real-time Diagnostics Testing
- **Objetivo**: Validar UI diagnostics y refresh functionality
- **Test Cases**:
  - Effective values display correctamente
  - Refresh button loguea `DIAG [manual-refresh]`
  - Auto echo `DIAG [init]` al inicio
  - Valores se actualizan después de cada cálculo

### G6 - Multi-Instrument with Preset (NEW)
- **Config**: Testing con preset completo pre-configurado
- **Instrumentos**: MNQ, NQ, MES, ES, MGC, GC
- **Objetivo**: Validar preset completo y override vs auto detection

## 📊 Métricas a Validar

1. **Precisión de Cálculos**
   - Qty calculada vs esperada
   - Risk por contrato vs configurado
   - Tick value detectado vs real

2. **Robustez del Sistema**
   - Manejo de errores en auto-detección
   - Warnings apropiados cuando usa fallbacks
   - Validación de inputs del usuario

3. **Performance**
   - Tiempo de cálculo
   - Caching de valores detectados
   - Impacto en latencia de ejecución

## 🛠️ Enhanced Analysis Tools (v2.2)

### Log Analysis Commands
```bash
# View all risk diagnostics (NEW)
grep -n "DIAG \[" ATAS_SESSION_LOG.txt

# View tick value detection and mismatches
grep -nE "TICK-VALUE|MinStepPrice|override|auto-detected|MISMATCH" ATAS_SESSION_LOG.txt

# View account equity detection
grep -nE "ACCOUNT EQUITY|auto-detected|override" ATAS_SESSION_LOG.txt

# View auto-qty calculations and underfunded protection
grep -n "AUTOQTY\|ABORT ENTRY\|Underfunded" ATAS_SESSION_LOG.txt

# View parser issues
grep -n "GetTickValueFromOverrides\|failed" ATAS_SESSION_LOG.txt
```

### Real-time Validation
- **UI Diagnostics Panel**: Real-time view of effective values
- **Refresh Diagnostics Button**: Manual echo for immediate validation
- **Live Calculation Tracking**: Updated after each position size calculation
- **Emergency Logs**: Persistent logging across ATAS restarts

## 📁 Estructura

```
risk_management/
├── README.md (este archivo)
├── scenarios/
│   ├── G1_fixed_usd_50/
│   ├── G2_percent_account_0.5/
│   ├── G3_tick_value_override/
│   └── G4_multi_instrument/
├── logs/
│   └── analysis_results/
└── validation/
    ├── calculation_tests.md
    └── edge_cases.md
```

## 🚀 Getting Started (v2.2)

### Quick Setup
1. **Deploy v2.2** - `tools/deploy_all.ps1` (ya incluye todas las mejoras)
2. **Verify Preset** - Confirm tick value overrides: `MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10`
3. **Test Diagnostics** - Click "Refresh diagnostics" button y verificar `DIAG [manual-refresh]` en logs
4. **Configure Mode** - Set position sizing mode (Manual/Fixed USD/% Account)
5. **Execute Test Scenario** - Elegir G1-G6 basado en objetivo

### Validation Checklist
- [ ] **UI Diagnostics Display**: Effective tick value, equity, last qty values showing
- [ ] **Auto Echo**: `DIAG [init]` aparece al inicio de sesión
- [ ] **Override Parser**: Preset MNQ=0.5 etc. funcionando correctamente
- [ ] **MinStepPrice Detection**: Priority on ATAS standard $/tick property
- [ ] **Underfunded Protection**: Abort when risk/contract > target (if enabled)
- [ ] **Live Updates**: Diagnostics update after each calculation

## ⚠️ Enhanced Considerations (v2.2)

### Before Testing
- **Demo/Paper First**: Always test new risk management in safe environment
- **Validate Preset**: Confirm included instruments match your broker's tick values
- **Check Auto-detection**: Verify MinStepPrice is available for your instruments
- **Monitor Logs**: Watch for `CRITICAL` warnings about fallback usage

### During Testing
- **Monitor Diagnostics**: Use UI panel and refresh button frequently
- **Check Underfunded**: Test with low risk amounts to trigger protection
- **Validate Calculations**: Math should match expectations per scenario
- **Log Analysis**: Use provided grep commands for detailed analysis

### Production Deployment
- **Backup Configs**: Save current settings before enabling auto position sizing
- **Gradual Rollout**: Start with small risk amounts and monitor behavior
- **Monitor Mismatches**: Watch for tick value or equity detection discrepancies