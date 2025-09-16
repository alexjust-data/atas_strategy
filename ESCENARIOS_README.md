# SISTEMA DE ESCENARIOS DE TESTING

## 📋 ARCHIVOS CREADOS

```
ATAS_SESSION_LOG_A_baseline.txt     ← Escenario A: Baseline (ambas confluencias)
ATAS_SESSION_LOG_B_conf1_only.txt   ← Escenario B: Solo GL slope
ATAS_SESSION_LOG_C_conf2_only.txt   ← Escenario C: Solo EMA8 vs Wilder8
ATAS_SESSION_LOG_D_conf2_strict.txt ← Escenario D: EMA8 vs Wilder8 estricto
ATAS_SESSION_LOG_E_strict_n1.txt    ← Escenario E: Control timing N+1
ATAS_SESSION_LOG_F_guard_test.txt   ← Escenario F: Test OnlyOnePosition guard

setup_escenario.bat                  ← Script para cambiar escenarios
analizar_escenario.bat              ← Script para analizar resultados
```

## 🚀 USO RÁPIDO

### 1. Cambiar a un escenario:
```batch
setup_escenario.bat A    # Para escenario A (baseline)
setup_escenario.bat B    # Para escenario B (solo CONF#1)
# etc...
```

### 2. Configurar estrategia en ATAS según indicaciones mostradas

### 3. Reactivar estrategia (OFF→ON) estando flat

### 4. Esperar 1 entrada y cierre

### 5. Analizar resultados:
```batch
analizar_escenario.bat A    # Analiza escenario A
analizar_escenario.bat B    # Analiza escenario B
# etc...
```

## 📊 ESCENARIOS DETALLADOS

### A - BASELINE (Comportamiento Normal)
**Objetivo**: Verificar que solo entra con ambas confluencias válidas
**Config**: GL slope=ON, EMA8 vs Wilder8=ON, Window, Tolerance=2
**Esperado**: CONF#1 OK + CONF#2 OK → GUARD PASS → MARKET SENT

### B - CONF1_ONLY (Solo Pendiente GenialLine)
**Objetivo**: Verificar entrada solo por pendiente GL
**Config**: GL slope=ON, EMA8 vs Wilder8=OFF
**Esperado**: Solo CONF#1 OK → MARKET SENT (sin verificar CONF#2)

### C - CONF2_ONLY (Solo EMA8 vs Wilder8)
**Objetivo**: Verificar entrada solo por ventana EMA/Wilder
**Config**: GL slope=OFF, EMA8 vs Wilder8=ON, Tolerance=4
**Esperado**: Solo CONF#2 OK → MARKET SENT (sin verificar CONF#1)

### D - CONF2_STRICT (EMA8 Estricto)
**Objetivo**: Provocar fallos deterministas de CONF#2
**Config**: GL slope=OFF, EMA vs Wilder=ON, Tolerance=0, Count equality=OFF
**Esperado**: Múltiples CONF#2 FAIL → ABORT ENTRY

### E - STRICT_N1 (Control de Timing)
**Objetivo**: Probar expiración por timing estricto
**Config**: Strict N+1=ON, Open tolerance=0, Ambas confluencias=ON
**Esperado**: PENDING EXPIRED si primer tick fuera de tolerancia

### F - GUARD_TEST (Guardia OnlyOnePosition)
**Objetivo**: Verificar bloqueo durante posiciones activas
**Config**: Como A + Cooldown=2 barras
**Esperado**: GUARD BLOCK mientras active=True, GUARD PASS tras Trade lock RELEASED

## 🔍 GREPS ÚTILES POR ESCENARIO

**A - Baseline**:
```bash
grep -nE "CAPTURE: N=|CONF#1 .* -> |CONF#2 .* -> |GUARD OnlyOnePosition|MARKET ORDER SENT|BRACKETS ATTACHED" ATAS_SESSION_LOG.txt
```

**B - CONF1 Only**:
```bash
grep -nE "CAPTURE: N=|CONF#1 .* -> (OK|FAIL)|ABORT ENTRY: Conf#1|MARKET ORDER SENT" ATAS_SESSION_LOG.txt
```

**C - CONF2 Only**:
```bash
grep -nE "CAPTURE: N=|CONF#2 .* -> (OK|FAIL)|ABORT ENTRY: Conf#2|MARKET ORDER SENT" ATAS_SESSION_LOG.txt
```

**D - CONF2 Strict**:
```bash
grep -nE "CONF#2 .* -> FAIL|ABORT ENTRY: Conf#2 failed" ATAS_SESSION_LOG.txt
```

**E - Strict N+1**:
```bash
grep -nE "PENDING ARMED|PROCESSING PENDING @N\+1|PENDING EXPIRED|beyond open tolerance|first tick" ATAS_SESSION_LOG.txt
```

**F - Guard Test**:
```bash
grep -nE "GUARD OnlyOnePosition|Trade lock RELEASED" ATAS_SESSION_LOG.txt
```

## 📝 EVIDENCIAS A CAPTURAR

Para cada escenario necesitas:
1. **Log completo**: `ATAS_SESSION_LOG.txt` tras la prueba
2. **Screenshot UI**: Configuración de la estrategia en ATAS
3. **Screenshot Chart**: Chart mostrando N y N+1 con GL, EMA8 y Wilder8
4. **Análisis**: Salida del comando `analizar_escenario.bat X`

## ⚠️ IMPORTANTE

- **Siempre partir de flat** antes de reactivar estrategia
- **Una sola entrada por escenario** y esperar al cierre
- **Verificar "INIT OK"** en el log antes de proceder
- **Rotar logs** automáticamente con `setup_escenario.bat`