

# 1) Objetivo de la estrategia

* **Señal base (obligatoria):** cierre de la vela **N** cruza la **Genial Line**.
* **Ejecución:** en la **vela N+1**.
* **Confluencias (opcionales):**

  * **CONF#1 – Pendiente de Genial Line a favor de la señal**: BUY exige GL **subiendo**; SELL exige GL **bajando**.
  * **CONF#2 – EMA8 vs Wilder8**: BUY ⇒ EMA8 ≥ W8 (con tolerancia); SELL ⇒ EMA8 ≤ W8 (con tolerancia).
* **Brackets:** SL bajo/encima de la vela de señal (según setting). TP1/TP2/TP3 = R1/R2/R3 desde **Open(N+1)**.

---

# 2) Fallos detectados y correcciones (cronológico)

## A. Confluencias y señal

* **(A1) Igualdad EMA8=Wilder8 → bloqueaba entradas**
  *Síntoma:* `CONF#2 ... -> FAIL` incluso cuando iban iguales.
  *Fix:* permitir igualdad o mejor **tolerancia** de 1–2 ticks (recomendado).
* **(A2) Pendiente GL mal interpretada/duplicada en logs**
  *Síntoma:* mensajes “raros” (a veces lectura de **precio** en vez de la **serie**).
  *Fix:* unificar el cálculo de pendiente en una sola función y **eliminar el log duplicado** que mezclaba fuentes.
* **(A3) Debate > vs >= para N+1**
  *Hallazgo:* `bar > pendingBar` ya fuerza N+1; el problema real era **diagnóstico confuso** en el mismo tick de captura.
  *Mejora:* ventana explícita **N+1** y logs claros (ARMED → EXEC\@N+1 → EXPIRED).

## B. Ejecución N+1 (riesgo)

* **(B1) Ventana exacta N+1**
  *Añadido:* `execBar = N+1` con tres caminos:
  `bar < execBar` → **ARMED** (espera),
  `bar == execBar` → **EJECUTA**,
  `bar > execBar` → **EXPIRED** (descarta señal tardía).
* **(B2) Apertura estricta con tolerancia**
  *Opción de riesgo:* `StrictN1Open = true` + **OpenToleranceTicks (1–2)**.
  Si te pierdes el **primer tick** y el precio ya se desvió más que la tolerancia → **expira**.

## C. Anti-dobles entradas / solapes

* **(C1) OnlyOnePosition (triple guardia)**
  Bloquea si: (a) `net!=0`, (b) hay **órdenes activas** de la estrategia, (c) `_tradeActive=true`.
* **(C2) Zombies**
  Si `net=0` pero hay órdenes activas, **cancelar** y **salir** del ciclo (no re-entrar en el mismo tick).
* **(C3) Cooldown opcional (N velas)**
  Tras quedar plano, **enfriamiento** de 1–2 velas antes de aceptar otra señal (evita flip-flop).

## D. OCO y brackets (corazón del problema práctico)

* **(D1) Pre-fill vs post-fill**
  *Problema original:* crear TP/SL **antes** de saber cuántos contratos entraron ⇒ desalineación (ej: net=1 pero 3 TPs/SL).
  *Fix crítico:* **colgar brackets post-fill** en `OnOrderChanged` (o en `OnPositionChanged` como fallback) usando el **net real**.
* **(D2) Número de patas = posición real**
  Si net=1 ⇒ sólo **1 TP + 1 SL**; net=2 ⇒ **TP1+TP2 + sus SL**; net=3 ⇒ **TP1+TP2+TP3 + sus SL**.
  (Cada par TP/SL con su **OCO** propio).
* **(D3) Reconciliación continua**
  En cada `OnOrderChanged`:

  * Si hay **más TPs que net** → cancelar sobrantes.
  * Si la **suma de SLs ≠ net** → cancelar y crear **1 SL** con qty=net.
* **(D4) Fallbacks de net cuando el portfolio llega tarde**
  Si `GetNetPosition()` devuelve 0, leer de la **orden**: `Filled/FilledQuantity/Executed/QtyFilled`.
  Si `status==Filled` y aún 0 → usar `QuantityToFill`. (Cubre **fills parciales** y **completos**).
* **(D5) “Secuencia mortal” y solución final**
  *Síntoma en logs:* `BRACKETS ATTACHED` → **GetNetPosition=0** de inmediato → candado liberado → con `AutoCancel=true`, **ATAS cancela** todo.
  *Fix definitivo:*

  * **Desactivar `AutoCancel`** en TP/SL (lo cancelamos nosotros).
  * **Anti-flat window** (p.ej. 400 ms) justo tras colgar brackets: si detectas `net==0` dentro de esa ventana, **no** liberas ni cancelas; asumes glitch transitorio.
  * Pasada la ventana: si de verdad `net==0`, cancelas hijos y **liberas candado** (limpieza controlada).

---

# 3) Parámetros recomendados (por defecto)

* **StrictN1Open = true**
* **OpenToleranceTicks = 1–2**
* **RequireGenialSlope = ON** (si buscas máxima calidad de señal)
* **RequireEmaVsWilder = ON** con **tolerancia 1–2 ticks**
* **OnlyOnePosition = ON**
* **EnableCooldown = ON**, **CooldownBars = 2**
* **AntiFlatMs = 400**
* **AutoCancel (TP/SL) = false** *(los cancelamos manualmente al confirmar plano)*
* **Brackets post-fill = ON** (adjuntar según **net** real)
* **Reconciliación continua = ON**

---

# 4) Qué ver en los logs (chuleta)

* **Señal:**
  `CAPTURE: N=... BUY/SELL uid=...`
* **Ventana N+1:**
  `PENDING ARMED: now=..., execBar=...`
  `PROCESSING PENDING @N+1: bar=...`
  `PENDING EXPIRED: missed N+1`
