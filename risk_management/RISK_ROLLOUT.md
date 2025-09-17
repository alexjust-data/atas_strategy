# 📋 PLAN DETALLADO DE IMPLEMENTACIÓN - RISK MANAGEMENT

**Sistema completo de gestión de riesgo para estrategias de trading**: Implementación incremental de position sizing automático (Manual/FixedUSD/%), breakeven system, y diagnósticos en tiempo real. Plan de 6 pasos priorizando la **seguridad del código base**.

## 🎯 **PASO 1: FOUNDATION - Enums y Properties Básicos**

## 🌳 **TARGET UI TREE - ESTRUCTURA COMPACTA**

```
▼ General
  └─ Quantity                          2
  └─ Allow only one position at a t... ✓

▼ Risk Management                    ← SECCIÓN CONSOLIDADA
  ├─ ▼ 🎯 Position Sizing           ← SUBCATEGORÍA COLAPSABLE
  │   ├─ Position Sizing Mode         [FixedRiskUSD ▼]
  │   ├─ Risk per trade (USD)         100
  │   ├─ Risk % of account            0.5
  │   ├─ Manual account equity over... 650
  │   ├─ Tick value overrides (SYM=V) MNQ=0.5;NQ=5;MES=1.25;ES=12
  │   └─ Enable detailed risk logging ✓
  │
  ├─ ▼ 🔒 Breakeven                 ← SUBCATEGORÍA COLAPSABLE
  │   ├─ Breakeven mode               [OnTPFill ▼]
  │   ├─ Breakeven offset (ticks)     4
  │   ├─ Trigger breakeven manually   □
  │   ├─ Trigger on TP1 touch/fill    ✓
  │   ├─ Trigger on TP2 touch/fill    □
  │   └─ Trigger on TP3 touch/fill    □
  │
  └─ ▼ 📊 Diagnostics               ← SUBCATEGORÍA COLAPSABLE
      ├─ Effective tick value (USD/t) 0.5    (read-only)
      ├─ Effective tick size (pts/t)  0.25   (read-only)
      └─ Effective account equity     650    (read-only)

▼ Risk/Targets
  └─ Use SL from signal candle        ✓
  └─ SL offset (ticks)                2
  └─ Enable TP1                       ✓
  └─ TP1 (R multiple)                 1,
  └─ Enable TP2                       ✓
  └─ TP2 (R multiple)                 2,
  └─ Enable TP3                       □
  └─ TP3 (R multiple)                 3,

▼ Validation
  └─ Validate GL cross on close...    ✓
  └─ Hysteresis (ticks)               0

▼ Confluences
  └─ Require GenialLine slope...      ✓
  └─ Require EMA8 vs Wilder8...       ✓

▼ Execution
  └─ Strict N+1 open...               ✓
  └─ Open tolerance (ticks)           4
```

### **Explicación de íconos:**
- 🎯 **Position Sizing**: Cálculo automático de contratos basado en riesgo objetivo
- 🔒 **Breakeven**: Sistema que mueve SL a breakeven cuando TP1 es alcanzado
- 📊 **Diagnostics**: Información en tiempo real sobre detecciones automáticas

### **Explicación detallada del árbol de Risk Management:**

