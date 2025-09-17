me gusta mucho cómo lo estás llevando: ya tienes la base estable (confluencias + guards) y los replays confirman que va fino. Para meter el Risk Management sin romper nada, yo lo haría con una **estrategia de capas + “feature flags”** y mini-checks después de cada capa. Te dejo el itinerario exacto, los archivos a crear y qué verificar en cada paso.

# Plan incremental (seguro y reversible)

## Capa 0 — Congelar baseline

* **Objetivo:** asegurar que todo lo actual sigue igual mientras añadimos piezas alrededor.
* **Acción:** añadir un flag global (propiedad en la estrategia) `EnableRiskManagement` (por defecto **OFF**) y un sub-flag `RiskDryRun` (por defecto **ON** cuando `EnableRiskManagement=ON`).
* **Evidencia a revisar:** los logs actuales (prefijo `468/STR`) siguen sin variación, no aparece ningún `468/RISK` salvo mensajes de init. Tu baseline de replays se mantiene tal cual (ya vimos que hoy estaba perfecto).&#x20;

Qué comprobar tras aplicar la Capa 0
* Build sin cambios de warnings/errores respecto a tu baseline.
* En el log de la sesión, verás solo una línea nueva al inicio:

```sh
468/RISK INIT flags EnableRiskManagement=False RiskDryRun=True effectiveDryRun=True
```
* No deben aparecer otras líneas 468/RISK o 468/CALC nuevas que no existieran ya (no hemos tocado nada más).

## Capa 1 — Detección robusta de símbolo + migraciones sin comportamiento

* **Objetivo:** resolver el “UNKNOWN” y preparar overrides por símbolo sin tocar la ejecución.
* **Acciones:**

  1. Introducir `GetEffectiveSecurityCode()` **cacheado** con esta prioridad:

     * `InstrumentInfo.Instrument` (API moderna) → `Security.Instrument` (legado) → `Security.Code` → `Instrument` → `UNKNOWN`.
     * Log en **RISK INIT**: `SYMBOL source=... value=...`.
  2. Migrar warnings obsoletos: usar `Security.TickSize` y, si no hay, `InstrumentInfo.TickSize`; como último recurso, tu helper de fallback (sin cambiar el cálculo de entradas).
* **Archivos:**

  * `src/MyAtas.Strategies/FourSixEightConfluencesStrategy_Simple.cs` (añadir helper + logs).
* **Evidencia:** en logs verás `468/RISK SYMBO L ...`. Con eso habilitamos overrides por símbolo más adelante. (Tu **map\_rute.md** ya lo anticipa como parte del árbol de UI y del flujo de risk, así que casamos con el plan maestro.)&#x20;

## Capa 2 — Motor de cálculo en “dry-run” (sin tocar Quantity real)

* **Objetivo:** ejecutar `CalculateQuantity()` en **OnCalculate** con **throttle** y solo loggear diagnósticos (no altera órdenes).
* **Acciones:**

  1. Llamar a `CalculateQuantity()` cada X barras/ticks (p.ej., hash de inputs y “cooldown” temporal para no spamear).
  2. Añadir **snapshot** al finalizar:
     `468/CALC SNAPSHOT [SYM] mode=... slTicks=... tickValue=... equity=... -> qty=...`
  3. Actualizar **propiedades diagnósticas** (read-only en la UI) cada vez: `EffectiveTickValue`, `EffectiveTickSize`, `EffectiveAccountEquity`, `LastAutoQty`, `LastRiskPerContract`, etc.
* **Archivos:**

  * `FourSixEightConfluencesStrategy_Simple.cs` (la función ya la tienes casi completa).
* **Evidencia:** en los logs de tu replay debe aparecer actividad `468/RISK`/`468/CALC` que **no estaba** (en el log que compartiste no sale ningún `468/RISK`/`468/CALC`, justo el gap que detectaste).&#x20;

## Capa 3 — CSV overrides y autovalores (tick value + equity), solo diagnóstico

* **Objetivo:** que **tick value** y **equity** efectivos salgan “correctos” en los logs sin afectar trades.
* **Acciones:**

  1. `ParseTickValueOverrides()` ya existe; conectarlo a `GetEffectiveTickValue()` con prioridad: **override por símbolo → autodetección → fallback** (sin cambiar Quantity real).
  2. Equity efectivo: `BalanceAvailable` si > 0; si no, `Balance` (>0); si nada, fallback.

     * **Muy importante:** corrige el patrón **nullable vs non-nullable**. El error de hoy (`decimal.HasValue/Value`) indica que en ese punto el tipo **no** es `decimal?`. Usa:

       * `var avail = Portfolio?.BalanceAvailable; if (avail.HasValue && avail.Value > 0) ...`
       * `else if (Portfolio?.Balance > 0) ...` (sin `.HasValue` ni `.Value` aquí).
  3. Log de RISK INIT con: símbolo, overrides aplicados, currency (`Security.QuoteCurrency`), tick size/tick value/equity.
