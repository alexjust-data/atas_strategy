using System;


namespace MyAtasIndicator.Shared
{
    public enum Trend { None = 0, Up = 1, Down = -1 }


    /// <summary>
    /// Señal emitida por el indicador 468 (FourSixEightIndicator) justo cuando pinta la vela "Cambio".
    /// No se recalcula ni se transforma: la estrategia usa este mismo objeto tal cual.
    /// </summary>
    public sealed class Signal468
    {
        /// <summary>Identificador único de la señal (para evitar duplicados).</summary>
        public Guid Uid { get; set; }


        /// <summary>Marca temporal cuando el indicador publicó la señal (hora local).</summary>
        public DateTime Ts { get; set; }


        /// <summary>Índice de barra en el contexto del chart donde se generó.</summary>
        public int BarId { get; set; }


        /// <summary>+1 = compra (Cambio alcista), -1 = venta (Cambio bajista).</summary>
        public int Dir { get; set; }


        /// <summary>Refleja la dirección en forma de enum.</summary>
        public Trend Trend { get; set; }


        /// <summary>Fuente del disparo: "WPR" (reglas 11/12) o "GenialLine".</summary>
        public string Source { get; set; } = "WPR";


        /// <summary>Precio de referencia en el momento de publicar (opcional, informativo).</summary>
        public decimal PriceRef { get; set; }


        /// <summary>
        /// Clona la señal con nuevo Uid (útil si necesitas re-publicar conservando payload).
        /// </summary>
        public Signal468 CloneWithNewUid()
        => new Signal468
        {
            Uid = Guid.NewGuid(),
            Ts = Ts,
            BarId = BarId,
            Dir = Dir,
            Trend = Trend,
            Source = Source,
            PriceRef = PriceRef
        };
    }
}