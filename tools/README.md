# Tools Directory

Scripts y herramientas para desarrollo y análisis de la estrategia ATAS 468.

## 📋 Scripts de Análisis

### `analizar_escenario.bat`
Herramienta principal para análisis forense de escenarios de testing.

**Uso:**
```bash
analizar_escenario.bat [A|B|C|D|E|F]
```

**Escenarios disponibles:**
- **A - BASELINE**: Analiza ambas confluencias + guard + ejecuciones
- **B - CONF1_ONLY**: Analiza solo confluence GL slope
- **C - CONF2_ONLY**: Analiza solo confluence EMA8 vs Wilder8
- **D - CONF2_STRICT**: Analiza fallos de confluence EMA8 estricta
- **E - STRICT_N1**: Analiza timing y expiraciones N+1
- **F - GUARD_TEST**: Analiza comportamiento OnlyOnePosition guard

### `setup_escenario.bat`
Configuración automática de escenarios de testing.

**Funcionalidad:**
- Backup automático de logs existentes
- Instrucciones específicas de configuración ATAS
- Preparación del entorno para testing

## 🚀 Scripts de Deployment

### PowerShell Scripts
- `deploy_all.ps1` - Deployment completo (indicadores + estrategias)
- `deploy_indicators.ps1` - Solo indicadores
- `deploy_strategies.ps1` - Solo estrategias
- `deploy_simple.ps1` - Deployment básico
- `clean_atas.ps1` - Limpieza de archivos ATAS

**Uso típico:**
```powershell
.\deploy_all.ps1  # Deployment completo tras cambios
```

## 📊 Integración con Logs

Los scripts están configurados para trabajar con la nueva estructura de logs:
- Lee de: `../logs/current/ATAS_SESSION_LOG.txt`
- Backup en: `../logs/current/ATAS_SESSION_LOG_backup_*.txt`
- Logs de emergencia: `../logs/emergency/EMERGENCY_ATAS_LOG.txt`

## 🔍 Análisis Automático

Cada script de análisis proporciona:
- Conteo automático de señales y ejecuciones
- Análisis de confluencias (OK/FAIL)
- Seguimiento de decisiones del guard
- Estadísticas de timing N+1
- Detección de trade lock releases
