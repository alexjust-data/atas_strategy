**grep-cheatsheet** para identificar (rápido) cuándo **se cumplen** las condiciones y qué pasó con cada señal en tus logs. Usa el que corresponda a tu archivo (por ej. `ATAS_SESSION_LOG.txt`).

> Sugerencia: define una variable
> `LOG=ATAS_SESSION_LOG.txt` (o `EMERGENCY_ATAS_LOG.txt`) y copia/pega.

---

### 0) Barrido rápido de señales completas (de captura a ejecución)

```bash
# Secuencia típica de una señal válida y ejecutada
grep -nE "CAPTURE: N=|CONF#1 .* -> OK|CONF#2 .* -> OK|ENTRY \+ BRACKET sent|BRACKETS ATTACHED" "$LOG"
```

### 1) Dónde hubo **cruce válido** al cierre (señal base)

```bash
grep -nE "GENIAL CROSS detected|CAPTURE: N=.*\(confirmed close\)" "$LOG"
```

### 2) Confluencias en N+1 (quién pasó / quién falló)

```bash
# Todo lo relacionado con confluencias
grep -nE "CONF#1|Genial slope|CONF#2|EMA8 vs W8|ABORT ENTRY: Conf#" "$LOG"

# Solo las que pasaron
grep -nE "CONF#1 .* -> OK|CONF#2 .* -> OK" "$LOG"

# Resumen de fallos por tipo
grep -oE "ABORT ENTRY: Conf#(1|2) failed" "$LOG" | sort | uniq -c
```

### 3) Estricto N+1 (armado/ejecución/caducidad)

```bash
grep -nE "PENDING ARMED|PROCESSING PENDING @N\+1|PENDING EXPIRED" "$LOG"
# Entradas abortadas por precio en apertura (tolerancia)
grep -nE "ABORT ENTRY: .*tolerance|beyond open tolerance" "$LOG"
```

### 4) Guard de **OnlyOnePosition** (bloqueos por estado)

```bash
# Ver cada decisión del guard
grep -nE "GUARD OnlyOnePosition" "$LOG"
# Resumen PASS/BLOCK
grep -oE "GUARD OnlyOnePosition.*-> (PASS|BLOCK)" "$LOG" | sort | uniq -c
```

### 5) Envío de entrada y **brackets**

```bash
# Envío de la market y attach de brackets
grep -nE "MARKET submitted|ENTRY \+ BRACKET sent|BRACKETS ATTACHED" "$LOG"

# Ciclo de vida de los hijos (TP/SL)
grep -nE "OnOrderChanged: 468(ENTRY|TP|SL)" "$LOG"

# Cuántas ejecuciones con brackets hubo
grep -c "BRACKETS ATTACHED" "$LOG"
```

### 6) Anti-flat y reconciliación (para diagnosticar “desapariciones”)

```bash
# Ventana anti-flat y liberación controlada
grep -nE "ANTI-FLAT|FLAT CONFIRMED|Trade (lock|candado) RELEASED" "$LOG"

# Reconciliación (y skips seguros cuando net es dudoso)
grep -nE "RECONCILE (SKIP|ReconcileBracketsWithNet|SELF-HEAL|re-attached)" "$LOG"
```

### 7) **Detalles** de cada confluencia (para auditar cálculos)

```bash
# Pendiente de GenialLine (GL slope)
grep -nE "CONF#1.*GL slope|Genial.*GL\[|GL\[N\+1\]|trend=UP|trend=DOWN|trend=FLAT" "$LOG"

# EMA vs Wilder (regla, tolerancia, igualdad, valores)
grep -nE "EMA8 vs Wilder.*rule|pre-cross tol|count equality|e8=|w8=" "$LOG"
```

### 8) Foco por **bar** o **uid** (inspección puntual)

```bash
# Reemplaza 17621 por el bar que te interese (muestra contexto ±10 líneas)
grep -n "bar=17621" "$LOG" -A10 -B10

# Por uid de señal
grep -n "uid=<PEGA_AQUI_EL_UID>" "$LOG" -A10 -B10
```

### 9) Resúmenes útiles

```bash
# ¿Cuántas señales se capturaron?
grep -c "CAPTURE: N=" "$LOG"

# ¿Cuántas expiraron en N+1?
grep -c "PENDING EXPIRED" "$LOG"

# ¿Cuántas entradas ejecutadas (brackets colgados)?
grep -c "BRACKETS ATTACHED" "$LOG"
```

---

#### Cómo leerlo rápido (patrones clave)

* **Señal ejecutada**:
  `CAPTURE` → `PENDING @N+1` → `CONF#1 OK` + `CONF#2 OK` → `ENTRY + BRACKET sent` → `BRACKETS ATTACHED`
* **Señal válida pero bloqueada**:
  `CONF#1 OK` + `CONF#2 OK` → `GUARD OnlyOnePosition ... -> BLOCK`
* **Señal abortada por confluencias**:
  `ABORT ENTRY: Conf#1 failed` **o** `ABORT ENTRY: Conf#2 failed`
* **Caducada en N+1**:
  `PENDING EXPIRED` **o** `ABORT ENTRY: beyond open tolerance`
* **Brackets “se van”**:
  mirar `ANTI-FLAT` + `RECONCILE ... SKIP/SELF-HEAL` + estados `OnOrderChanged` de `468TP/468SL`.

Si quieres, te preparo un “bundle” de scripts (bash/powershell) que ejecuten todos estos filtros y te saquen un **informe resumido** por sesión.