* **Confluencias:**
  `CONF#1 (GL slope) trend=UP/DOWN/FLAT -> OK/FAIL`
  `CONF#2 (EMA8 vs W8 @N+1) e8=..., w8=..., tol=... -> OK/FAIL`
* **Guardias:**
  `ABORT ENTRY: OnlyOnePosition guard is active`
  `ZOMBIE CANCEL ... will re-check on next tick/bar`
* **Entrada:**
  `MARKET submitted: BUY/SELL ...`
  `OnOrderChanged: 468ENTRY ... Placed/PartlyFilled/Filled`
* **Brackets:**
  `BRACKETS ATTACHED (from net=...)` (OnOrderChanged/OnPositionChanged)
  `ReconcileBracketsWithNet ... cancel ... / recreate SL ...`
* **Anti-flat:**
  `ANTI-FLAT: net=0 detected but within window → suppress release`
  `Trade lock RELEASED (flat confirmed & no active orders)`

---

# 5) Cómo interpretar los triángulos del gráfico

* **Triángulo azul:** entrada ejecutada (BUY/SELL).
* **Triángulo rojo:** TP ejecutado (salida parcial).
  Si ves “triángulo rojo” y el panel indica **net=1** pero hay **2** límites y un **stop de 2**, es el viejo síntoma de **brackets creados antes** del fill real o de **no reconciliar**. Con los cambios, eso **se corrige solo** en el mismo evento.

---

# 6) Edge cases y comportamiento actual

* **Fill parcial de la market:**
  Se adjuntan brackets **según net**; reconciliación limpia cualquier sobrante.
* **Portfolio lento en actualizar net:**
  Fallback lee **FilledQuantity** de la orden; si aún así no hay net, usa `QuantityToFill` cuando el `status==Filled`.
* **Net=0 fantasma justo tras colgar brackets:**
  Anti-flat window evita liberar/cancelar; ATAS ya **no** puede barrer hijos porque `AutoCancel=false`.
* **Señal tardía (N+2+):**
  **Expira** (log `PENDING EXPIRED`); no hay “pendientes eternos”.
* **Solapes de entradas contrarias:**
  Zombies se cancelan y **no** re-entra en el mismo tick; hay **cooldown**.

---

# 7) Resultado final

* **Entradas** coherentes con la señal (N→N+1) y confluencias.
* **Gestión de riesgo**: apertura N+1 estricta con tolerancia, expiración, OnlyOnePosition, cooldown.
* **Brackets** robustos: se crean **post-fill**, se **reconcilian** siempre y **no desaparecen** por `AutoCancel` + net=0 fantasma.
* **Logs** que explican cada decisión (perfectos para auditar “qué pasó y por qué”).

---

> ---

---

# D5) “Secuencia mortal” — explicación con ejemplo práctico

## ¿Qué es?

Un **glitch temporal** justo después de colgar los brackets: `GetNetPosition()` devuelve **0** por unos milisegundos (el portfolio aún no “marcó” la posición), y como los TP/SL estaban con **AutoCancel=true**, **ATAS** cree que estás **plano** y **cancela** todos los brackets. Visualmente “desaparecen”.

## El patrón en los logs (lo que viste)

1. `00:48:22.159  BRACKETS ATTACHED (from net=3)` → **colgamos** TP/SL correctamente.
2. `00:48:22.159  GetNetPosition: returning 0` → **glitch**: el portfolio aún reporta **0**.
3. `00:48:22.160  Trade candado RELEASED: net=0 & no active orders` → el código **cree** que estás plano y libera.
4. `00:48:22.183-243 status=Placed` → los brackets se ven **colocados**.
5. `00:48:22.256-299 status=Cancelled` → con **AutoCancel=true**, **ATAS** los **cancela** al pensar que estás plano.

## El fix (lo que hicimos)

1. **AutoCancel=false** en TP/SL

   * ATAS ya **no** podrá barrer los brackets por su cuenta si detecta plano momentáneo.

2. **Anti-flat window** (p.ej. **400 ms**) tras colgar los brackets

   * Si `net==0` **dentro** de esa ventana → **NO** liberamos candado ni cancelamos nada; lo tratamos como **glitch transitorio**.
   * Normalmente, en esos milisegundos el portfolio se actualiza a net>0 y todo queda estable.

3. **Limpieza controlada pasado el window**

   * Si **tras** la ventana **sigue** `net==0` y no hay órdenes activas → **ahora sí** cancelamos nosotros los hijos (si queda alguno) y liberamos candado.
   * Esto cubre el caso real de quedarte plano (ej. TP final, cierre manual, cancelación total, etc.).

## Ejemplo paso a paso (con timestamps)

* `00:48:22.150` → entra market BUY 3.
* `00:48:22.159` → **BRACKETS ATTACHED (from net=3)** y guardamos `_bracketsAttachedAt=22.159`.
* `00:48:22.159` → `GetNetPosition()` responde **0** (glitch).
* **Antes**: se liberaba el candado y **ATAS** (AutoCancel=true) barría los TP/SL.
* **Ahora**:

  * Comprobamos `WithinAntiFlatWindow()` → **sí** (han pasado < 400 ms).
  * **NO** liberamos, **NO** cancelamos; esperamos.
* `00:48:22.260` → el portfolio ya refleja **net=3**.
* Los brackets **siguen vivos** (AutoCancel=false y no liberamos lock).
* Más tarde, cuando cierras todo:

  * Detectamos **net=0 fuera** de la ventana, **cancelamos** hijos si queda algo, y **liberamos** candado con logs:
    `FLAT CONFIRMED: cancelling remaining children…` → `Trade lock RELEASED (flat confirmed & no active orders)`.



