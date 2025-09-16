Perfecto, Alex — ya tengo tu **escenario A (baseline)** analizado con lupa usando los ficheros que me enviaste (estrategia + logs). Voy directo al grano.

# Hallazgos clave (forense)

1. **Se detectan 16 señales “CAPTURE: N=”**

* **Conf#1 (pendiente GenialLine)**: 7 OK, 6 FAIL, 3 sin evaluar por abortos previos.
* **Conf#2 (EMA8 vs Wilder – Window)**: **7 OK, 0 FAIL** (en este tramo no falló).
* **Entradas realmente lanzadas**: **1** (SELL en N=17526).
* **Brackets**: se adjuntan correctamente tras ese fill (SL enviado, TP enviado y SL cancelado al cerrarse por TP).
* **Cierres**: el TP cierra la posición a las **14:58:43.361** y cancela el SL (queda constancia en el log).

2. **Bloqueos y “falsos negativos”**

* Tras el único trade, el **candado OnlyOnePosition queda activo** sin liberarse, **aunque net=0 y no hay órdenes activas** (“active=True net=0 activeOrders=0 cooldown=NO -> BLOCK”).
* No aparece **ningún** log de “Trade lock RELEASED …”, pese a que tu código los emite al liberar el candado por watchdog o por eventos de posición/órdenes.
* Resultado: varias señales posteriores con **Conf#1 OK & Conf#2 OK** **no entran** por **bloqueo del candado** (falsos negativos).

3. **Abortos adicionales bien justificados**

* Varias señales abortan con: **“Candle direction at N does not match signal”** (coherente con tu validación de dirección en N).
* Otros abortos por **“Conf#1 failed”** son correctos: la pendiente de GenialLine registra **trend opuesto** a la dirección de la señal.

---

# Línea de tiempo (resumen de las 16 capturas)

| #  | Hora         | N     | Side | Conf#1   | Conf#2 | Resultado                                    |
| -- | ------------ | ----- | ---- | -------- | ------ | -------------------------------------------- |
| 1  | 14:57:39.411 | 17525 | BUY  | **FAIL** | —      | ABORT: Conf#1 failed                         |
| 2  | 14:57:40.403 | 17526 | SELL | OK       | OK     | **ENTRY SENT** → TP fill 14:58:43, SL cancel |
| 3  | 14:58:59.926 | 17545 | BUY  | **FAIL** | —      | ABORT: Conf#1 failed                         |
| 4  | 14:59:21.016 | 17572 | BUY  | —        | —      | ABORT: Candle direction ≠ signal             |
| 5  | 15:00:09.152 | 17576 | BUY  | OK       | OK     | **BLOCK**: OnlyOnePosition activo            |
| 6  | 15:00:52.410 | 17582 | SELL | **FAIL** | —      | ABORT: Conf#1 failed                         |
| 7  | 15:01:08.678 | 17586 | BUY  | OK       | OK     | **BLOCK**: OnlyOnePosition activo            |
| 8  | 15:01:31.080 | 17593 | SELL | **FAIL** | —      | ABORT: Conf#1 failed                         |
| 9  | 15:01:46.883 | 17597 | BUY  | OK       | OK     | **BLOCK**: OnlyOnePosition activo            |
| 10 | 15:01:55.588 | 17601 | SELL | OK       | OK     | **BLOCK**: OnlyOnePosition activo            |
| 11 | 15:01:57.084 | 17602 | BUY  | OK       | OK     | **BLOCK**: OnlyOnePosition activo            |
| 12 | 15:02:13.083 | 17603 | SELL | —        | —      | ABORT: Candle direction ≠ signal             |
| 13 | 15:02:24.883 | 17605 | SELL | **FAIL** | —      | ABORT: Conf#1 failed                         |
| 14 | 15:02:54.883 | 17608 | BUY  | —        | —      | ABORT: Candle direction ≠ signal             |
| 15 | 15:04:22.698 | 17620 | BUY  | **FAIL** | —      | ABORT: Conf#1 failed                         |
| 16 | 15:04:30.397 | 17621 | SELL | OK       | OK     | **BLOCK**: OnlyOnePosition activo            |

