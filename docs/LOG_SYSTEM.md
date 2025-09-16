# Sistema de Logs ATAS 468 Strategy

## 📋 Descripción del Sistema

El sistema de logs utiliza **doble escritura** para máxima robustez:

### 1. **Archivo Persistente** (Histórico completo)
- **Ruta**: `EMERGENCY_ATAS_LOG.txt` (en raíz del proyecto)
- **Contenido**: Todas las sesiones de ATAS acumuladas
- **Propósito**: Histórico permanente para análisis a largo plazo

### 2. **Archivo de Sesión** (Solo sesión actual)
- **Ruta**: `ATAS_SESSION_LOG.txt` (en raíz del proyecto)
- **Contenido**: Solo la sesión actual de ATAS
- **Propósito**: Debug de la sesión en curso (se limpia en cada nueva sesión)

### 3. **Archivo de Control de Sesión**
- **Ruta**: `ATAS_SESSION_ID.tmp` (en raíz del proyecto)
- **Contenido**: PID del proceso ATAS actual
- **Propósito**: Detectar cuando ATAS se reinicia

## 🔄 Detección de Nueva Sesión

**Criterio**: Se considera nueva sesión cuando:
1. No existe `ATAS_SESSION_ID.tmp`, O
2. El PID almacenado es diferente al proceso actual ATAS

**Acciones en nueva sesión**:
1. Se limpia completamente `ATAS_SESSION_LOG.txt`
2. Se escribe header de nueva sesión en ambos archivos
3. Se actualiza `ATAS_SESSION_ID.tmp` con el nuevo PID

## 📁 Estructura de Archivos

```
06_ATAS_strategy - v2/
├── EMERGENCY_ATAS_LOG.txt      ← Persistente (TODAS las sesiones)
├── ATAS_SESSION_LOG.txt        ← Solo sesión actual (se limpia)
├── ATAS_SESSION_ID.tmp         ← PID de control
└── src/...
```

## 🛠️ Uso Práctico

### Para debuggar la sesión actual:
```bash
# Ver en tiempo real
tail -f ATAS_SESSION_LOG.txt

# Buscar señales de compra en sesión actual
grep "CAPTURE.*BUY" ATAS_SESSION_LOG.txt
```

### Para análisis histórico:
```bash
# Buscar patrones en todas las sesiones
grep "CAPTURE.*BUY" EMERGENCY_ATAS_LOG.txt

# Ver inicios de todas las sesiones
grep "NEW ATAS SESSION" EMERGENCY_ATAS_LOG.txt

# Analizar errores recurrentes
grep "ERROR\|ABORT" EMERGENCY_ATAS_LOG.txt
```

### Para análisis de confluencias:
```bash
# Confluencias que fallan frecuentemente
grep "ABORT ENTRY: Conf#" EMERGENCY_ATAS_LOG.txt | sort | uniq -c

# Secuencia completa de una señal específica
UID="12345678"
grep "$UID" ATAS_SESSION_LOG.txt
```

## 🎯 Ventajas del Sistema

1. **Robustez**: Doble escritura garantiza que no se pierdan logs
2. **Sesión limpia**: Archivo de sesión siempre empieza vacío
3. **Histórico completo**: El archivo persistente mantiene todo
4. **Detección automática**: No requiere intervención manual
5. **Análisis flexible**: Dos niveles de granularidad (sesión vs histórico)

## 🔧 Configuración

El sistema es **completamente automático**. No requiere configuración manual.

Los logs se escriben automáticamente cuando:
- Se inicializa cualquier indicador/estrategia
- Se ejecutan métodos de logging (`DebugLog.Critical()`, `DebugLog.W()`, etc.)
- Se detecta una nueva sesión de ATAS

## 📊 Headers de Sesión

Cada nueva sesión escribe un header como:
```
=== NEW ATAS SESSION STARTED at 2024-01-15 14:30:25 (PID: 12345) ===
```

Esto permite identificar fácilmente el inicio de cada sesión en ambos archivos.