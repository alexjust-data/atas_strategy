He leído tu RiskManager.Manual.cs y lo que ya tienes hoy da estas pistas:

El sistema actual está pensado para 3 TPs reales y usa un “builder” que normaliza splits y solo crea TP1..TP3 si su % > 0 (lo ves en _RmSplitHelper.BuildTpArrays, que recorta a 3 y re-normaliza). 

RiskManager.Manual

Hay modos declarados de trailing (RmTrailMode { Off, BarByBar, TpToTp }), así que la UI ya tiene el “hueco” conceptual, solo falta rematar el motor con el comportamiento que quieres sin tocar el resto. 

RiskManager.Manual

Abajo te dejo un plan quirúrgico (en dos fases) para: 1) Trailing simple “TP→TP” y “bar a bar cada N velas”, y 2) poder trabajar con TP1..N sin cambiar la arquitectura de brackets que hoy funciona.

Fase A — Trailing “plug-in” (sin tocar lo demás)
1) Modos de trailing (reusar la UI actual)

Mode: Off | TpToTp | BarByBar (ya existe el enum). 

RiskManager.Manual

Confirm bars: ya lo tienes; lo usamos como “solo muevo cuando cierre la vela de confirmación”.

Distance (ticks): lo interpretamos como el offset desde el ancla (igual que haces para el SL por vela previa). La UI ya tiene “Distance (ticks)” en Trailing.

2) Comportamiento

TpToTp

Al tocar el siguiente “milestone” (TP1, luego TP2, luego TP3, etc.), muevo el SL al extremo de la última(s) vela(s) según dirección (LOW para largos, HIGH para cortos) ± Distance (ticks).

Solo aprieta (nunca afloja): si el nuevo SL quedara peor que el actual, no se mueve.

BarByBar

Cada N velas (N = “Confirm bars”) al cerrar, recalculo ancla = mínimo/máximo del bloque de las últimas N velas y coloco el SL en ancla ± Distance.

Igual: solo aprieta.

3) Reglas de seguridad (no tocan nada del resto)

No mover mientras haya fills sospechosos diferidos (tu cola de suspect fills); usar el mismo gate que ya usas en BE/heartbeat.

Respetar tu volatility floor solo en el primer SL (cuando no hay N-1). Luego el trailing ya es “solo apretar”.

Tasa de movimiento: máx. 1 ajuste por barra para no spamear la API.

4) Logs para depurar

RM/TRAIL/ARMED: modo, params, ancla.

RM/TRAIL/MOVE: de → a, causa (TPk tocado | cierre de barra k/N), ancla y offset.

Resultado: con esto puedes activar TpToTp o BarByBar cada N velas sin tocar tu lógica de entrada, BE, ni brackets. Todo convive.

Fase B — TP1..N “sin romper los 3 brackets”

La limitación real hoy no es de concepto, es operativa: tu constructor de TPs solo maneja 3 líneas y ATAS se lleva genial con 2–3 órdenes OCO simultáneas. Cambiar a 20 TPs “reales” multiplicaría órdenes, OCOs y edge cases.

La solución quirúrgica es un “ladder” virtual con ventana deslizante de 3 TPs reales:

Nuevo “Targets ladder (avanzado)”

Editor tipo tabla (mismo look & feel de tu “Targets”) pero con filas ilimitadas: R y % a cerrar.

Compatibilidad total:

Si el ladder está vacío → se usan tus 3 TPs de siempre (no cambia nada).

Si el ladder tiene datos → el ladder manda para milestones de trailing y para escalados.

Ventana deslizante (máx. 3 reales)

Mantén como máximo 3 órdenes TP vivas.

Cuando se llena/descarta un TP, publicas el siguiente del ladder (si queda).

Así soportas TP4…TP20 sin cambiar la arquitectura ni saturar el broker.

Trailing sobre el ladder

En TpToTp, el “siguiente TP” viene del ladder (real o virtual).

Si el % de un peldaño es 0, es un TP virtual: sirve para armar BE o avanzar trailing sin vender.

UI mínima (no rompe la actual)

En “Stops & TPs”:

Targets (clic para editar): se queda como está (3 TPs).

Targets ladder (avanzado): un nuevo diálogo con +/– filas.

Max concurrent TPs (read-only = 3) para que quede claro cómo funciona la ventana.

Trail starts at: BE | TP1 | TPk | Immediately (solo un ComboBox).

Si ladder activo, en la cabecera de “Stops & TPs” mostramos un resumen corto (“Ladder: 7 escalones, 3 activos”).

Interacción con lo que ya tienes

Breakeven: sin cambios; se dispara por “tocar precio” (no fill) y el R del trigger lo tomamos de la tabla/ladrillo (virtual o real).

SL inicial: sigue siendo “PrevBarOppositeExtreme” con tu offset y piso de volatilidad cuando no hay N-1.

Cola de fills sospechosos y gates: se respetan tal cual (trailing solo opera cuando el sistema está “limpio”).

P&L / estadísticas: no se tocan.

Qué ganarías exactamente

Trailing por TPs (con o sin cobro) y bar a bar cada N velas con 3 parámetros que ya existen en tu UI.

Hasta N TPs sin multiplicar órdenes: sigues viendo como mucho 3 brackets en el gráfico; el resto son “peldaños” que se van activando conforme avanzas.

Simplicidad: si no activas el ladder, todo funciona como hoy.

Checklist de aceptación

TpToTp

Con TP1 %=0 (virtual) y BE: OnTPTouch (1), el BE se arma y el SL no vende nada.

Al tocar TP2 (real o virtual), el SL se mueve al ancla ± distance y no vuelve atrás.

BarByBar (N=2)

Cada 2 cierres, el SL avanza al extremo de las últimas 2 velas ± distance.

Nunca afloja.

Ladder

Defino 6 peldaños; en el gráfico solo veo 3 TPs; al llenarse TP1, aparece TP4, etc.

Si un peldaño tiene %=0, no vende pero sirve para BE/trailing.

Si te encaja este plan, te preparo los diffs mínimos por fases (primero Trailing, luego Ladder). Con eso mantendremos lo actual al 100% y añadiremos las dos piezas que pides sin sorpresas.