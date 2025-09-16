# Logs Directory

Sistema de logging dual para la estrategia ATAS 468.

## Estructura de Directorios

### `current/` - Logs de Sesión Actual
- **ATAS_SESSION_LOG.txt** - Log principal de la sesión actual (se limpia cada nueva sesión)
- **ATAS_SESSION_ID.tmp** - Control de PID para detección de nuevas sesiones

### `emergency/` - Logs Persistentes
- **EMERGENCY_ATAS_LOG.txt** - Log persistente histórico (TODAS las sesiones acumuladas)

## Sistema de Detección de Sesiones

La estrategia detecta automáticamente nuevas sesiones ATAS:
- **Nueva sesión**: Cuando no existe PID o el PID cambió
- **Acción**: Limpia ATAS_SESSION_LOG.txt y escribe headers
- **Persistencia**: EMERGENCY_ATAS_LOG.txt mantiene histórico completo

## Uso Práctico

### Monitoreo General
```bash
# Monitorear sesión actual en tiempo real
tail -f logs/current/ATAS_SESSION_LOG.txt

# Buscar patrones históricos
grep "MARKET ORDER SENT" logs/emergency/EMERGENCY_ATAS_LOG.txt

# Detectar nuevas sesiones
grep "NEW ATAS SESSION" logs/emergency/EMERGENCY_ATAS_LOG.txt
```

### Risk Management Analysis (NEW v2.2)
```bash
# Ver todos los diagnósticos de risk management
grep -n "DIAG \[" logs/current/ATAS_SESSION_LOG.txt

# Ver detección de tick values y mismatches
grep -nE "TICK-VALUE|MinStepPrice|override|auto-detected|MISMATCH" logs/current/ATAS_SESSION_LOG.txt

# Ver cálculos de auto-qty y protección underfunded
grep -n "AUTOQTY\|ABORT ENTRY\|Underfunded" logs/current/ATAS_SESSION_LOG.txt

# Ver detección de account equity
grep -nE "ACCOUNT EQUITY|auto-detected|override" logs/current/ATAS_SESSION_LOG.txt

# Ver errores de parsing
grep -n "GetTickValueFromOverrides\|failed" logs/current/ATAS_SESSION_LOG.txt
```

### Confluence Testing
```bash
# Análisis de escenarios confluence
cd tools && ./analizar_escenario.bat [A|B|C|D|E|F]
```

## Configuración

Los paths están configurados en `src/MyAtas.Shared/Shared/DebugLog.cs`:
- Sistema automático de creación de directorios
- Manejo robusto de archivos bloqueados
- Thread-safe para operaciones concurrentes
