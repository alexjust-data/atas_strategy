using System.ComponentModel;
using ATAS.Indicators;
using ATAS.Types;

namespace MyAtasIndicator.Indicators
{
    [Category("Custom")]
    [DisplayName("HTF100 Publisher")]
    public class HTF100Publisher : Indicator
    {
        private readonly ValueDataSeries _series = new("HTF100");

        public HTF100Publisher()
        {
            // No toques el Panel; el predeterminado es suficiente.
            DataSeries.Add(_series);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            _series[bar] = value; // Demo sencillo
        }
    }
}