**Conclusión operativa del A:** hubo 7 oportunidades “vivas” (Conf#1 OK & Conf#2 OK); **6 se perdieron** por **candado no liberado**; 1 se ejecutó correctamente y cerró por TP.

---

# Diagnóstico del bug del candado (root-cause)

* El **estado `_tradeActive`** no se **resetea** tras quedar **flat sin órdenes vivas**.
* En tu código hay tres rutas para liberar el candado (watchdog de “flat”, `OnOrderChanged` y `OnPositionChanged`) con logs del tipo **“Trade lock RELEASED …”**. **En los logs no aparece ninguno**, así que **esas rutas no están disparando** la liberación en la práctica.
* En el cierre del trade, vemos secuencia correcta: **TP fill** → **SL cancel** → net=0 detectado… pero **no** llega el log de liberación, y **más tarde** el guard imprime `active=True net=0 activeOrders=0 cooldown=NO`.
* Esto indica **condición de salida no satisfecha** en tiempo real (p.ej., el `!HasAnyActiveOrders()` aún era `false` en el último `OnOrderChanged`, y **no existe** una **comprobación posterior** en el **heartbeat**/`OnCalculate` que “repesque” la liberación cuando ya es seguro.

---

# Corrección concreta (sin esperar re-corridas)

1. **Failsafe en `OnCalculate` (heartbeat)**

   * Al final de `OnCalculate`, añade una verificación explícita:

     * Si **`_tradeActive`** y **`GetNetPosition()==0`** y **`!HasAnyActiveOrders()`** y **(anti-flat cumplido: tiempo y barras)**
       → **`_tradeActive=false`**, limpia `_bracketsPlaced`, `_orderFills`, `_cachedNetPosition`, resetea `_antiFlatUntilBar`, y **log**:
       `Trade lock RELEASED by heartbeat (flat & no active orders)`.
   * Esto cubre **cualquier** caso en que los eventos no suelten el candado.

2. **Garantizar liberación en `OnOrderChanged`**

   * Tras cancelar el SL por TP (o viceversa), si **`!HasAnyActiveOrders()`** justo después de la cancelación, **libera el candado** y emite el log “by OnOrderChanged”.
   * Si aún queda `liveCount > 0`, **programa un check diferido** (flag) para volver a testear en el **siguiente `OnCalculate`**.

3. **Trazas explícitas del estado del candado**

   * Loggea **cada transición** de `_tradeActive` (→true al enviar entrada; →false al liberar) con **motivo** y **net/activeOrders** capturados.
   * Añade un **ping cada X segundos** (heartbeat) con: `_tradeActive`, `net`, `activeOrders`, `antiFlat window`, `cooldown`.

Con estos tres puntos, **desaparecen los falsos negativos** por bloqueo.

---

# Sobre las confluencias

* **Conf#1 (GL slope)**:

  * Para señales BUY, falla cuando el **trend=DOWN** (correcto).
  * Para señales SELL, **trend=DOWN → OK** (coherente con la lógica).
  * Tus logs ya imprimen los valores `gN/gN+1` y el `trend`, así que el criterio es sólido.

* **Conf#2 (EMA8 vs Wilder – Window)**:

  * En este tramo resultó **siempre OK** (7/7). No es bug; simplemente el mercado estaba dentro del “window” que defines (con tolerancia y igualdad permitida).

* **Abortos por dirección de vela en N**:

  * La validación “candle direction at N ≠ signal” se está aplicando correctamente y explica 3 abortos.

---

# Qué haría en la próxima corrida A

1. Aplicar el **failsafe** descrito.
2. Mantener `OnlyOnePosition=ON`, **cooldown=2** y **Enable flat watchdog=ON**.
3. Verificar que, tras cerrar el primer trade, aparece **uno de estos logs**:

   * `Trade lock RELEASED by OnOrderChanged ...` **o**
   * `Trade lock RELEASED by watchdog ...` **o**
   * `Trade lock RELEASED by heartbeat ...` (nuevo).
4. Confirmar que las siguientes señales con **Conf#1 OK & Conf#2 OK** **ya entran** (sin BLOCK).

---

## Cómo aplicar y validar
>**checklist y greps - A** 
>para validar automáticamente que el candado se libera y >que no hay más falsos negativos en el A

Repite **Escenario A** con tu misma configuración 
  (OnlyOnePosition=ON, cooldown=2, watchdog=ON).
  3. Greps mínimos para comprobar que ya no se “queda bloqueado”:

     * `grep -n "Trade lock RELEASED" ATAS_SESSION_LOG_A_results.txt`     
     * `grep -n "GUARD OnlyOnePosition" ATAS_SESSION_LOG_A_results.txt`   
     * `grep -n "RETRY NEXT TICK" ATAS_SESSION_LOG_A_results.txt`

  Debes ver al menos uno de:

  * `Trade lock RELEASED by OnOrderChanged (final)`
  * `Trade lock RELEASED by heartbeat (flat & no active orders)`
  * *(y/o el existente)* `Trade lock RELEASED by watchdog ...`

  Si aparecen y, después, ves **`GUARD ... -> PASS`** en las siguientes   
  señales con `CONF#1 OK & CONF#2 OK`, el problema de **falsos negativos  por candado** queda resuelto.&#x20;

