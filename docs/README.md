
# ATAS 468 Strategy - Documentation

Documentación completa de la estrategia de trading cuantitativo 468.

## Índice
- [DEVELOPMENT_HISTORY](DEVELOPMENT_HISTORY.md) - Historial completo de desarrollo
- [validation/](validation/) - Análisis de confluencias y validaciones
- [execution/](execution/) - Lógica de ejecución N+1 y timing
- [concluencias/](concluencias/) - Análisis técnico de confluencias EMA vs Wilder
- [grep-cheatsheet/](grep-cheatsheet/) - Herramientas de análisis de logs
- [UI_Atas/](UI_Atas/) - Configuración de interfaz ATAS

## README (raw) — Panel de parámetros de la estrategia

**Nombre en ATAS:** `468 – Simple Strategy (GL close + 2 confluences) - FIXED`
**Entrada:** Ejecuta en **N+1** al `open` de la barra siguiente a la señal del indicador 468 (cruce Genial/WPR, según `TriggerSource` del indicador). **Bracket OCO**: SL único por la **cantidad total** + TPs distribuidos por múltiplos de R. &#x20;

## Control

* **Only one position at a time**
  Si está activo, evita abrir nueva posición si ya hay posición abierta (impide solapamientos).&#x20;

## Orders

* **Quantity**
  Nº total de contratos/lotes a entrar. Se reparte automáticamente entre los TPs activos (ver “Risk/Targets”).&#x20;
* **Execution Mode**
  `Market` (true) entra a mercado en N+1; `Limit` (false) lanza una limit en N+1 al `open` como precio de referencia.&#x20;

## Confluences

* **Require 468 Trend Up for Long**
  Solo permite largos si la tendencia del indicador 468 está alcista (`IsTrendUp()`). *Requiere implementar helper en el indicador o adaptar la comprobación.*&#x20;
* **Require 468 Trend Down for Short**
  Solo permite cortos con tendencia bajista (`IsTrendDown()`). *Id. nota anterior.*&#x20;
* **Validate Genial cross locally (SMA proxy)**
  Valida localmente que el **cierre de la vela de señal (N)** quedó al lado correcto de la GenialLine. Filtro extra contra falsos cruces.&#x20;
* **Allow flip (change side)**
  Permite flips automáticos (cambio de lado con orden contraria) cuando aparece señal opuesta. Si está desactivado, se ignoran las señales contrarias mientras haya posición.&#x20;
* **Flip cooldown (bars)**
  Nº mínimo de barras a esperar entre flips. Reduce *reversals* reactivos.&#x20;
* **Flip confirm closes vs Genial (bars)**
  Exige *n* cierres al otro lado de Genial antes de permitir el flip. Refuerza la robustez del cambio de lado.&#x20;
* **Require 468 trend for flip**
  Solo permite flip si la tendencia 468 favorece el nuevo lado.&#x20;
* **Add cooldown same side (bars)**
  Espaciado mínimo entre *adds* (piramidación) en la misma dirección.&#x20;

## Risk/Targets

* **Use SL from signal candle**
  Si está activo, el **SL** se ancla a la vela **N** (vela de señal):

  * LONG: `SL = Low(N) − offsetTicks`
  * SHORT: `SL = High(N) + offsetTicks`
    Si está desactivado, el SL se calcula desde el precio de entrada.&#x20;
* **SL offset (ticks)**
  Desplazamiento en ticks respecto al High/Low usado para fijar el SL.&#x20;
* **Enable TP1 / TP2 / TP3**
  Activa cada TP. La cantidad total se reparte de forma inteligente entre los TPs activos (resto al último TP).&#x20;
* **TP1/TP2/TP3 (R multiple)**
  Niveles por múltiplos de **R** donde `R = |Entry − SL|`.
  Ej.: `TP2_R = 2` → TP2 a **2R** desde la entrada, con redondeo al **tick size** del instrumento.&#x20;

## Misc

* **IsActivated / Visible / Locked**
  Ajustes estándar de ATAS para activar, mostrar en el chart y bloquear edición. (No afectan la lógica interna).&#x20;

## Common

* **Source**
  Serie de precios fuente (Close(MNQ) en la captura). La estrategia usa la serie del chart para validaciones locales (por ejemplo, cierres vs Genial).&#x20;

## Drawing

* **Panel**
  Dónde se dibuja el trazo asociado (si aplica). Estético, no funcional.&#x20;

### Notas de ejecución

* **Pipeline N→N+1**: captura señal en **N** del indicador 468 y ejecuta la orden en **N+1**.&#x20;
* **Bracket OCO completo**: 1 **Stop** por **cantidad total** + varios **TPs** *con el mismo OCO Id*; cuando se ejecuta el SL, cancela TPs; cuando se llenan todos los TPs, cancela SL.&#x20;
* **Tick size / redondeo**: todas las órdenes se ajustan automáticamente al `MinStep` del instrumento.&#x20;

