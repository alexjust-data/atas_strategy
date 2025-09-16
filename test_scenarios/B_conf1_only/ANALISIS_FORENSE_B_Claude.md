# ANÁLISIS FORENSE ESCENARIO B - CONF1_ONLY
**Sesión:** ATAS_SESSION_LOG_B_results.txt
**Escenario:** Solo GenialLine slope activa (CONF#2 deshabilitada)
**Fecha:** 2025-09-16

## RESUMEN EJECUTIVO

### Estadísticas Generales
- **Total señales capturadas:** 16
- **CONF#1 válidas:** 7 (43.75%)
- **CONF#1 fallos:** 6 (37.5%)
- **Órdenes ejecutadas:** 6 (37.5%)
- **Ejecución vs Válidas:** 6/7 = 85.7% **⚠️ PROBLEMA DETECTADO**

### PROBLEMA CRÍTICO IDENTIFICADO
**1 señal válida CONF#1 NO fue ejecutada** - Hay un fallo en el flujo de ejecución.

## ANÁLISIS DETALLADO DE SEÑALES

### Señales Ejecutadas (6/7 válidas)
1. **N=17526 SELL** ✅
   - `[18:13:30.873] CONF#1 (GL slope @N+1) gN=19898,33566 gN1=19898,10000 trend=DOWN -> OK`
   - `[18:13:30.885] MARKET ORDER SENT: SELL 1 at N+1 (bar=17527)`

2. **N=17576 BUY** ✅
   - `[18:14:27.165] CONF#1 (GL slope @N+1) gN=19904,99743 gN1=19905,55993 trend=UP -> OK`
   - `[18:14:27.166] MARKET ORDER SENT: BUY 1 at N+1 (bar=17577)`

3. **N=17586 BUY** ✅
   - `[18:14:34.242] CONF#1 (GL slope @N+1) gN=19907,28787 gN1=19907,32279 trend=UP -> OK`
   - `[18:14:34.244] MARKET ORDER SENT: BUY 1 at N+1 (bar=17587)`

4. **N=17597 BUY** ✅
   - `[18:14:43.919] CONF#1 (GL slope @N+1) gN=19909,12022 gN1=19909,18382 trend=UP -> OK`
   - `[18:14:43.921] MARKET ORDER SENT: BUY 1 at N+1 (bar=17598)`

5. **N=17601 SELL** ✅
   - `[18:14:45.995] CONF#1 (GL slope @N+1) gN=19909,10331 gN1=19909,09265 trend=DOWN -> OK`
   - **⚠️ NO HAY LOG DE MARKET ORDER SENT INMEDIATO**

6. **N=17602 BUY** ✅
   - `[18:14:48.147] CONF#1 (GL slope @N+1) gN=19909,05294 gN1=19909,20000 trend=UP -> OK`
   - `[18:14:48.149] MARKET ORDER SENT: BUY 1 at N+1 (bar=17603)`

7. **N=17621 SELL** ✅
   - `[18:15:27.704] CONF#1 (GL slope @N+1) gN=19903,13824 gN1=19902,78382 trend=DOWN -> OK`
   - `[18:15:27.705] MARKET ORDER SENT: SELL 1 at N+1 (bar=17622)`

### Señales Rechazadas Correctamente (6)
1. **N=17525 BUY** - CONF#1 FAIL (trend=DOWN pero señal BUY)
2. **N=17571 SELL** - CONF#1 FAIL (trend=UP pero señal SELL)
3. **N=17582 SELL** - CONF#1 FAIL (trend=UP pero señal SELL)
4. **N=17593 SELL** - CONF#1 FAIL (trend=UP pero señal SELL)
5. **N=17605 SELL** - CONF#1 FAIL (trend=UP pero señal SELL)
6. **N=17620 BUY** - CONF#1 FAIL (trend=DOWN pero señal BUY)

### Capturas Sin Análisis CONF#1 (3)
- **N=17572 BUY** - No hay log de CONF#1
- **N=17608 BUY** - No hay log de CONF#1
- **N=17603 SELL** - No hay log de CONF#1

## INVESTIGACIÓN DE LA ANOMALÍA

### Caso Problemático: N=17601 SELL
```
[18:14:45.988] CAPTURE: N=17601 SELL uid=0f158d90-ab45-4e02-9783-8b5ed38d0699
[18:14:45.995] CONF#1 (GL slope @N+1) gN=19909,10331 gN1=19909,09265 trend=DOWN -> OK
[18:14:46.387] CAPTURE: N=17602 BUY uid=98514840-f1a4-4638-a084-e7dcc7ab494f
[18:14:48.147] CONF#1 (GL slope @N+1) gN=19909,05294 gN1=19909,20000 trend=UP -> OK
[18:14:48.149] MARKET ORDER SENT: BUY 1 at N+1 (bar=17603)
```

**PROBLEMA:** La señal N=17601 SELL pasó CONF#1 pero nunca generó MARKET ORDER SENT.
La siguiente señal N=17602 BUY sí se ejecutó correctamente.

### Posibles Causas
1. **Trade lock activo** - La estrategia podría estar bloqueada por una operación anterior
2. **Timing crítico** - La señal N=17602 podría haber interrumpido el procesamiento de N=17601
3. **CONF#2 phantom check** - Aunque CONF#2 está deshabilitada, podría estar interfiriendo
4. **Guard OnlyOnePosition** - Posible bloqueo no documentado en logs

## CONCLUSIONES

### ✅ CONF#1 Funciona Correctamente
- **Precisión:** 7 válidas de 13 evaluadas = 53.8%
- **Rechazo correcto:** 6 señales con direcciones incorrectas
- **Lógica:** Perfecta detección de tendencia vs dirección de señal

### ⚠️ Fallo de Ejecución Detectado
- **1 de 7 señales válidas no se ejecutó** (N=17601)
- **Tasa de ejecución real:** 85.7% (debería ser 100%)
- **Requiere investigación adicional**

### Recomendaciones
1. Investigar logs detallados del período 18:14:45.995 - 18:14:46.387
2. Verificar estado del trade lock durante N=17601
3. Analizar interferencia entre señales consecutivas N=17601/N=17602
4. Confirmar que CONF#2 está realmente deshabilitada en este escenario