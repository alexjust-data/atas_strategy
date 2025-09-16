# Confluencia 2 — **EMA8 vs Wilder8**

## ¿Qué valida?

Que la relación **EMA8** frente a **Wilder8** respalde la dirección de la señal en **N+1**.
Es un filtro de momentum/suavizado para acompañar al cruce con GL.

## ¿Cuándo se evalúa?

Siempre en **N+1**, junto con el resto de validaciones previas a la orden.

## Lógica exacta (dos versiones)

### A) Versión básica (la que ya tienes)

Con una **tolerancia** en ticks:

```text
diff = EMA8 - Wilder8
tol  = Ticks(EMA_vs_Wilder_tolerance)

BUY  pasa si diff ≥ -tol    (≈ EMA≥W o un poco por debajo)
SELL pasa si diff ≤ +tol    (≈ EMA≤W o un poco por encima)
```

> Esto permite igualdad y una holgura **simétrica** de 1–2 ticks.

### B) Versión extendida (granularidad por modos) — *opcional si la activas*

* **Rule = Strict**:
  BUY: `EMA8 > Wilder8` · SELL: `EMA8 < Wilder8`
* **Rule = Inclusive**:
  BUY: `EMA8 ≥ Wilder8` · SELL: `EMA8 ≤ Wilder8`
* **Rule = Window (pre-cross)**: *(recomendada para backtests finos)*
  Definimos `diff = EMA8 − Wilder8` y `preTol = Ticks(N)`:

  * **BUY** pasa si `diff ≥ −preTol`  (acepta “un poco antes del cruce”, igualdad y después del cruce).
  * **SELL** pasa si `diff ≤ +preTol`.

Parámetros propuestos para el panel:

* **EMA vs Wilder rule**: `Strict | Inclusive | Window`
* **EMA vs Wilder pre-cross tolerance (ticks)**: `0–2`
* **Count equality as pass**: `true/false` (decide si la igualdad entre EMA8 y Wilder8 aprueba la confluencia.)

## Ejemplo práctico

* Señal **BUY** (N cruzó GL al alza). En N+1 tienes:
  `EMA8 = 19910,00` · `Wilder8 = 19909,75` → `diff = +0,25`

  * **Strict**: pasa (diff>0).
  * **Inclusive**: pasa.
  * **Window (preTol=1 tick)**: pasa (también pasaría si `diff = −0,25` por el margen “pre-cruce”).

## Logs esperados

```
CONF#2 (EMA8 vs W8 @N+1) e8=19910,00 w8=19909,75 rule=Window preTol=1 -> OK
ABORT ENTRY: Conf#2 failed                           (si no pasa)
```

## Casos borde & consejos

* Si `EMA8 == Wilder8` y **no** usas igualdad, la confluencia básica puede **fallar**; para evitar falsos negativos usa **1–2 ticks** de tolerancia o `Inclusive/Window`.
* **Default recomendado**: ON con **tolerancia 1–2 ticks** (o `Window` con `preTol=1` si activas la versión extendida).

---

## Recordatorios operativos (comunes a ambas)

* **Siempre N+1**: las confluencias se miran en la vela de **ejecución**, no en la de señal.
* Si **OnlyOnePosition** está activo, además debe pasar la **guardia** (sin órdenes vivas, sin posición, sin cooldown).
* Los **brackets** se cuelgan **post-fill**; su creación no depende de las confluencias, sino de que la entrada se haya ejecutado.

---

## Greps rápidos para auditar confluencias

```bash
# GenialLine slope (CONF#1)
grep -nE "CONF#1|Genial slope|ABORT ENTRY: Conf#1" EMERGENCY_ATAS_LOG.txt

# EMA vs Wilder (CONF#2)
grep -nE "CONF#2|EMA8 vs W8|ABORT ENTRY: Conf#2" EMERGENCY_ATAS_LOG.txt

# Secuencia completa alrededor de una señal/ejecución
UID="tu_uid_parcial"
grep -nE "CAPTURE|PROCESSING PENDING|CONF#|ABORT ENTRY|MARKET submitted|BRACKETS ATTACHED" EMERGENCY_ATAS_LOG.txt | grep "$UID"
```

---

## Defaults recomendados (resumen)

* **Require GenialLine slope**: **ON**
* **EMA vs Wilder**: **ON**

  * Básica: **tolerancia = 1–2 ticks**
  * Extendida (si la activas): `rule = Window`, `preTol = 1`, `equality = true`

Con esto tienes la foto completa de **qué valida cada confluencia, cuándo, cómo**, y **cómo interpretarlo en logs**.