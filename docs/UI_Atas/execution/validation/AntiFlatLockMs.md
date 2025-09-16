

# AntiFlatLockMs.md

## Qué es

Ventana (p.ej. **400 ms**) tras colgar brackets en la que **ignoras** un `net==0` transitorio. Evita que se libere el lock y se cancelen hijos por error.

## Por qué (la “secuencia mortal”)

1. `BRACKETS ATTACHED`
2. `GetNetPosition=0` justo después (portfolio aún no refleja)
3. **Lock liberado** ⇒ con `AutoCancel=true`, el broker/ATAS **cancela TP/SL**
4. Desaparecen brackets

## Lógica

* Si `net==0` **dentro** de la ventana ⇒ **no** liberar ni cancelar (asumir glitch).
* Al salir de la ventana: si **sigue** `net==0` ⇒ **cancelar hijos** y **liberar** (plano confirmado).

## Recomendado

**300–500 ms** (default 400 ms).

## Logs útiles (grep)

```
grep -nE "ANTI-FLAT|FLAT CONFIRMED|Trade (candado|lock) RELEASED" EMERGENCY_ATAS_LOG.txt
```

## Notas

* Funciona junto con **AutoCancel=false** y la **reconciliación continua** para que los brackets no desaparezcan injustificadamente.
