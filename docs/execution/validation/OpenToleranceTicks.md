

# OpenToleranceTicks.md

## Qué es

Desviación máxima permitida entre el precio de ejecución y **Open(N+1)**.

## Cuándo corre

En el intento de ejecución **N+1** (con o sin Strict activado).

## Lógica

* Si `abs(PriceExec − Open(N+1)) > Ticks(OpenToleranceTicks)` ⇒ **ABORT ENTRY** (fuera de tolerancia).

## Recomendado

**1–2 ticks** (3 si el activo es muy volátil).

## Ejemplo

* Open(N+1)=19912.00; Tolerancia=2 ticks.
* Precio de ejecución=19912.75 (3 ticks por encima) ⇒ **ABORT**.

## Logs útiles (grep)

```
grep -nE "beyond open tolerance|ABORT ENTRY" EMERGENCY_ATAS_LOG.txt
```


