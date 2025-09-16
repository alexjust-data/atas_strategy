

# AttachBracketsFromActualNetFill.md

## Qué es

Cuelga **TP/SL después de la entrada** (post-fill), usando la **posición neta real** (`net`). Evita desalineaciones cuando hay fills parciales.

## Cuándo corre

* En `OnOrderChanged` de la entrada (Placed/PartlyFilled/Filled).
* **Fallback** en `OnPositionChanged` por si el portfolio actualiza tarde.

## Lógica

* Si `net>0` ⇒ crear **tantas patas** como `net` (máx 3): TP1/TP2/TP3 con su **OCO** (cada TP con su SL).
* **Reconciliación continua** en cada `OnOrderChanged`:

  * Si hay **más TPs que net** ⇒ cancelar sobrantes.
  * Si suma de **SL ≠ net** ⇒ recrear 1 SL con `qty=net`.

## Anti-“secuencia mortal”

* **AutoCancel=false** en TP/SL (limpieza es manual al confirmar plano).
* **AntiFlatLockMs** impide liberar/cancelar por `net=0` fantasma inmediatamente después de colgar brackets.

## Recomendado

**ON** (crítico para robustez operativa).

## Logs útiles (grep)

```
grep -nE "BRACKETS ATTACHED|BRACKETS NOT ATTACHED|ReconcileBracketsWithNet" EMERGENCY_ATAS_LOG.txt
grep -nE "ANTI-FLAT|FLAT CONFIRMED|Trade (candado|lock) RELEASED" EMERGENCY_ATAS_LOG.txt
```


