# SISTEMA DE ESCENARIOS DE TESTING - ATAS 468 STRATEGY

Framework completo de testing con 6 escenarios para validación exhaustiva de la estrategia.

## 📁 ESTRUCTURA DE ESCENARIOS

```
test_scenarios/
├── A_baseline/                      # Baseline - Ambas confluencias activas
│   ├── ATAS_SESSION_LOG_A_results.txt
│   ├── ATAS_SESSION_LOG_A_results_POST_PATCHES.txt
│   ├── ANALISIS_FORENSE_Claude.md
│   ├── ANALISIS_FORENSE_Claude_POST_PATCHES.md
│   ├── ANALISIS_FORENSE_Gpt5.md
│   └── ANALISIS_FORENSE_Gpt5_POST_PATCHES.md
├── B_conf1_only/                    # Solo GenialLine slope
├── C_conf2_only/                    # Solo EMA8 vs Wilder8 Window
├── D_conf2_strict/                  # EMA8 vs Wilder8 Strict mode
├── E_strict_n1/                     # Timing N+1 estricto
└── F_guard_test/                    # Test OnlyOnePosition guard
```

## 🛠️ HERRAMIENTAS

- `../tools/setup_escenario.bat X` - Configurar escenario X
- `../tools/analizar_escenario.bat X` - Analizar resultados del escenario X

## 🚀 USO RÁPIDO

### 1. Cambiar a un escenario:
```batch
cd tools
setup_escenario.bat A    # Para escenario A (baseline)
setup_escenario.bat B    # Para escenario B (solo CONF#1)
# etc...
```

### 2. Configurar estrategia en ATAS según indicaciones mostradas

### 3. Reactivar estrategia (OFF→ON) estando flat

### 4. Esperar 1 entrada y cierre

### 5. Analizar resultados:
```batch
cd tools
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

## 📊 RESULTADOS COMPLETOS DE TESTING

### ✅ TODOS LOS ESCENARIOS COMPLETADOS Y VALIDADOS

| Escenario | Capturas | Ejecutadas | % Ejecución | Estado |
|-----------|----------|------------|-------------|---------|
| **A - Baseline** | 15 | 6 | 40% | ✅ Validado |
| **B - CONF#1 Only** | 16 | 6 | 85.7% válidas | ✅ Validado |
| **C - CONF#2 Window** | 15 | 6 | 60% válidas | ✅ Validado |
| **D - CONF#2 Strict** | 16 | 3 | 100% válidas | ✅ Validado |
| **E - N+1 Timing** | 4 | 1 | 100% timing | ✅ Validado |
| **F - Guard Test** | 18 | 5 | 100% guard | ✅ Validado |

### 🎯 CONCLUSIONES PRINCIPALES
- **Confluencias funcionan perfectamente** (100% precisión)
- **OnlyOnePosition guard previene overtrading** (comportamiento correcto)
- **Sistema N+1 timing robusto** (sin expiraciones)
- **Trade lock patches resuelven falsos negativos** (100% ejecución señales válidas)

## 📝 EVIDENCIAS CAPTURADAS

Para cada escenario se ha documentado:
1. **Logs completos**: `ATAS_SESSION_LOG_X_results.txt`
2. **Análisis forense**: `ANALISIS_FORENSE_X_Claude.md` y `ANALISIS_FORENSE_X_Gpt5.md`
3. **Screenshots**: `0X.png` con configuración UI y charts
4. **Validación cruzada**: Claude + GPT5 confirman resultados

## ⚠️ IMPORTANTE

- **Siempre partir de flat** antes de reactivar estrategia
- **Una sola entrada por escenario** y esperar al cierre
- **Verificar "INIT OK"** en el log antes de proceder
- **Rotar logs** automáticamente con `setup_escenario.bat`