```
▼ Risk Management
  │
  ├─ ▼ 🎯 Position Sizing ──────────── SUBCATEGORÍA COLAPSABLE
  │   │
  │   ├─ Position Sizing Mode [FixedRiskUSD ▼]
  │   │   ├─ Manual ────────────── Usa cantidad fija de General > Quantity (ej: 2 contratos)
  │   │   ├─ FixedRiskUSD ──────── Calcula contratos para arriesgar X dólares por trade
  │   │   │                        (Si underfunded → ABORT automáticamente)
  │   │   └─ PercentOfAccount ──── Calcula contratos basado en % del equity total
  │   │                            (Si underfunded → ABORT automáticamente)
  │   │
  │   ├─ Risk per trade (USD) [100]
  │   │   └─ Cantidad máxima en USD a perder por trade (solo FixedRiskUSD mode)
  │   │
  │   ├─ Risk % of account [0.5]
  │   │   └─ Porcentaje del equity total a arriesgar (solo PercentOfAccount mode)
  │   │
  │   ├─ Manual account equity override [650]
  │   │   └─ Valor manual si detección automática falla o quieres override
  │   │
  │   ├─ Tick value overrides (SYM=V) [MNQ=0.5;NQ=5;MES=1.25;ES=12]
  │   │   ├─ Format: SYMBOL=VALUE separado por ;
  │   │   ├─ MNQ=0.5 (Micro NASDAQ $0.50/tick)
  │   │   ├─ NQ=5 (E-mini NASDAQ $5.00/tick)
  │   │   ├─ MES=1.25 (Micro S&P $1.25/tick)
  │   │   └─ Si no definido: usa detección automática ATAS
  │   │
  │   └─ Enable detailed risk logging [✓]
  │       └─ Activa logging detallado de cálculos para debugging
  │
  ├─ ▼ 🔒 Breakeven ───────────────── SUBCATEGORÍA COLAPSABLE
  │   │
  │   ├─ Breakeven mode [OnTPFill ▼]
  │   │   ├─ OnTPFill ──────────── Activa automáticamente cuando TP1 se ejecuta
  │   │   ├─ Manual ───────────── Solo activación manual
  │   │   └─ Disabled ─────────── Sistema breakeven desactivado
  │   │
  │   ├─ Breakeven offset (ticks) [4]
  │   │   └─ Ticks por encima de entrada para nuevo SL (entrada + 4 ticks)
  │   │
  │   ├─ Trigger breakeven manually [□]
  │   │   └─ Botón para activar breakeven manualmente sin esperar TP1
  │   │
  │   ├─ Trigger on TP1 touch/fill [✓]
  │   │   └─ Activa breakeven cuando TP1 se ejecuta (configuración principal)
  │   │
  │   ├─ Trigger on TP2 touch/fill [□]
  │   │   └─ Activa breakeven cuando TP2 se ejecuta (configuración avanzada)
  │   │
  │   └─ Trigger on TP3 touch/fill [□]
  │       └─ Activa breakeven cuando TP3 se ejecuta (configuración avanzada)
  │
  └─ ▼ 📊 Diagnostics ─────────────── SUBCATEGORÍA COLAPSABLE
      │
      ├─ Effective tick value (USD/tick) [0.5] (read-only)
      │   ├─ Valor real por tick que usa el sistema
      │   ├─ Resultado de: override manual → detección ATAS → fallback
      │   └─ Ejemplo: 0.5 (MNQ), 5.0 (NQ), 1.25 (MES)
      │
      ├─ Effective tick size (points/tick) [0.25] (read-only)
      │   ├─ Tamaño del tick en puntos del instrumento
      │   ├─ Usado para convertir distancias SL/TP a ticks
      │   └─ Ejemplo: 0.25 puntos/tick para MNQ
      │
      └─ Effective account equity (USD) [650] (read-only)
          ├─ Equity real usado para cálculos % de cuenta
          ├─ Detección: Portfolio API → manual override → fallback
          └─ Base para PercentOfAccount mode
```

---


# Plan incremental (seguro y reversible)

## Capa 0 — Congelar baseline

* **Objetivo:** asegurar que todo lo actual sigue igual mientras añadimos piezas alrededor.
* **Acción:** añadir un flag global (propiedad en la estrategia) `EnableRiskManagement` (por defecto **OFF**) y un sub-flag `RiskDryRun` (por defecto **ON** cuando `EnableRiskManagement=ON`).
* **Evidencia a revisar:** los logs actuales (prefijo `468/STR`) siguen sin variación, no aparece ningún `468/RISK` salvo mensajes de init. Tu baseline de replays se mantiene tal cual (ya vimos que hoy estaba perfecto).&#x20;

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

# Qué haría **ahora mismo** (tu siguiente commit)

1. **Arreglar definitivamente el bug de `decimal.HasValue`**
   — Centraliza `GetEffectiveAccountEquity()` con el patrón correcto (nullable para `BalanceAvailable`, no-nullable para `Balance`), y añade logs de tipos (opcional, con `GetType().Name`) para detectarlo rápido la próxima vez.
   *(Esto te elimina el CS1061 y estabiliza la Capa 3.)*

2. **Meter `GetEffectiveSecurityCode()` + RISK INIT log**
   — Así desbloqueas `TickValueOverrides` por símbolo y verás el símbolo correcto en diagnósticos.

3. **Encender `CalculateQuantity()` en dry-run con throttle + SNAPSHOT**
   — Ya lo tienes; solo asegúrate de que está **siempre** en dry-run (no toca `Quantity` real) y que el snapshot imprime todo lo necesario para comparar en los replays.

Con esas 3 cosas, el **riesgo de regresión es cero** y ya tendrás trazas `468/RISK`/`468/CALC` en los replays, que hoy **no existen** (tu propio informe decía `grep -c "468/RISK|468/CALC" → 0`).&#x20;

