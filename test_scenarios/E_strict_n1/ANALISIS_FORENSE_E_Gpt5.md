# Resumen forense (E)

* **CAPTURE (señales N capturadas):** 4
  (N=17525 BUY, N=17526 SELL, N=17545 BUY, N=17572 BUY).&#x20;
* **Procesamientos @N+1:** 3
  (17526→17527, 17525→17526, 17545→17546).
* **Órdenes a mercado enviadas:** 1 (SELL en 17527).&#x20;
* **PENDING ARMED:** 0. (No aparece en el log).&#x20;
* **PENDING EXPIRED:** 0. (No aparece en el log).&#x20;
* **First-tick missed (pero dentro de tolerancia=0):** 3 (igualdad exacta al open de N+1).
* **ABORT ENTRY por Conf#1:** 2 (las otras dos procesadas abortan por GL slope).&#x20;
* **ABORT por Conf#2 / CandleDir / Guard:** 0.&#x20;
* **IGNORES por “close did not confirm” (antiflip-flop):** numerosas (ruido intrabar esperado).&#x20;

## Evidencia puntual de los 3 @N+1

1. **N=17525 → 17526 (BUY) → ABORT Conf#1**
   `PROCESSING PENDING @N+1: bar=17526` → `First-tick missed but within tolerance (19899,00~19899,00)` → `CONF#1 ... trend=DOWN -> FAIL` → `ABORT ENTRY: Conf#1 failed`.&#x20;

2. **N=17526 → 17527 (SELL) → ORDEN EJECUTADA** ✅
   `PROCESSING PENDING @N+1: bar=17527` → `First-tick missed but within tolerance (19893,50~19893,50)` → `CONF#1 ... trend=DOWN -> OK` + `CONF#2 ... diff=-0,72268 ... -> OK` → `MARKET ORDER SENT: SELL 1 at N+1 (bar=17527)`.&#x20;

3. **N=17545 → 17546 (BUY) → ABORT Conf#1**
   `PROCESSING PENDING @N+1: bar=17546` → `First-tick missed but within tolerance (19882,75~19882,75)` → `CONF#1 ... trend=DOWN -> FAIL` → `ABORT ENTRY: Conf#1 failed`.&#x20;

## Falsos positivos / falsos negativos

* **Falsos positivos (FP): 0.** La única orden enviada tenía Conf#1=OK y Conf#2=OK.&#x20;
* **Falsos negativos (FN): 0.** No hay ningún caso con **ambas** confluencias OK y entrada abortada por timing (no hubo `PENDING EXPIRED`) ni por guardia.

## Conclusiones sobre la estrategia (E)

* El **timing Strict N+1** funciona tal y como está codificado: si se pierde el primer tick, **solo continúa** si el precio actual == open de N+1 (con tol=0). Los tres “first-tick missed” que ves son exactamente ese caso de igualdad, por eso “proceed”. (Ver lógica en código: chequeo de `IsFirstTickOf()`, tolerancia y mensajes “First-tick missed…” / “EXPIRE…”).&#x20;
* La **confluencia Conf#1 (pendiente de GenialLine)** actuó correctamente: dos abortos (BUY contra pendiente bajista) y un OK en el SELL, coherente con los valores `gN/gN1` y su signo de pendiente.
* La **confluencia Conf#2 (EMA8 vs Wilder, window)** validó cuando debía en el trade ejecutado; no aparece como causa de rechazo en esta sesión.&#x20;
* Los muchos **“IGNORE signal at N (close did not confirm)”** son el filtro antiflip-flop intrabar esperado: evita capturas cuando el cruce se pinta pero el **close** no lo confirma. No afecta a la lógica N→N+1 salvo que impide “CAPTURE” si no hay confirmación real.&#x20;

¿Algo que ajustar? Si este patrón de “casi cruces” te parece excesivo (muchos IGNORE), podríamos **elevar ligeramente la histéresis o el alisado** del GenialLine para reducir ruido intrabar sin tocar la semántica de E. Pero, en cuanto a **timing y confluencias**, la sesión E está **consistente** con lo especificado y con tus cifras.
