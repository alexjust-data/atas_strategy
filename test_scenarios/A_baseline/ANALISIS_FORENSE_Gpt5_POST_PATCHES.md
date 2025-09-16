Listo. Te dejo el forense de la **nueva sesión (A – baseline, post-parches)** con cifras, pruebas de log y veredicto.

# Resumen empírico

* **Señales capturadas (CAPTURE N=...)**: 15.&#x20;
* **Entradas a mercado ejecutadas**: **6** (todas con *GUARD → PASS*, *BRACKETS ATTACHED* y *Trade lock RELEASED*).&#x20;
* **Falsos negativos** (ambas confluencias OK pero sin entrada): **0**. Todas las “CONF#1 OK + CONF#2 OK” acabaron en **entry**.&#x20;
* **Falsos positivos** (entrada sin ambas confluencias OK): **0**. Todas las entradas tienen **CONF#1 OK** y **CONF#2 OK** justo antes del *MARKET ORDER SENT*.&#x20;
* **Abortos correctos por validaciones**: **9**

  * **Conf#1 FAIL**: 6 abortos → “ABORT ENTRY: Conf#1 failed”.&#x20;
  * **Dirección vela N no coincide**: 3 abortos → “Candle direction at N does not match signal”.&#x20;
  * **Conf#2 FAIL**: 0 (no aparece ningún “ABORT ENTRY: Conf#2 failed”).&#x20;
* **Guardia OnlyOnePosition**: **0** bloqueos; todos los casos relevantes muestran **“→ PASS”**. (El bug del candado quedó resuelto).&#x20;

# Evidencia (extracto con líneas clave)

## Las 6 entradas (con prueba de guardia, brackets y release)

1. **SELL** – N+1 bar=17527

   * GUARD: PASS (l. **83698**) → MARKET ORDER SENT (l. **83700**) → BRACKETS ATTACHED (l. **83713**) → RELEASED (l. **83877**).&#x20;
2. **BUY** – N+1 bar=17577

   * PASS (**86218**) → MARKET (**86220**) → BRACKETS (**86233**) → RELEASED (**86338**).&#x20;
3. **BUY** – N+1 bar=17587

   * PASS (**86809**) → MARKET (**86811**) → BRACKETS (**86824**) → RELEASED (**86859**).&#x20;
4. **BUY** – N+1 bar=17598

   * PASS (**87420**) → MARKET (**87422**) → BRACKETS (**87437**) → RELEASED (**87515**).&#x20;
5. **BUY** – N+1 bar=17603

   * PASS (**87953**) → MARKET (**87955**) → BRACKETS (**87970**) → RELEASED (**89026**).&#x20;
6. **SELL** – N+1 bar=17622

   * PASS (**91179**) → MARKET (**91181**) → BRACKETS (**91196**) → RELEASED (**91358**).&#x20;

> Nota: El *release* del caso 5 cae varias líneas después (no dentro del bloque de esa captura), pero aparece correctamente **\[17:45:21.292]**.&#x20;

## Abortos correctos (9/15 capturas)

* **Conf#1 FAIL (pendiente GL)**: 6 ocurrencias con “ABORT ENTRY: Conf#1 failed”. (p. ej., alrededor de 17:43:41 / 17:44:14 / 17:45:31…).&#x20;
* **Dirección vela N ≠ señal**: 3 ocurrencias con “ABORT ENTRY: Candle direction at N does not match signal”. (p. ej., \~17:44:15).&#x20;

# Confluencias (consistencia)

* **CONF#1 — GenialLine slope @ N+1**:

  * Pasa en las 6 entradas (BUY con trend=UP / SELL con trend=DOWN).
  * Falla en 6 capturas (rechazos esperados) cuando la pendiente contradice la dirección de la señal. **Correcto**.&#x20;
* **CONF#2 — EMA8 vs Wilder (Window, tolPre=1, equality=ON)**:

  * **0 fallos** en toda la sesión; siempre coherente con la ventana/tolerancia configuradas. **Correcto**.&#x20;

# Guardia y parches (verificación)

* Aparecen múltiples **“GUARD OnlyOnePosition … → PASS”** y **ningún** “BLOCK” tras quedar plano: el **candado ya no se queda enganchado**. Además, cada entrada muestra **“BRACKETS ATTACHED”** y un **“Trade lock RELEASED …”** posterior, cumpliendo la secuencia que añadimos por *OnOrderChanged/heartbeat*.

# Cifras finales (A – post-parches)

* **CAPTUREs**: 15
* **Entradas ejecutadas**: **6 / 6** señales **válidas** (CONF#1 OK + CONF#2 OK) → **100% de ejecución**, **0 falsos negativos**.&#x20;
* **Falsos positivos**: **0** (ninguna entrada sin ambas confluencias OK).&#x20;
* **Rechazos correctos**: **9** (6 por CONF#1 FAIL, 3 por dirección de vela N).&#x20;

# Conclusión

* **El fix del candado funciona**: no hay bloqueos; todas las señales válidas se ejecutan y liberan bracket/candado correctamente.&#x20;
* **La estrategia (A) se comporta exactamente según especificación**:
  `CAPTURE → CONF#1 + CONF#2 → GUARD PASS → MARKET → BRACKETS → RELEASE`.&#x20;
* **Siguiente paso**: repetir la misma auditoría en **B–F** (con tus toggles) para aislar cada confluencia/tiempos; ya tienes los *greps* listos en *strategy.md / cheatsheet*.

