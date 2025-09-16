# TopUpMissingQtyToTarget.md

## Qué es

Si la entrada se llenó **por debajo** del objetivo (p.ej. querías 3 y entró 1), envía una **market adicional** para **completar** la cantidad.

## Cuándo corre

Tras detectar **fill parcial** de la orden de entrada.

## Lógica

* `missing = targetQty − net`
* Si `missing > 0` ⇒ enviar **top-up** por `missing`.

## Riesgos

* Aumenta exposición en momentos de **slippage**/latencia.
* Puede disparar **doble coste** si el mercado se aleja rápido.

## Recomendado

**OFF** al inicio. Activarlo solo si necesitas **forzar tamaño**.

## Logs útiles (grep)

```
grep -n "TOP-UP: missing" EMERGENCY_ATAS_LOG.txt
```

