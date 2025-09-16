# ValidateGLCrossOnCloseN.md

## Qué es

Activa la **validación de cruce de Genial Line al cierre de la vela N**. La señal solo se arma si el **Close(N)** ha cruzado la GL (no cuenta intrabar).

## Cuándo corre

En el **cierre de N** (señal). La ejecución es en **N+1**.

## Lógica

* **BUY**: `Close(N)` cruza **de abajo a arriba** la GL y **cierra** por encima de la GL (respetando Hysteresis si aplica).
* **SELL**: `Close(N)` cruza **de arriba a abajo** y **cierra** por debajo.

## Interacciones

* **HysteresisTicks** añade un umbral extra a este cruce (ver su doc).
* Confluencias se evalúan **en N+1**, después de esta validación.

## Recomendado

**ON** (evita señales por ruido intrabar).

## Ejemplo rápido

* La vela pincha sobre la GL pero **cierra** por debajo ⇒ **NO** hay señal.
* La vela **cierra** 2 ticks por encima ⇒ **SÍ** hay señal (si Hysteresis ≤ 2).

## Logs útiles (grep)

```
grep -nE "GENIAL CROSS detected|CAPTURE: N=.*\(confirmed close\)" EMERGENCY_ATAS_LOG.txt
```


