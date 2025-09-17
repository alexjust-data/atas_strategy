¡Vamos a los hechos 👇!

# Resultado forense de la sesión (con logs)

**Qué conté en tu `ATAS_SESSION_LOG.txt`:**

* **Señales (CAPTURE):** 16
* **CONF#1 OK / FAIL:** 6 OK, 6 FAIL
* **CONF#2 OK / FAIL:** 5 OK, 1 FAIL (solo se evalúa cuando #1=OK)
* **Ambas OK (candidatas reales):** 5
* **De esas 5:**

  * **GUARD PASS:** 3 → **entradas enviadas** (3)
  * **GUARD BLOCK:** 2 → **no se entra**
* **Entradas enviadas:** 3 (“MARKET ORDER SENT”)
* **TPs llenados:** 5 (varias patas)
* **Triggers de Breakeven:** **0** (ningún “BRK / BREAKEVEN”)

Evidencias en el log (muestras):

* Señales “CAPTURE” y confluencias evaluadas.&#x20;
* Guard PASS/BLOCK durante esas señales.&#x20;
* Órdenes de mercado enviadas.&#x20;
* TP llenados (patas TP1/TP2/TP3).&#x20;

**Conclusión numérica sobre confluencias:**

* **Falsos positivos:** 0 (todas las entradas ocurren solo cuando **CONF#1=OK & CONF#2=OK & GUARD=PASS**).
* **Falsos negativos “puros” (ambas OK + GUARD PASS pero sin entrada):** 0.
* **Menos entradas que antes:** se explica por (a) más **FAIL** en CONF#1 en esta sesión, y (b) **dos bloqueos** por el guard (OnlyOnePosition/cooldown) **cuando las confluencias sí estaban bien**.&#x20;

---

# Por qué el **breakeven no se movió**

Aunque hubo **fills de TP** (TP1/TP2/TP3) **no hay ni un solo** log de **BRK / BREAKEVEN** → el motor de BE **no ejecutó**. En tus sesiones previas ya vimos el patrón:

> “TRIGGER by TP1 fill …” → seguido de “SKIP BE: unknown entryPrice”

Eso ocurre porque en **órdenes de mercado** el `order.Price` suele venir **0.00**, y si tu lógica depende de `order.Price > 0` para poblar `_lastEntryPrice`, **nunca guardas el precio de entrada**. El trigger detecta el TP fill, pero al no tener `_lastEntryPrice` válido, **aborta el movimiento a BE**.

En **esta** sesión:

* Hay **TP fills** (lo vemos) pero **no hay** logs de **ENTRY FILL** ni de **BRK** ⇒ **no se está capturando el precio de entrada** (o la rama de BE no corre).&#x20;

**Qué hacer (sin tocar todavía lógica de confluencias):**

1. **Capturar el fill real de mercado** con un helper tipo `GetEffectiveFillPrice(order)` que pruebe, en este orden: `AvgFillPrice` / `ExecutionPrice` / `LastFillPrice` / (fallback) `Price`. Guardar **siempre** `_lastEntryPrice` al **primer fill** de la entrada.
2. **Disparar BE en `OnOrderChanged`** cuando detectes **cualquier TP Filled** de ese OCO/entrada:

   * Mover **todas** las SL activas del mismo OCO a **BE + offset** (según `BreakevenOffsetTicks`).
   * Log explícito: `468/BRK MOVE -> newSL=... (from TPx fill ...) reordered=k`.
3. **Redundancia (failsafe):** si por lo que sea no prendió `_lastEntryPrice`, añadir un **watchdog 1–2s** que, si hay TPs llenos y queda SL activo, intente resolver el **precio de entrada** a partir de fills cacheados y mover el SL.

---

# “¿Cómo pudo abrir 28 contratos con SL de 27 ticks?”

En la sesión que analizamos, el cálculo fue:

* `tickValue=0.50 USD/tick`, `SL=7 ticks` ⇒ **riskPerContract=3.50 USD**
* `RiskPerTradeUSD=100` ⇒ **qty=floor(100 / 3.5) = 28**
  Esto está **en el log de cálculo** y en el de **ENTRY qty source=AUTO**.&#x20;

Si viste “Stop: 27” en la UI, estás mirando otra métrica (p. ej., ticks gráficos de otra referencia o un rango visual) distinta del **SLOffset** que el motor de riesgo usa realmente (7). El propio log de brackets deja constancia del SL real enviado: `SL=..., risk=3,25~3,50`. Revisa que **SLOffset**/`UseSLFromSignalCandle` de la estrategia coincida con lo que ves en el panel.

---

# Por qué ahora “entra menos” aun con misma configuración

1. **Más CONF#1=FAIL** en esta sesión (6 de 12 evaluaciones de CONF#1). Eso ya reduce oportunidades.&#x20;
2. **Guard bloqueó 2** de las 5 ocasiones con ambas confluencias OK (una por **net>0 + activeOrders**, otra por **cooldown**). Antes, con qty manual bajo, es probable que no coincidiera ese escenario.&#x20;
3. **Strict N+1** y **OpenToleranceTicks** pueden hacer que algún “timing” quede fuera; en los logs se ve el procesamiento N+1 y tolerancias.&#x20;

> En los datos de esta sesión **no hay** casos de “ambas OK + GUARD PASS” que no acabaran en **ENTRY**. Es decir, **no hubo falsos negativos** de ejecución atribuibles al RM.

---

# Qué hacer ahora (secuencial, seguro, y sin romper lo que ya está)

1. **Arreglar BE (crítico y acotado):**

   * Añadir `GetEffectiveFillPrice(order)` y **poblar `_lastEntryPrice`** al primer fill de entrada.
   * En `OnOrderChanged`, al detectar `468TP:* status=Filled`, **mover** todos los SL activos del mismo **OCO** a **BE + offset**.
   * Añadir `468/BRK` logs detallados y un **tiny watchdog** (1–2s) que reintente si faltó el precio o si un SL quedó atrás.

2. **Aislar el impacto del RM sobre la frecuencia de entradas**:

   * Repite un **replay A/B** con la **misma sesión**:

     * **A:** `EnableRiskManagement=ON`, **pero** `UseAutoQuantityForLiveOrders=OFF` (qty manual=3)
     * **B:** lo mismo **pero ON**.
   * La diferencia de **#entradas** entre A y B te dirá cuánto aporta el **guard por tamaño grande**. (En tus logs ya vimos 2 bloqueos por guard; con qty=3 probablemente no ocurren).&#x20;

3. **Si quieres “modo permisivo” para validar confluencias**:

   * `Strict N+1 open`: **OFF** (temporalmente, solo para validar señales).
   * `OpenToleranceTicks`: 4–6.
   * `OnlyOnePosition`: **OFF** o cooldown=0, para ver cuántas señales **habría** tomado.
   * **RM**: ON pero `UseAutoQuantityForLiveOrders=OFF` (para evitar que el tamaño genere bloqueos colaterales).
   * `Skip trade if underfunded`: OFF, `MinQtyIfUnderfunded=1`.
   * Con esto mides solo **confluencias**.

4. **Validación breve de BE tras el fix**:

   * Un replay corto con **TP1 cerca**, `BreakevenMode=OnTPFill` y `Trigger on TP1 touch/fill=ON`.
   * Espera ver:

     * `468/ORD: ... 468TP:... status=Filled` (evidencia del TP) **y acto seguido**
     * `468/BRK MOVE ...` con la lista de SL actualizados.&#x20;

---

## Resumen

* **Confluencias:** números cuadran; **no** hay falsos negativos de ejecución (ambas OK + guard pass siempre acabó en ENTRY). Menos entradas por **FAILs** de CONF#1 en esta sesión y por **bloqueos del guard** con tamaño grande.&#x20;
* **Breakeven:** el problema **no es** el etiquetado TP; **sí es** que **no guardas el entry price** en market orders (price==0). Solución: **helper de fill real** + mover SLs al TP fill.
* **Cantidad 28 con SL “27”:** en esta sesión el RM usó **7 ticks** (3.5 USD/contrato) → 28 contratos por riesgo 100 USD (correcto). El “27” que viste es otra métrica visual.

Si quieres, en el siguiente paso te doy el **diff mínimo** para: (1) capturar el **fill real** y (2) **mover SLs** al BE tras TP1/2/3, con logs “468/BRK” muy claros, sin tocar nada de las confluencias.
