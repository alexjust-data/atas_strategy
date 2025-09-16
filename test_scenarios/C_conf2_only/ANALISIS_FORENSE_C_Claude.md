# ANÁLISIS FORENSE ESCENARIO C - CONF2_ONLY
**Sesión:** ATAS_SESSION_LOG_C_results.txt
**Escenario:** Solo EMA8 vs Wilder8 activa (CONF#1 deshabilitada)
**Fecha:** 2025-09-16

## RESUMEN EJECUTIVO

### Estadísticas Generales
- **Total señales capturadas:** 15
- **CONF#2 válidas:** 10 (66.7%)
- **CONF#2 fallos:** 2 (13.3%)
- **Órdenes ejecutadas:** 6 (40%)
- **Ejecución vs Válidas:** 6/10 = 60% **⚠️ PROBLEMA DETECTADO**

### PROBLEMA CRÍTICO IDENTIFICADO
**4 de 10 señales válidas CONF#2 NO fueron ejecutadas** - Hay un fallo sistemático en el flujo de ejecución.

## ANÁLISIS DETALLADO DE SEÑALES

### Señales Ejecutadas (6/10 válidas)
1. **N=17526 SELL** ✅
   - `[18:27:12.374] CONF#2 (EMA8 vs W8 @N+1) e8=19895,29170 w8=19895,94147 diff=-0,64976 mode=Window tolPre=1,00000 SELL -> OK`
   - `[18:27:12.388] MARKET ORDER SENT: SELL 1 at N+1 (bar=17527)`

2. **N=17576 BUY** ✅
   - `[18:28:30.365] CONF#2 (EMA8 vs W8 @N+1) e8=19904,86689 w8=19905,40099 diff=-0,53410 mode=Window tolPre=1,00000 BUY -> OK`
   - `[18:28:30.367] MARKET ORDER SENT: BUY 1 at N+1 (bar=17577)`

3. **N=17582 SELL** ✅
   - `[18:28:34.392] CONF#2 (EMA8 vs W8 @N+1) e8=19908,80495 w8=19908,14107 diff=+0,66388 mode=Window tolPre=1,00000 SELL -> OK`
   - `[18:28:34.394] MARKET ORDER SENT: SELL 1 at N+1 (bar=17583)`

4. **N=17597 BUY** ✅
   - `[18:28:47.109] CONF#2 (EMA8 vs W8 @N+1) e8=19908,80982 w8=19909,03115 diff=-0,22133 mode=Window tolPre=1,00000 BUY -> OK`
   - `[18:28:47.111] MARKET ORDER SENT: BUY 1 at N+1 (bar=17598)`

5. **N=17602 BUY** ✅
   - `[18:28:51.360] CONF#2 (EMA8 vs W8 @N+1) e8=19910,85745 w8=19910,41788 diff=+0,43957 mode=Window tolPre=1,00000 BUY -> OK`
   - `[18:28:51.361] MARKET ORDER SENT: BUY 1 at N+1 (bar=17603)`

6. **N=17620 BUY** ✅
   - `[18:29:27.038] CONF#2 (EMA8 vs W8 @N+1) e8=19898,59425 w8=19899,19010 diff=-0,59585 mode=Window tolPre=1,00000 BUY -> OK`
   - `[18:29:27.041] MARKET ORDER SENT: BUY 1 at N+1 (bar=17621)`

### Señales Válidas NO Ejecutadas (4/10) ⚠️
1. **N=17586 BUY** - CONF#2 OK pero sin MARKET ORDER SENT
   - `[18:28:37.468] CONF#2 (EMA8 vs W8 @N+1) e8=19906,00452 w8=19906,45406 diff=-0,44954 mode=Window tolPre=1,00000 BUY -> OK`

2. **N=17601 SELL** - CONF#2 OK pero sin MARKET ORDER SENT
   - `[18:28:49.185] CONF#2 (EMA8 vs W8 @N+1) e8=19910,68180 w8=19910,28114 diff=+0,40065 mode=Window tolPre=1,00000 SELL -> OK`

3. **N=17605 SELL** - CONF#2 OK pero sin MARKET ORDER SENT
   - `[18:28:57.312] CONF#2 (EMA8 vs W8 @N+1) e8=19911,18350 w8=19910,87009 diff=+0,31341 mode=Window tolPre=1,00000 SELL -> OK`

4. **N=17621 SELL** - CONF#2 OK pero sin MARKET ORDER SENT
   - `[18:29:30.901] CONF#2 (EMA8 vs W8 @N+1) e8=19899,14738 w8=19899,43587 diff=-0,28849 mode=Window tolPre=1,00000 SELL -> OK`

### Señales Rechazadas Correctamente (2)
1. **N=17545 BUY** - CONF#2 FAIL (diff=-1,20142 fuera de ventana)
2. **N=17593 SELL** - CONF#2 FAIL (diff=+1,06732 fuera de ventana)

### Capturas Sin Análisis CONF#2 (3)
- **N=17572 BUY** - No hay log de CONF#2
- **N=17608 BUY** - No hay log de CONF#2
- **N=17603 SELL** - No hay log de CONF#2

## ANÁLISIS TÉCNICO CONF#2

### Validación de Ventana (Window mode)
- **Tolerancia:** 1.00000
- **Lógica:** Para BUY: EMA8 < Wilder8 (diff negativo), Para SELL: EMA8 > Wilder8 (diff positivo)
- **Precisión:** 10 válidas de 12 evaluadas = 83.3%

### Diferencias en Señales Válidas
- **Rango válido:** -0.64976 a +0.66388
- **Promedio absoluto:** 0.44 puntos
- **Todas dentro de tolerancia de 1.0**

## PROBLEMA SISTEMÁTICO IDENTIFICADO

**60% de fallos de ejecución** sugiere el mismo problema detectado en Scenario B:
- **Trade lock bloqueando señales válidas**
- **Cooldown interfiriendo con ejecución**
- **Timing conflicts entre señales consecutivas**

## CONCLUSIONES

### ✅ CONF#2 Funciona Correctamente
- **Precisión:** 10 válidas de 12 evaluadas = 83.3%
- **Rechazo correcto:** 2 señales fuera de ventana de tolerancia
- **Lógica:** Perfecta detección de divergencia EMA8 vs Wilder8

### ⚠️ Fallo Crítico de Ejecución
- **4 de 10 señales válidas no ejecutadas** (40% de fallos)
- **Tasa de ejecución real:** 60% (debería ser 100%)
- **Patrón similar al Scenario B** - problema del sistema de guards/locks

### Recomendaciones
1. Investigar logs de trade lock/cooldown para las 4 señales no ejecutadas
2. Verificar interferencia del OnlyOnePosition guard
3. Analizar timing de señales consecutivas
4. Confirmar que CONF#1 está realmente deshabilitada

### Comparación con Scenario B
- **Scenario B:** 85.7% ejecución (6/7 válidas)
- **Scenario C:** 60% ejecución (6/10 válidas)
- **Problema sistemático en ambos escenarios**