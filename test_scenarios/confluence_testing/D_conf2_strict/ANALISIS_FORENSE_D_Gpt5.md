Vamos con el forense de **C) Solo Conf#2 (EMA8 vs Wilder – Window)**.
(Setup esperado: GL slope **OFF**, EMA vs Wilder **ON**, *Window*, *count equality* **ON**, *bracket reconciliation* **OFF**. Así está definido en tu plantilla de escenario C).&#x20;

# Resultado empírico (Sesión C)

| Métrica                                    | Conteo |
| ------------------------------------------ | -----: |
| Señales capturadas (`CAPTURE: N=`)         | **15** |
| Checks de **Conf#2** ejecutados            | **12** |
| — **Conf#2 → OK**                          | **10** |
| — **Conf#2 → FAIL**                        |  **2** |
| Aborts por dirección vela **N** (mismatch) |  **3** |
| Guardia **OnlyOnePosition → PASS**         |  **6** |
| Guardia **OnlyOnePosition → BLOCK**        |  **4** |
| **Órdenes a mercado** enviadas             |  **6** |
| **Brackets adjuntados** post-fill          |  **6** |
| `Trade lock RELEASED` tras cierre          |  **6** |

Todo lo anterior está en el TXT de C (líneas con `CONF#2 ... -> OK/FAIL`, `MARKET ORDER SENT`, `BRACKETS ATTACHED`, `Trade lock RELEASED`, `GUARD ... PASS/BLOCK`, y abortos por vela N).

---

## Evidencia puntual (muestras)

**Entradas correctas (Conf#2 OK → GUARD PASS → MARKET → BRACKETS → RELEASE):**

* 18:27:12 — `CONF#2 … SELL -> OK` → `GUARD … -> PASS` → `MARKET ORDER SENT: SELL 1` → `BRACKETS ATTACHED` → `Trade lock RELEASED`.&#x20;
* 18:28:51 — `CONF#2 … BUY -> OK` → `GUARD … -> PASS` → `MARKET ORDER SENT: BUY 1` → `BRACKETS ATTACHED` → `Trade lock RELEASED`.&#x20;

**Abortos correctos (sin check de Conf#2 por filtro de vela N):**

* 18:28:26, 18:28:55, 18:29:09 — `ABORT ENTRY: Candle direction at N does not match signal`.&#x20;

**Conf#2 FAIL (con abort):**

* 18:27:43 y 18:28:43 — `CONF#2 … -> FAIL` seguido de `ABORT ENTRY: Conf#2 failed`.&#x20;

**Guardia (bloqueos esperados):**

* 18:28:37 y 18:28:49 — `GUARD OnlyOnePosition … -> BLOCK` (con cooldown activo).&#x20;

> Nota: En esta sesión **no hay líneas de `CONF#1`**, confirmando que GL slope estaba **OFF** como pide el escenario C.&#x20;

---

## Clasificación por captura (15 señales)

|  # |     N | Side | Conf#2 |   Guard   | Decisión                |
| -: | ----: | :--: | :----: | :-------: | :---------------------- |
|  1 | 17526 | SELL |   OK   |    PASS   | **ENTRY SELL**          |
|  2 | 17545 |  BUY |  FAIL  |     –     | **ABORT Conf#2 failed** |
|  3 | 17572 |  BUY |    —   |     –     | **ABORT vela N**        |
|  4 | 17576 |  BUY |   OK   |    PASS   | **ENTRY BUY**           |
|  5 | 17582 | SELL |   OK   |    PASS   | **ENTRY SELL**          |
|  6 | 17586 |  BUY |   OK   | **BLOCK** | **BLOCK guard**         |
|  7 | 17593 | SELL |  FAIL  |     –     | **ABORT Conf#2 failed** |
|  8 | 17597 |  BUY |   OK   |    PASS   | **ENTRY BUY**           |
|  9 | 17601 | SELL |   OK   | **BLOCK** | **BLOCK guard**         |
| 10 | 17602 |  BUY |   OK   |    PASS   | **ENTRY BUY**           |
| 11 | 17603 | SELL |    —   |     –     | **ABORT vela N**        |
| 12 | 17605 | SELL |   OK   | **BLOCK** | **BLOCK guard**         |
| 13 | 17608 |  BUY |    —   |     –     | **ABORT vela N**        |
| 14 | 17620 |  BUY |   OK   |    PASS   | **ENTRY BUY**           |
| 15 | 17621 | SELL |   OK   | **BLOCK** | **BLOCK guard**         |

(Consolida las 6 entradas y los 9 no-fills por **FAIL/vela/guard**). Todo rastreable en el log C.&#x20;

---

## Falsos positivos / falsos negativos (definiciones de C)

* **FP (falso positivo)**: se abre mercado **sin** `CONF#2 -> OK`.
* **FN (falso negativo)**: `CONF#2 -> OK` **y** `GUARD -> PASS` pero **no** se envía la market.

**Hallazgo:**
**FP = 0** (todas las markets van precedidas de `CONF#2 -> OK` + `GUARD -> PASS`). **FN = 0** (cada `OK` con `PASS` terminó en market). Las 4 señales con `CONF#2 -> OK` pero **guard BLOCK** no cuentan como FN: están correctamente bloqueadas por la guardia (*OnlyOnePosition*).&#x20;

---

## Observación importante de configuración

En los logs de Conf#2 se registra **`mode=Window tolPre=1,00000`** (tolerancia efectiva **1**), **no 4**. Si pretendías correr C con *pre-cross tolerance = 4*, la sesión C se ejecutó con **1**. Esto hace el filtro **más estricto** de lo planificado (aun así, 10/12 pasaron). Revisa el ajuste en la UI antes del siguiente run.&#x20;

---

## Veredicto (C)

* La estrategia en **“Sólo Conf#2”** **cumple la especificación**: ejecuta cuando `CONF#2 OK` y la guardia lo permite; aborta correctamente en `Conf#2 FAIL` o **vela N** inconsistente; adjunta **brackets** post-fill y libera el **trade lock** al cierre.
* **0 FP / 0 FN** bajo las reglas de C.
* **Guardia** opera como debe (4 bloqueos bien justificados).&#x20;


