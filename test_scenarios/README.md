# Test Scenarios - ATAS 468 Strategy

Framework completo de testing para la estrategia ATAS 468, organizado por módulos independientes.

## 📁 Módulos de Testing

### 🧪 [confluence_testing/](confluence_testing/)
Testing exhaustivo del sistema de confluencias y validaciones.

**Escenarios incluidos:**
- **A - Baseline**: Ambas confluencias activas + guard validation
- **B - CONF#1 Only**: GenialLine slope isolation testing
- **C - CONF#2 Window**: EMA vs Wilder8 window mode testing
- **D - CONF#2 Strict**: EMA vs Wilder8 strict mode testing
- **E - N+1 Timing**: Strict timing and expiration testing
- **F - Guard Test**: OnlyOnePosition guard behavior validation

**Status:** ✅ Completado y validado - Todas las confluencias funcionan perfectamente

### 💰 [risk_management/](risk_management/)
Testing del sistema avanzado de gestión de riesgo económico y position sizing (v2.2).

**Funcionalidades implementadas:**
- **3 modos de position sizing**: Manual, Fixed Risk USD, % of Account
- **Auto-detección robusta**: MinStepPrice priority + reflection fallbacks
- **Sistema de overrides mejorado**: Parser acepta SYM=VAL y SYM,VAL con InvariantCulture
- **Protección underfunded**: Abort inteligente cuando risk/contract > target
- **Diagnósticos en tiempo real**: UI properties + refresh button + auto echo
- **Preset completo**: MNQ, NQ, MES, ES, MGC, GC pre-configurados

**Status:** ✅ Implementado - Testing framework actualizado con 6 scenarios (G1-G6)

## 🛠️ Herramientas Comunes

- `../tools/analizar_escenario.bat` - Análisis de resultados
- `../tools/setup_escenario.bat` - Configuración de escenarios

## 📊 Metodología de Testing

Cada módulo incluye:
1. **Logs de sesión** - Datos completos de ejecución
2. **Análisis forense** - Validación Claude + GPT5
3. **Screenshots** - Configuración UI y charts
4. **Documentación** - READMEs específicos por módulo

## 🎯 Objetivos

- **Confluence Testing**: Validar precisión del sistema de señales ✅
- **Risk Management**: Validar cálculos avanzados de position sizing y gestión de riesgo ✅
- **Real-time Diagnostics**: Validar sistema de diagnósticos en tiempo real
- **Underfunded Protection**: Validar protección inteligente contra entradas forzadas
- **Integration Testing**: Asegurar que todos los módulos trabajen correctamente juntos

## 🚀 Quick Start

### Para Risk Management Testing
```bash
# Deploy v2.2 con mejoras de risk management
tools/deploy_all.ps1

# Verificar preset en UI: MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10
# Configurar position sizing mode y risk amount
# Usar botón "Refresh diagnostics" para log instantáneo
# Ejecutar trades y verificar logs con grep commands
```

### Para Confluence Testing
```bash
# Usar escenarios A-F pre-configurados
tools/analizar_escenario.bat [A|B|C|D|E|F]
```