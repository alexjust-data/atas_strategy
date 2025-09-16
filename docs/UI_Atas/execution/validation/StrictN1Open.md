

# StrictN1Open.md

## Qué es

Ejecuta **estrictamente en la apertura de N+1** (primer tick). Si no se puede, la señal **expira** (o se aborta por tolerancia).

## Cuándo corre

En la transición **N → N+1** (ventana única).

## Lógica / Máquina de estados

* `ARMED` (espera a N+1)
* `PROCESSING PENDING @N+1` (intento de ejecutar en el primer tick)
* Si se pierde: `PENDING EXPIRED: missed N+1`

## Interacciones

* **OpenToleranceTicks** permite un desvío máximo vs `Open(N+1)`.
* Con **OnlyOnePosition** y **cooldown** la ejecución puede bloquearse aunque esté armado.

## Recomendado

**ON** para control de riesgo y repetibilidad.

## Ejemplo

* Open(N+1)=19912.00; si el mejor precio disponible en ese primer tick ya excede la tolerancia, **no ejecuta**.

## Logs útiles (grep)

```
grep -nE "PENDING ARMED|PROCESSING PENDING @N\+1|PENDING EXPIRED" EMERGENCY_ATAS_LOG.txt
```