* **Evidencia:** ver en replay:

  * `468/RISK TICK-VALUE override: ...` o `auto-detected: ...`
  * `468/RISK ACCOUNT ... detected ...`
  * Desaparece el error de compilación **CS1061** de `.HasValue/.Value` en `decimal` (el error te salía en línea \~1927; esta capa lo elimina).


## Capa 4 — Validación offline por escenarios (sin tocar trading)

* **Objetivo:** probar exhaustivamente el motor de cálculo con tus “replays” y escenarios.
* **Acciones:**

  1. Añadir script `tools/deploy_risk_management.ps1` para compilar + copiar solo la estrategia y forzar `EnableRiskManagement=ON` y `RiskDryRun=ON` (parámetros por línea de comandos o config local).
  2. Añadir README corto en `test_scenarios/risk_management/` explicando cómo comparar logs (`grep '468/CALC'`, etc.).
* **Archivos nuevos:**

  * `tools/deploy_risk_management.ps1`
  * `docs/RISK_ROLLOUT.md` (guía de activación por capas)
  * `docs/RISK_LOGGING.md` (prefijos, ejemplos, grep)
* **Evidencia:** dif entre logs de baseline y dry-run debe mostrar **solo** líneas `468/RISK`/`468/CALC`; ninguna línea `468/STR` cambia.&#x20;

## Capa 5 — “Soft-engage”: Quantity efectivo con **modo sombra**

* **Objetivo:** empezar a **usar** el `LastAutoQty` **solo cuando sea seguro**, manteniendo “escape hatch”.
* **Acciones:**

  1. Nuevo flag: `UseAutoQuantityForLiveOrders` (OFF por defecto).
  2. Cuando ON: en el punto donde hoy pasas `qty` al `SubmitMarket`, usa `autoQty` **solo si**:

     * `EnableRiskManagement=ON`, `RiskDryRun=OFF`, `autoQty>0`, `riskPerContract>0`, **y** `UnderfundedPolicy` lo permite (ver Capa 6).
  3. Log claro cuando se **usa** autoQty vs cuando se **ignora** (con motivo).
* **Evidencia:** dif de logs muestra `468/STR ENTRY ... qty=autoQty` únicamente cuando lo habilites.

## Capa 6 — Underfunded policy y límites

* **Objetivo:** evitar entradas “carísimas” por SL largo/tick value alto.
* **Acciones (ya lo tienes en cálculo):**

  * `SkipIfUnderfunded` → qty=0 y log `ABORT`.
  * `MinQtyIfUnderfunded` → forzar min qty y log de advertencia con `actualRisk`.
  * **Límites globales**: clamp `1..MaxContracts` (p.ej. 1000) en el cálculo; y `MaxRiskPerTradeUSD` (si lo añades) como red-line.
* **Evidencia:** replays con SL largo deben mostrar `ABORT` o `minQty` según política, nunca romper.

## Capa 7 — End-to-end con TP/SL y currency

* **Objetivo:** comprobar que el cálculo es consistente con tus brackets (SL/TP actuales).
* **Acciones:**

  * Validar que el `slDistanceInTicks` que usa el riesgo coincide con el SL que realmente envías/ajustas.
  * Si hay `QuoteCurrency` ≠ `USD`, o futuros con coste por tick no-USD, documentar y (si aplica) factor de conversión (lo puedes dejar anotado para un PASO 4).
* **Evidencia:** snapshot `468/CALC` y `468/STR` cuentan la misma historia (distancias y qty coherentes).

---

# Estructura de archivos sugerida

* `docs/RISK_ROLLOUT.md` → guía por capas (lo de arriba, conciso, con lista de verificación).
* `docs/RISK_LOGGING.md` → prefijos, formato de mensajes, comandos `grep` habituales.
* `docs/RISK_API_NOTES.md` → notas de API ATAS que ya has descubierto (p. ej., `Portfolio.BalanceAvailable` es `decimal?`, `Portfolio.Balance` es `decimal`, no existe `Equity`; `Security.TickSize`/`QuoteCurrency`; evitar miembros obsoletos).
* `tools/deploy_risk_management.ps1` → build + copia + seteo de flags (dry-run por defecto).
* `test_scenarios/risk_management/scenarios/...` → ya lo tienes; añade “expected logs” por escenario.
* `logs/current/` y `logs/emergency/` → ya existen (mantén).

Tu **mapa de ruta** (`map_rute.md`) ya marca la UI/propiedades y el plan por pasos; úsalo de contrato para decidir cuándo subir cada flag.&#x20;

