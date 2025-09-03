using System;
using System.ComponentModel;
using ATAS.Indicators;
using ATAS.Types;

namespace MyAtasIndicator.Indicators
{
    [Category("Custom")]
    [DisplayName("LTF10 Absorption")]
    public class LTF10Absorption : Indicator
    {
        private readonly ValueDataSeries _series = new("Absorption");

        public LTF10Absorption()
        {
            DataSeries.Add(_series);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            _series[bar] = Math.Abs(value);
        }
    }
}







