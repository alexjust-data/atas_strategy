Aquí tienes la **documentación por confluencia** de la estrategia (lista para guardar). He mantenido el mismo estilo claro y práctico que usamos antes.

---

# Confluencia 1 — Pendiente de **Genial Line** a favor de la señal

## ¿Qué valida?

Confirma que la **dirección de la señal** (BUY/SELL) **coincide** con la **pendiente de la Genial Line** en el **momento de ejecutar (N+1)**.

## ¿Cuándo se evalúa?

Siempre en **N+1**, justo antes de lanzar la market.

> La señal base ya existe porque la vela **N** cerró cruzando la Genial Line.

## Lógica exacta

Definimos:

* `GL[N]`  → valor de Genial Line al cierre de la vela **N** (vela de señal).
* `GL[N+1]`→ valor de Genial Line en la vela **N+1** (vela de ejecución).

```text
trendUp   = GL[N+1] > GL[N]
trendDown = GL[N+1] < GL[N]

BUY  pasa  si trendUp  == true
SELL pasa  si trendDown == true
(FLAT/igual → falla)
```

## Parámetro en el panel

* **Require GenialLine slope with signal direction** (ON/OFF)

## Ejemplo práctico

* Vela N cierra **por encima** de GL → señal **BUY**.
* En N+1, `GL[N+1] = 19.908,25` y `GL[N] = 19.907,33` → **sube** → **OK**.
* Si `GL[N+1] ≤ GL[N]`, la entrada se **aborta** por confluencia #1.

## Logs esperados

```
CONF#1 (GL slope @N+1) gN=19907,33272 gN1=19908,25000 trend=UP -> OK
ABORT ENTRY: Conf#1 failed                (si no pasa)
```

## Casos borde & consejos

* GL **plana** (igualdad) → tratar como **FAIL** (evita señales débiles).
* Asegúrate de que el log use **la serie** de GL (no el precio). Hemos unificado eso en el código para evitar mensajes confusos.
* **Default recomendado**: ON.


