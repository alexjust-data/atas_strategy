using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using System.Text.RegularExpressions;
using ATAS.Strategies.Chart;
using ATAS.Types;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using MyAtas.Shared;
using MyAtas.Risk.Engine;
using MyAtas.Risk.Models;

namespace MyAtas.Strategies
{
    // Enums externos para serializaciÃƒÆ’Ã‚Â³n correcta de ATAS
    public enum RmSizingMode { Manual, FixedRiskUSD, PercentAccount }
    // Mantiene compat. binaria pero aclara intenciÃ³n:
    public enum RmBeMode { Off, OnTPTouch, OnTPFill }
    public enum RmTrailMode { Off, BarByBar, TpToTp }
    public enum RmStopPlacement { ByTicks, PrevBarOppositeExtreme }  // modo de colocaciÃƒÆ’Ã‚Â³n del SL
    public enum RmPrevBarOffsetSide { Outside, Inside }              // NEW: lado del offset (fuera/dentro)

    // --- Volatility floor (solo cuando no hay N-1) ---
    public enum RmVolMetric { ATR, AverageRange, MedianRange }

    // --- Helpers de normalizacion de TPs/splits ---
    internal static class _RmSplitHelper
    {
        // Devuelve arrays listos para el motor: solo TPs con split>0 y suma=100 (el ultimo absorbe la diferencia)
        public static (decimal[] r, int[] s) BuildTpArrays(int preset, decimal tp1, decimal tp2, decimal tp3, int sp1, int sp2, int sp3)
        {
            var rList = new System.Collections.Generic.List<decimal>();
            var sList = new System.Collections.Generic.List<int>();
            var n = Math.Clamp(preset, 1, 3);
            if (n >= 1 && sp1 > 0) { rList.Add(tp1); sList.Add(Math.Max(0, sp1)); }
            if (n >= 2 && sp2 > 0) { rList.Add(tp2); sList.Add(Math.Max(0, sp2)); }
            if (n >= 3 && sp3 > 0) { rList.Add(tp3); sList.Add(Math.Max(0, sp3)); }
            if (rList.Count == 0)
            {
                // Sin % válidos → cero TPs reales (permitido con VirtualBE/Trailing)
                return (Array.Empty<decimal>(), Array.Empty<int>());
            }
            var sum = sList.Sum();
            if (sum != 100)
            {
                // Re-normaliza a 100 y el ultimo absorbe la diferencia
                for (int i = 0; i < sList.Count; i++)
                    sList[i] = (int)Math.Max(0, Math.Round(100m * sList[i] / Math.Max(1, sum)));
                var diff = 100 - sList.Sum();
                sList[^1] = Math.Max(1, sList[^1] + diff);
            }
            return (rList.ToArray(), sList.ToArray());
        }

        // Reparte 'total' segÃºn 'splits' (% normalizados a 100). Ãšltimo absorbe diferencia.
        public static int[] SplitQty(int total, int[] splits)
        {
            if (total <= 0 || splits == null || splits.Length == 0) return Array.Empty<int>();
            var q = new int[splits.Length];
            var remain = total;
            for (int i = 0; i < splits.Length; i++)
            {
                if (i == splits.Length - 1)
                {
                    q[i] = Math.Max(0, remain);
                }
                else
                {
                    var qi = (int)Math.Round(total * (splits[i] / 100m));
                    q[i] = Math.Max(0, qi);
                    remain -= q[i];
                }
            }
            // Ajuste anti-todo-en-cero: si sumÃ³ 0 pero total>0, pon 1 al Ãºltimo
            if (q.Sum() == 0 && total > 0) q[^1] = total;
            // Ajuste si por redondeo sobrepasÃ³:
            var diff = q.Sum() - total;
            if (diff > 0) q[^1] = Math.Max(0, q[^1] - diff);
            return q;
        }
    }

    // =================== SIMPLE TARGETS UI ===================
    [Serializable]
    public class TargetRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private bool _active = false;
        private decimal _r = 1m;
        private int _percent = 0;

        [Browsable(false)] public string Name { get; set; } = "";

        [DisplayName("Activo")]
        public bool Active
        {
            get => _active;
            set { if (_active == value) return; _active = value; OnChanged(nameof(Active)); }
        }

        [DisplayName("R")]
        public decimal R
        {
            get => _r;
            set { if (_r == value) return; _r = value; OnChanged(nameof(R)); }
        }

        [DisplayName("Tanto % de cobro al cerrar")]
        public int Percent
        {
            get => _percent;
            set { if (_percent == value) return; _percent = value; OnChanged(nameof(Percent)); }
        }
    }

    [Serializable]
    [TypeConverter(typeof(TargetsModelConverter))]
    public class TargetsModel
    {
        public event EventHandler Changed;
        void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        private TargetRow _tp1 = new TargetRow { Name = "TP1", Active = false, R = 1m, Percent = 0 };
        private TargetRow _tp2 = new TargetRow { Name = "TP2", Active = true, R = 2m, Percent = 50 };
        private TargetRow _tp3 = new TargetRow { Name = "TP3", Active = true, R = 3m, Percent = 50 };

        public TargetsModel()
        {
            WireRow(_tp1);
            WireRow(_tp2);
            WireRow(_tp3);
        }

        void WireRow(TargetRow r)
        {
            if (r != null) r.PropertyChanged += (_, __) => RaiseChanged();
        }

        [DisplayName("TP1")]
        public TargetRow TP1
        {
            get => _tp1;
            set
            {
                if (_tp1 != null) _tp1.PropertyChanged -= (_, __) => RaiseChanged();
                _tp1 = value ?? new TargetRow { Name = "TP1" };
                WireRow(_tp1);
                RaiseChanged();
            }
        }

        [DisplayName("TP2")]
        public TargetRow TP2
        {
            get => _tp2;
            set
            {
                if (_tp2 != null) _tp2.PropertyChanged -= (_, __) => RaiseChanged();
                _tp2 = value ?? new TargetRow { Name = "TP2" };
                WireRow(_tp2);
                RaiseChanged();
            }
        }

        [DisplayName("TP3")]
        public TargetRow TP3
        {
            get => _tp3;
            set
            {
                if (_tp3 != null) _tp3.PropertyChanged -= (_, __) => RaiseChanged();
                _tp3 = value ?? new TargetRow { Name = "TP3" };
                WireRow(_tp3);
                RaiseChanged();
            }
        }

        public static TargetsModel FromLegacy(int preset, decimal tp1, decimal tp2, decimal tp3, int sp1, int sp2, int sp3)
        {
            var m = new TargetsModel();
            m.TP1.Active = preset >= 1 && sp1 > 0; m.TP1.R = tp1; m.TP1.Percent = Math.Max(0, sp1);
            m.TP2.Active = preset >= 2 && sp2 > 0; m.TP2.R = tp2; m.TP2.Percent = Math.Max(0, sp2);
            m.TP3.Active = preset >= 3 && sp3 > 0; m.TP3.R = tp3; m.TP3.Percent = Math.Max(0, sp3);
            return m;
        }
        private IEnumerable<TargetRow> All() => new[] { TP1, TP2, TP3 };
        public string ToSummary()
        {
            var act = All().Where(r => r.Active && r.Percent > 0).OrderBy(r => r.R).ToList();
            if (act.Count == 0) return "â€” sin targets â€”";
            return string.Join(" | ", act.Select(a => $"{a.Percent}% @ {a.R}R"));
        }
        public void NormalizePercents()
        {
            var act = All().Where(r => r.Active && r.Percent > 0).ToList();
            if (act.Count == 0) return;
            var sum = act.Sum(a => a.Percent);
            if (sum == 100) return;
            for (int i = 0; i < act.Count; i++)
                act[i].Percent = (int)Math.Max(0, Math.Round(100m * act[i].Percent / Math.Max(1, sum)));
            var diff = 100 - act.Sum(a => a.Percent);
            act[^1].Percent = Math.Max(1, act[^1].Percent + diff);
        }
        public List<TargetRow> ActiveOrdered() =>
            All().Where(r => r.Active && r.Percent > 0).OrderBy(r => r.R).ToList();
    }

    public class TargetsModelConverter : TypeConverter
    {
        public override bool GetPropertiesSupported(ITypeDescriptorContext context) => true;
        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
            => TypeDescriptor.GetProperties(value, attributes, true);
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => false;

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is TargetsModel m) return m.ToSummary();
            return base.ConvertTo(context, culture, value, destinationType);
        }

        // Permitir cargar desde string (lo que guarda ATAS)
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
            {
                // Ejemplos admitidos:
                // "50% @ 1R | 50% @ 3R"
                // "TP1: 40% @ 0,8R | TP3: 60% @ 2.5R"
                var model = new TargetsModel();
                try
                {
                    var list = new List<(decimal R, int Pct)>();
                    var rx = new Regex(@"(?:TP\d\s*:)?\s*(\d{1,3})\s*%\s*@\s*([0-9]+(?:[.,][0-9]+)?)\s*R",
                                       RegexOptions.IgnoreCase);
                    foreach (Match m in rx.Matches(s))
                    {
                        if (!m.Success) continue;
                        var pct = Math.Max(0, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                        var rStr = m.Groups[2].Value.Replace(',', '.');
                        if (decimal.TryParse(rStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var rVal))
                            list.Add((rVal, pct));
                    }
                    // Fallback si no hay matches
                    if (list.Count == 0)
                    {
                        model.TP1.Active = true; model.TP1.R = 1m; model.TP1.Percent = 100;
                        model.TP2.Active = false; model.TP3.Active = false;
                        return model;
                    }
                    // Ordenar por R y rellenar TP1..TP3
                    var ordered = list.OrderBy(x => x.R).Take(3).ToList();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        var (rVal, pctVal) = ordered[i];
                        var row = new TargetRow { Active = pctVal > 0, R = rVal, Percent = pctVal, Name = $"TP{i+1}" };
                        if (i == 0) model.TP1 = row;
                        else if (i == 1) model.TP2 = row;
                        else model.TP3 = row;
                    }
                    model.NormalizePercents();
                    return model;
                }
                catch
                {
                    // Ãšltimo recurso: 100% @ 1R
                    model.TP1.Active = true; model.TP1.R = 1m; model.TP1.Percent = 100;
                    return model;
                }
            }
            return base.ConvertFrom(context, culture, value);
        }
    }

    public class TargetsEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;
        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            var edSvc = provider?.GetService(typeof(IWindowsFormsEditorService)) as IWindowsFormsEditorService;
            if (edSvc == null) return value;
            var model = value as TargetsModel ?? new TargetsModel();
            using (var form = new TargetsEditorForm(model))
            {
                if (edSvc.ShowDialog(form) == DialogResult.OK)
                    return form.ResultModel;
            }
            return value;
        }
    }

    public class TargetsEditorForm : Form
    {
        private DataGridView _grid;
        private Button _ok, _cancel, _normalize;
        private Button _preset_50_13, _preset_30_08_2, _preset_40_123, _preset_100_2;
        public TargetsModel ResultModel { get; private set; }
        public TargetsEditorForm(TargetsModel model)
        {
            Text = "Configurar Targets (R / %)";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Width = 640; Height = 360;

            var rows = new BindingList<TargetRow>(new List<TargetRow> {
                new TargetRow { Name="TP1", Active=model.TP1?.Active??false, R=model.TP1?.R??1m, Percent=model.TP1?.Percent??0 },
                new TargetRow { Name="TP2", Active=model.TP2?.Active??false, R=model.TP2?.R??2m, Percent=model.TP2?.Percent??0 },
                new TargetRow { Name="TP3", Active=model.TP3?.Active??false, R=model.TP3?.R??3m, Percent=model.TP3?.Percent??0 },
            });
            _grid = new DataGridView { Dock = DockStyle.Top, Height = 240, AutoGenerateColumns = false, AllowUserToAddRows = false };
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Active", HeaderText = "Activo", Width = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Nombre", Width = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "R", HeaderText = "R", Width = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Percent", HeaderText = "% a cerrar", Width = 120 });
            _grid.DataSource = new BindingSource { DataSource = rows };

            _normalize    = new Button { Text = "Normalizar %",         Left = 12,  Top = 250, Width = 120 };
            _preset_50_13 = new Button { Text = "50% @ 1R | 50% @ 3R",  Left = 142, Top = 250, Width = 180 };
            _preset_30_08_2 = new Button { Text = "30% @ 0.8R | 70% @ 2R", Left = 328, Top = 250, Width = 200 };
            _preset_40_123  = new Button { Text = "40% @1R | 30% @2R | 30% @3R", Left = 12, Top = 284, Width = 308 };
            _preset_100_2   = new Button { Text = "100% @ 2R",           Left = 328, Top = 284, Width = 120 };
            _ok = new Button { Text = "OK",        Left = 540, Top = 250, Width = 80, DialogResult = DialogResult.OK };
            _cancel = new Button { Text = "Cancelar", Left = 540, Top = 284, Width = 80, DialogResult = DialogResult.Cancel };

            _normalize.Click += (s, e) =>
            {
                var bs = (BindingSource)_grid.DataSource; var r = (BindingList<TargetRow>)bs.DataSource;
                var m = new TargetsModel { TP1 = r[0], TP2 = r[1], TP3 = r[2] };
                m.NormalizePercents();
                _grid.Refresh();
            };

            // Helpers de presets
            void ApplyPreset((decimal R, int P)[] tpl)
            {
                var bs = (BindingSource)_grid.DataSource;
                var r  = (BindingList<TargetRow>)bs.DataSource;
                // reset
                for (int i=0;i<3;i++){ r[i].Active=false; r[i].Percent=0; }
                for (int i=0;i<tpl.Length && i<3;i++)
                {
                    r[i].Active = tpl[i].P > 0;
                    r[i].R      = tpl[i].R;
                    r[i].Percent= tpl[i].P;
                }
                // normaliza a 100
                var act = r.Where(x=>x.Active && x.Percent>0).ToList();
                var sum = act.Sum(x=>x.Percent);
                if (sum!=100 && act.Count>0)
                {
                    for (int i=0;i<act.Count;i++)
                        act[i].Percent = (int)Math.Max(0, Math.Round(100m*act[i].Percent/Math.Max(1,sum)));
                    var diff = 100 - act.Sum(x=>x.Percent);
                    act[^1].Percent = Math.Max(1, act[^1].Percent + diff);
                }
                _grid.Refresh();
            }
            _preset_50_13.Click += (s, e)   => ApplyPreset(new[]{ (1.0m,50),(3.0m,50) });
            _preset_30_08_2.Click += (s, e) => ApplyPreset(new[]{ (0.8m,30),(2.0m,70) });
            _preset_40_123.Click += (s, e)  => ApplyPreset(new[]{ (1.0m,40),(2.0m,30),(3.0m,30) });
            _preset_100_2.Click += (s, e)   => ApplyPreset(new[]{ (2.0m,100) });

            Controls.AddRange(new Control[] { _grid, _normalize, _preset_50_13, _preset_30_08_2, _preset_40_123, _preset_100_2, _ok, _cancel });
            AcceptButton = _ok; CancelButton = _cancel;
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK)
                {
                    var bs = (BindingSource)_grid.DataSource; var r = (BindingList<TargetRow>)bs.DataSource;
                    var act = r.Where(x=>x.Active && x.Percent>0).ToList();
                    if (act.Count == 0) { MessageBox.Show("Activa al menos un target (>0%).", "ValidaciÃ³n", MessageBoxButtons.OK, MessageBoxIcon.Warning); e.Cancel = true; return; }
                    var sum = act.Sum(x=>x.Percent);
                    if (sum != 100) {
                        for (int i=0;i<act.Count;i++) act[i].Percent = (int)Math.Round(100m*act[i].Percent/Math.Max(1,sum));
                        var diff = 100 - act.Sum(x=>x.Percent); act[^1].Percent += diff;
                    }
                    ResultModel = new TargetsModel {
                        TP1 = new TargetRow { Name="TP1", Active=r[0].Active, R=r[0].R, Percent=r[0].Percent },
                        TP2 = new TargetRow { Name="TP2", Active=r[1].Active, R=r[1].R, Percent=r[1].Percent },
                        TP3 = new TargetRow { Name="TP3", Active=r[2].Active, R=r[2].R, Percent=r[2].Percent },
                    };
                }
            };
        }
    }

    // Nota: esqueleto "safe". No envÃƒÂ¯Ã‚Â¿Ã‚Â½a ni cancela ÃƒÂ¯Ã‚Â¿Ã‚Â½rdenes.
    public class RiskManagerManualStrategy : ChartStrategy
    {
        // =================== Activation ===================
        [Category("Activation"), DisplayName("Manage manual entries")]
        public bool ManageManualEntries { get; set; } = true;

        [Category("Activation"), DisplayName("Allow attach without net (fallback)")]
        public bool AllowAttachFallback { get; set; } = true;

        [Category("Activation"), DisplayName("Ignore orders with prefix")]
        public string IgnorePrefix { get; set; } = "468"; // no interferir con la 468

        [Category("Activation"), DisplayName("Owner prefix (this strategy)")]
        public string OwnerPrefix { get; set; } = "RM:";

        [Category("Activation"), DisplayName("Enforce manual qty on entry")]
        [Description("Si la entrada manual ejecuta menos contratos que el objetivo calculado por el RM, la estrategia enviarÃƒÆ’Ã‚Â¡ una orden a mercado por la diferencia (delta).")]
        public bool EnforceManualQty { get; set; } = true;

        [Category("Position Sizing"), DisplayName("Mode")]
        public RmSizingMode SizingMode { get; set; } = RmSizingMode.FixedRiskUSD;

        [Category("Position Sizing"), DisplayName("Manual qty")]
        [Description("Cantidad objetivo de la ESTRATEGIA. Si difiere de la qty del ChartTrader y 'Enforce manual qty' estÃƒÆ’Ã‚Â¡ activo, el RM ajustarÃƒÆ’Ã‚Â¡ con orden a mercado.")]
        public int ManualQty { get; set; } = 1;

        [Category("Position Sizing"), DisplayName("Risk per trade (USD)")]
        public decimal RiskPerTradeUsd { get; set; } = 100m;

        [Category("Position Sizing"), DisplayName("Risk % of account")]
        public decimal RiskPercentOfAccount { get; set; } = 0.5m;

        [Category("Position Sizing"), DisplayName("Default stop (ticks)")]
        public int DefaultStopTicks { get; set; } = 12;

        [Category("Position Sizing"), DisplayName("Fallback tick size")]
        public decimal FallbackTickSize { get; set; } = 0.25m;

        [Category("Position Sizing"), DisplayName("Fallback tick value (USD)")]
        public decimal FallbackTickValueUsd { get; set; } = 12.5m;

        [Category("Position Sizing"), DisplayName("Min qty")]
        public int MinQty { get; set; } = 1;

        [Category("Position Sizing"), DisplayName("Max qty")]
        public int MaxQty { get; set; } = 1000;

        [Category("Position Sizing"), DisplayName("Underfunded policy")]
        public MyAtas.Risk.Models.UnderfundedPolicy Underfunded { get; set; } =
            MyAtas.Risk.Models.UnderfundedPolicy.Min1;

        // === Snapshot de cuenta (solo lectura en UI) ===
        [Category("Position Sizing"), DisplayName("Account equity (USD)")]
        [System.ComponentModel.ReadOnly(true)]
        public decimal AccountEquitySnapshot { get; private set; } = 0m;

        private DateTime _nextEquityProbeAt = DateTime.MinValue;

        [Category("Position Sizing"), DisplayName("Account equity override (USD)")]
        public decimal AccountEquityOverride { get; set; } = 0m;

        // === Session P&L Tracking ===
        [Category("Position Sizing"), DisplayName("Include unrealized in Session P&L")]
        [Description("Si está activo, Session P&L incluye P&L no realizado (posición abierta). Si está inactivo, solo muestra P&L realizado.")]
        public bool IncludeUnrealizedInSession { get; set; } = true;

        [Category("Position Sizing"), DisplayName("Realized P&L (USD)")]
        [System.ComponentModel.ReadOnly(true)]
        [Description("P&L realizado (cerrado) desde que se activó la estrategia. Monótono, no 'baila'.")]
        public decimal SessionPnLRealized => _sessionRealizedPnL;

        [Category("Position Sizing"), DisplayName("Unrealized P&L (USD)")]
        [System.ComponentModel.ReadOnly(true)]
        [Description("P&L no realizado de la posición abierta actual. Este valor 'baila' con el mercado.")]
        public decimal SessionUnrealized { get; private set; } = 0m;

        [Category("Position Sizing"), DisplayName("Session P&L (USD)")]
        [System.ComponentModel.ReadOnly(true)]
        [Description("Total = Realized + Unrealized (si 'Include unrealized' está activo). Se resetea al desactivar.")]
        public decimal SessionPnL { get; private set; } = 0m;

        [Category("Position Sizing"), DisplayName("Tick value overrides (SYM=V;...)")]
        public string TickValueOverrides { get; set; } = "MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10";

        // =================== Stops & TPs ===================
        // ===== Tabla de Targets (UI simple) - SSOT =====
        private TargetsModel _targets;
        private bool _useTargetsV2 = false; // SSOT: si true, los legacy se ignoran como entrada de usuario

        [Category("Stops & TPs"), DisplayName("Targets (clic para editar)")]
        [Description("Configura T1..T3 como (R, %) y acciones asociadas. Abre un editor en tabla.")]
        [Editor(typeof(TargetsEditor), typeof(UITypeEditor))]
        public TargetsModel Targets
        {
            get
            {
                if (_targets == null)
                {
                    _targets = TargetsModel.FromLegacy(PresetTPs, TP1R, TP2R, TP3R, TP1pctunit, TP2pctunit, TP3pctunit);
                    WireTargetsEvents(_targets);
                }
                return _targets;
            }
            set
            {
                _targets = value ?? TargetsModel.FromLegacy(PresetTPs, TP1R, TP2R, TP3R, TP1pctunit, TP2pctunit, TP3pctunit);
                // engancha eventos y sincroniza legacy (por compatibilidad con guardados antiguos)
                WireTargetsEvents(_targets);
                ApplyTargetsToLegacyConfig();
                _useTargetsV2 = true;
                if (EnableLogging) DebugLog.W("RM/CFG", $"Targets set: {_targets.ToSummary()} (V2 active)");
            }
        }

        private void WireTargetsEvents(TargetsModel m)
        {
            if (m == null) return;
            m.Changed -= OnTargetsChanged;
            m.Changed += OnTargetsChanged;
        }

        private void OnTargetsChanged(object s, EventArgs e)
        {
            try
            {
                ApplyTargetsToLegacyConfig();
                _useTargetsV2 = true;
                if (EnableLogging)
                    DebugLog.W("RM/TARGETS", $"Synced from UI: {Targets.ToSummary()}");
            }
            catch { }
        }

        // === Helpers de Targets (V2-first) ===
        private (int preset, decimal r1, decimal r2, decimal r3, int p1, int p2, int p3, string src) GetTargetsSnapshot()
        {
            if (_targets != null)
            {
                // Leer SIEMPRE de TP1/TP2/TP3, incluso con % = 0 (no caer a legacy)
                var r1 = _targets.TP1?.R ?? 1m;
                var r2 = _targets.TP2?.R ?? 2m;
                var r3 = _targets.TP3?.R ?? 3m;
                var p1 = _targets.TP1?.Percent ?? 0;
                var p2 = _targets.TP2?.Percent ?? 0;
                var p3 = _targets.TP3?.Percent ?? 0;
                var preset = (p1 > 0 ? 1 : 0) + (p2 > 0 ? 1 : 0) + (p3 > 0 ? 1 : 0);
                return (preset, r1, r2, r3, p1, p2, p3, "TargetsV2");
            }
            // Fallback: legacy (solo si _targets == null)
            return (PresetTPs, TP1R, TP2R, TP3R, TP1pctunit, TP2pctunit, TP3pctunit, "Legacy");
        }

        private int ComputeVolatilityTicks(int window, RmVolMetric metric, decimal tickSize)
        {
            try
            {
                int w = Math.Clamp(window, 3, 200);
                int last = Math.Max(0, CurrentBar - 1);
                if (w <= 0 || tickSize <= 0) return 0;

                var vals = new List<decimal>(w);
                decimal prevClose = 0m;
                for (int i = last; i >= 0 && vals.Count < w; i--)
                {
                    var c = GetCandle(i);
                    if (c == null) break;
                    decimal hi = c.High, lo = c.Low, cl = c.Close;
                    decimal tr = metric == RmVolMetric.ATR && prevClose > 0m
                        ? Math.Max(hi - lo, Math.Max(Math.Abs(hi - prevClose), Math.Abs(prevClose - lo)))
                        : (hi - lo);
                    vals.Add(Math.Max(0m, tr));
                    prevClose = cl;
                }
                if (vals.Count == 0) return 0;

                decimal baseRange;
                switch (metric)
                {
                    case RmVolMetric.MedianRange:
                        vals.Sort();
                        baseRange = vals[vals.Count / 2];
                        break;
                    case RmVolMetric.AverageRange:
                        baseRange = vals.Average();
                        break;
                    default: // ATR
                        baseRange = vals.Average();
                        break;
                }
                var ticks = (int)Math.Max(1, Math.Round((baseRange / tickSize) * (VolFloorFactor <= 0 ? 1.0m : VolFloorFactor)));
                return ticks;
            }
            catch { return 0; }
        }

        private void EnforceVolatilityFloorIfNeeded(
            int dir, decimal entryPx, ref decimal? overrideStopPx, ref int approxStopTicks, decimal tickSize)
        {
            if (!VolFloorEnabled || tickSize <= 0m || overrideStopPx == null) return;

            // Â¿Se usÃ³ N porque no habÃ­a N-1 en el momento del fill?
            bool usedN = (_pendingAnchorBarAtFill >= 0 && _pendingPrevBarIdxAtFill >= 0)
                            ? _pendingAnchorBarAtFill != _pendingPrevBarIdxAtFill
                            : true; // si no tenemos tracking claro, tratar como "usÃ³ N"
            if (VolFloorOnlyIfNoPrev && !usedN) return;

            int curTicks = Math.Max(1, (int)Math.Round(Math.Abs(entryPx - overrideStopPx.Value) / tickSize));
            int minTicks = ComputeVolatilityTicks(VolFloorWindow, VolFloorMetric, tickSize);

            if (EnableLogging)
                DebugLog.W("RM/VOLFLOOR", $"CHECK: usedN={usedN} curTicks={curTicks} minTicks={minTicks} " +
                    $"window={VolFloorWindow} metric={VolFloorMetric} factor={VolFloorFactor}");

            if (minTicks <= 0 || curTicks >= minTicks) return;

            // Ensanchar desde la entrada hasta cumplir minTicks
            decimal newSl = entryPx + (dir > 0 ? -(minTicks * tickSize) : +(minTicks * tickSize));
            overrideStopPx = ShrinkPrice(newSl);
            approxStopTicks = minTicks;

            if (EnableLogging)
                DebugLog.W("RM/VOLFLOOR",
                    $"ENFORCED: curTicks={curTicks} â†’ minTicks={minTicks} newSL={overrideStopPx.Value:F2}");
        }

        [Browsable(false)] public int PresetTPs { get; set; } = 2;
        [Browsable(false)] public decimal TP1R { get; set; } = 1.0m;
        [Browsable(false)] public decimal TP2R { get; set; } = 2.0m;
        [Browsable(false)] public decimal TP3R { get; set; } = 3.0m;
        [Browsable(false)] public int TP1pctunit { get; set; } = 50;
        [Browsable(false)] public int TP2pctunit { get; set; } = 50;
        [Browsable(false)] public int TP3pctunit { get; set; } = 0;

        // === Stop placement ===
        [Category("Stops & TPs"), DisplayName("Stop placement mode")]
        [Description("ByTicks: usa 'Default stop (ticks)'. PrevBarOppositeExtreme: coloca el SL en el extremo opuesto de la vela N-1 (+offset).")]
        public RmStopPlacement StopPlacementMode { get; set; } = RmStopPlacement.PrevBarOppositeExtreme;

        [Category("Stops & TPs"), DisplayName("Prev-bar offset (ticks)")]
        [Description("Holgura aÃƒÆ'Ã‚Â±adida al extremo de la vela N-1 (1 = un tick mÃƒÆ'Ã‚Â¡s allÃƒÆ'Ã‚Â¡ del High/Low).")]
        public int PrevBarOffsetTicks { get; set; } = 3;

        [Category("Stops & TPs"), DisplayName("Prev-bar offset side")]
        [Description("Outside: fuera del extremo (mÃƒÆ’Ã‚Â¡s allÃƒÆ’Ã‚Â¡ del High/Low). Inside: dentro del rango de la vela.")]
        public RmPrevBarOffsetSide PrevBarOffsetSide { get; set; } = RmPrevBarOffsetSide.Outside;

        [Category("Stops & TPs"), DisplayName("N-1 debe coincidir con la direcciÃ³n de entrada")]
        [Description("Si activo, N-1 solo se usa si su color coincide con la direcciÃ³n (LONGâ†’verde, SHORTâ†’rojo). Evita usar barras contra-tendencia.")]
        public bool PrevBarMustMatchEntryDir { get; set; } = true;

        [Browsable(false)]
        public int CounterTrendExtraOffsetTicks { get; set; } = 0;

        // Grupo para offset extra en contra-tendencia (ancla N)
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public class CounterTrendGroup
        {
            private readonly RiskManagerManualStrategy _o;
            public CounterTrendGroup(RiskManagerManualStrategy o) => _o = o;

            [DisplayName("Offset adicional (ticks)")]
            [Description("Ticks extra aÃ±adidos al SL cuando se usa ancla N (entrada contra-tendencia).")]
            public int OffsetTicks
            {
                get => _o.CounterTrendExtraOffsetTicks;
                set => _o.CounterTrendExtraOffsetTicks = Math.Max(0, value);
            }
            public override string ToString() => OffsetTicks > 0 ? $"{OffsetTicks} ticks" : "sin extra";
        }

        private CounterTrendGroup _ctUI;
        [Category("Stops & TPs"), DisplayName("Contratendencia (ancla N)")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public CounterTrendGroup CounterTrend => _ctUI ??= new CounterTrendGroup(this);

        [Browsable(false)]
        public bool VolFloorEnabled { get; set; } = false;

        [Browsable(false)]
        public bool VolFloorOnlyIfNoPrev { get; set; } = true;

        [Browsable(false)]
        public int VolFloorWindow { get; set; } = 14;

        [Browsable(false)]
        public RmVolMetric VolFloorMetric { get; set; } = RmVolMetric.ATR;

        [Browsable(false)]
        public decimal VolFloorFactor { get; set; } = 1.0m;

        // ----- UI group para Volatility Floor -----
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public class VolFloorGroup
        {
            private readonly RiskManagerManualStrategy _o;
            public VolFloorGroup(RiskManagerManualStrategy owner) => _o = owner;

            [DisplayName("Activo")]
            public bool Activo
            {
                get => _o.VolFloorEnabled;
                set => _o.VolFloorEnabled = value;
            }

            [DisplayName("Solo si no hay N-1")]
            public bool SoloSiNoHayN1
            {
                get => _o.VolFloorOnlyIfNoPrev;
                set => _o.VolFloorOnlyIfNoPrev = value;
            }

            [DisplayName("Ventana (barras)")]
            public int VentanaBarras
            {
                get => _o.VolFloorWindow;
                set => _o.VolFloorWindow = value;
            }

            [DisplayName("MÃ©trica")]
            public RmVolMetric Metrica
            {
                get => _o.VolFloorMetric;
                set => _o.VolFloorMetric = value;
            }

            [DisplayName("Factor Ã—")]
            public decimal Factor
            {
                get => _o.VolFloorFactor;
                set => _o.VolFloorFactor = value;
            }

            public override string ToString()
                => Activo ? $"{Metrica} {VentanaBarras}Ã—{Factor}" : "Desactivado";
        }

        // Campo + propiedad expuesta para el PropertyGrid
        private VolFloorGroup _volFloorUI;
        [Category("Stops & TPs"), DisplayName("Ticks mÃ­nimos del Stop Loss del SL por volatilidad")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public VolFloorGroup VolatilityFloor => _volFloorUI ??= new VolFloorGroup(this);

        // =================== Breakeven ===================
        [Category("Breakeven"), DisplayName("Mode")]
        public RmBeMode BreakEvenMode { get; set; } = RmBeMode.OnTPTouch;

        [Category("Breakeven"), DisplayName("BE trigger TP (1..3)")]
        [Description("QuÃ© TP dispara el paso a breakeven (1, 2 o 3). Funciona tambiÃ©n en modo virtual sin TP real.")]
        public int BeTriggerTp { get; set; } = 1;

        [Category("Breakeven"), DisplayName("BE offset (ticks)")]
        public int BeOffsetTicks { get; set; } = 4;

        [Category("Breakeven"), DisplayName("Virtual BE")]
        public bool VirtualBreakEven { get; set; } = false;

        // =================== Trailing (placeholder) ===================
        [Category("Trailing"), DisplayName("Mode")]
        public RmTrailMode TrailingMode { get; set; } = RmTrailMode.Off;

        [Category("Trailing"), DisplayName("Distance (ticks)")]
        public int TrailDistanceTicks { get; set; } = 8;

        [Category("Trailing"), DisplayName("Confirm bars")]
        public int TrailConfirmBars { get; set; } = 1;

        // =================== Diagnostics ===================
        [Category("Diagnostics"), DisplayName("Enable logging")]
        public bool EnableLogging { get; set; } = true;

        // =================== Internal Helpers ===================
        private int _lastSeenBar = -1;

        // === Breakeven: estado mÃ­nimo requerido por ResetAttachState / gates ===
        private bool   _beArmed     = false;
        private bool   _beDone      = false;
        private decimal _beTargetPx = 0m;

        // === Breakeven helpers ===
        private int _beDirHint = 0; // +1/-1 al armar (por si net no estÃ¡ disponible aÃºn)

        // Tracking de extremos desde el momento de armar BE (evita usar datos histÃ³ricos de barra)
        private decimal _beArmedAtPrice = 0m;  // Precio cuando se armÃ³ BE (baseline)
        private decimal _beMaxReached = 0m;    // MÃ¡ximo alcanzado DESPUÃ‰S de armar BE

        // === TRAILING state ===
        private bool   _trailArmed = false;
        private int    _trailLastStepIdx = -1;       // -1 = ninguno
        private decimal _trailBaselinePx = 0m;
        private decimal _trailMaxReached = 0m;
        private decimal _trailMinReached = 0m;
        private int    _lastTrailMoveBar = -1;

        // === TRAILING: tracking extra ===
        // Último precio conocido del stop RM (lo actualizamos en OnOrderChanged y cuando lo movemos)
        private decimal _lastKnownStopPx = 0m;
        // Riesgo por contrato (|entry - stopInicial|). Se establece al colocarse el primer SL.
        private decimal _trailRiskAbs = 0m;

        // === TRAILING: logging helper
        private void TLog(string sub, string msg)
        {
            if (EnableLogging) DebugLog.W("RM/TRAIL/" + sub, msg);
        }

        private decimal _beMinReached = 0m;    // MÃ­nimo alcanzado DESPUÃ‰S de armar BE

        // === Session P&L tracking ===
        private decimal _sessionStartingEquity = 0m;  // Equity inicial al activar estrategia
        private decimal _currentPositionEntryPrice = 0m;  // Precio promedio de entrada actual
        private int _currentPositionQty = 0;  // Cantidad de posiciÃ³n actual (signed: +LONG / -SHORT)
        private decimal _sessionRealizedPnL = 0m;  // P&L realizado acumulado desde activaciÃ³n

        private decimal ComputeBePrice(int dir, decimal entryPx, decimal tickSize)
        {
            var off = Math.Max(0, BeOffsetTicks) * tickSize;
            return dir > 0 ? ShrinkPrice(entryPx + off) : ShrinkPrice(entryPx - off);
        }

        // Clamp de seguridad para el BE: nunca por el otro lado del Ãºltimo precio
        private decimal ClampBeTrigger(decimal proposed)
        {
            var last = GetLastPriceSafe();
            var ts = Security?.TickSize ?? FallbackTickSize;
            if (_currentPositionQty > 0)        // estamos largos â†’ stop es Sell Stop
                return Math.Min(proposed, last - ts); // jamÃ¡s por encima de last
            else if (_currentPositionQty < 0)   // estamos cortos â†’ stop es Buy Stop
                return Math.Max(proposed, last + ts); // jamÃ¡s por debajo de last
            return proposed;
        }

        // Mueve todos los SL a 'newStopPx' preservando TPs:
        // 1) intenta MODIFICAR in-place; 2) si no se puede, cancela SL+TP y recrea ambos.
        private void MoveAllRmStopsTo(decimal newStopPx, string reason = "BE")
        {
            try
            {
                _lastKnownStopPx = newStopPx;
                TLog("APPLY", $"MoveAllRmStopsTo newStop={newStopPx:F2} reason={reason}");
                var list = this.Orders;
                if (list == null)
                {
                    if (EnableLogging) DebugLog.W("RM/BE", "MoveAllRmStopsTo: Orders list is NULL");
                    return;
                }
                if (EnableLogging) DebugLog.W("RM/BE", $"MoveAllRmStopsTo: Orders count={list.Count()}");

                // Agrupar TPs y SLs por OCO
                var tpsByOco = new System.Collections.Generic.Dictionary<string, Order>();
                var slsByOco = new System.Collections.Generic.Dictionary<string, Order>();

                foreach (var o in list)
                {
                    if (o == null) continue;
                    var c = o.Comment ?? "";
                    var st = o.Status();
                    if (o.Canceled || st == OrderStatus.Canceled || st == OrderStatus.Filled) continue;

                    if (c.StartsWith(OwnerPrefix + "TP:"))
                        tpsByOco[o.OCOGroup ?? ""] = o;
                    else if (c.StartsWith(OwnerPrefix + "SL:"))
                        slsByOco[o.OCOGroup ?? ""] = o;
                }

                int replaced = 0, recreated = 0, modified = 0;
                foreach (var kv in tpsByOco)
                {
                    var oco = kv.Key;
                    if (!slsByOco.TryGetValue(oco, out var slOld)) continue;

                    var qty = Math.Max(0, slOld.QuantityToFill);
                    if (qty <= 0) continue;

                    // 1) MODIFICAR in-place si la API lo permite
                    if (TryModifyStopInPlace(slOld, newStopPx)) { modified++; continue; }

                    // 2) Fallback: cancelar SL+TP y recrear pareja completa
                    var tpOld = kv.Value;
                    var tpPx  = tpOld?.Price ?? 0m;
                    var tpQty = Math.Max(0, tpOld?.QuantityToFill ?? 0);
                    try { CancelOrder(slOld); } catch { }
                    try { if (tpOld != null) CancelOrder(tpOld); } catch { }

                    var side = slOld.Direction; // ya es la cara "cover" correcta
                    var slNew = new Order
                    {
                        Portfolio      = Portfolio,
                        Security       = Security,
                        Direction      = side,
                        Type           = OrderTypes.Stop,
                        TriggerPrice   = newStopPx,
                        QuantityToFill = qty,
                        OCOGroup       = oco,
                        IsAttached     = true,
                        Comment        = $"{OwnerPrefix}SL:{Guid.NewGuid():N}"
                    };
                    TrySetReduceOnly(slNew);
                    TrySetCloseOnTrigger(slNew);
                    OpenOrder(slNew);
                    replaced++;

                    if (tpQty > 0 && tpPx > 0m)
                    {
                        var tpNew = new Order
                        {
                            Portfolio      = Portfolio,
                            Security       = Security,
                            Direction      = side,
                            Type           = OrderTypes.Limit,
                            Price          = tpPx,
                            QuantityToFill = tpQty,
                            OCOGroup       = oco,
                            IsAttached     = true,
                            Comment        = $"{OwnerPrefix}TP:{Guid.NewGuid():N}"
                        };
                        TrySetReduceOnly(tpNew);
                        OpenOrder(tpNew);
                        recreated++;
                    }
                }

                // (NUEVO) Procesar SLs huérfanos: sin TP emparejado en el mismo OCO.
                if (EnableLogging && slsByOco.Count > 0)
                    DebugLog.W("RM/BE/ORPHAN", $"Checking orphan SLs: total slsByOco={slsByOco.Count} tpsByOco={tpsByOco.Count}");

                foreach (var kv in slsByOco)
                {
                    var oco = kv.Key;                    // puede ser "" (sin OCO)
                    if (tpsByOco.ContainsKey(oco))       // ya tratado arriba junto con su TP
                    {
                        if (EnableLogging)
                            DebugLog.W("RM/BE/ORPHAN", $"Skip SL with oco={oco ?? "<empty>"} (has TP paired)");
                        continue;
                    }

                    var slOld = kv.Value;
                    var qty   = Math.Max(0, slOld.QuantityToFill);
                    if (EnableLogging)
                        DebugLog.W("RM/BE/ORPHAN", $"Found orphan SL: oco={oco ?? "<empty>"} qty={qty} status={slOld.Status()} comment={slOld.Comment}");

                    if (qty <= 0)
                    {
                        if (EnableLogging)
                            DebugLog.W("RM/BE/ORPHAN", $"Skip orphan SL (qty<=0)");
                        continue;
                    }

                    // 1) Intentar modificación in-place
                    if (TryModifyStopInPlace(slOld, newStopPx)) { modified++; continue; }

                    // 2) Fallback: cancelar y recrear SOLO el SL (no hay TP asociado)
                    try { CancelOrder(slOld); } catch {}
                    var side = slOld.Direction; // Buy para cubrir short, Sell para cubrir long
                    var slNew = new Order
                    {
                        Portfolio      = Portfolio,
                        Security       = Security,
                        Direction      = side,
                        Type           = OrderTypes.Stop,
                        TriggerPrice   = newStopPx,
                        QuantityToFill = qty,
                        OCOGroup       = oco,            // mantiene su (posible) OCO
                        IsAttached     = true,
                        Comment        = $"{OwnerPrefix}SL:{Guid.NewGuid():N}"
                    };
                    TrySetReduceOnly(slNew);
                    TrySetCloseOnTrigger(slNew);
                    OpenOrder(slNew);
                    replaced++;
                }

                if (EnableLogging)
                    DebugLog.W("RM/BE", $"Moved SLs to {newStopPx:F2} (modified={modified} replaced={replaced} tpRecreated={recreated}) reason={reason}");

                // Rate-limit: evitar que BE y Trailing muevan el stop múltiples veces en la misma barra
                _lastTrailMoveBar = CurrentBar;
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/BE", $"MoveAllRmStopsTo EX: {ex.Message}");
            }
        }

        // Intenta modificar un STOP existente sin tocar su OCO (evita matar el TP).
        private bool TryModifyStopInPlace(Order stopOrder, decimal newStopPx)
        {
            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"TryModifyStopInPlace ENTER: order={stopOrder?.Comment} oldTrigger={stopOrder?.TriggerPrice:F2} newTrigger={newStopPx:F2}");
            try
            {
                var tm = this.TradingManager;
                if (tm == null)
                {
                    if (EnableLogging) DebugLog.W("RM/BE/MOD", "TradingManager is NULL â†’ return false");
                    return false;
                }

                var tmt = tm.GetType();
                if (EnableLogging) DebugLog.W("RM/BE/MOD", $"TradingManager type: {tmt.Name}");

                // Escanear todos los mÃ©todos disponibles
                var allMethods = tmt.GetMethods();
                if (EnableLogging) DebugLog.W("RM/BE/MOD", $"TradingManager has {allMethods.Length} total methods");

                foreach (var name in new[] { "ModifyOrder", "ChangeOrder", "UpdateOrder" })
                {
                    if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Searching for method: {name}");
                    var matches = allMethods.Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Found {matches.Count} methods matching '{name}'");

                    foreach (var mi in matches)
                    {
                        var ps = mi.GetParameters();
                        if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Method {mi.Name} has {ps.Length} parameters: {string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"))}");

                        // Firma simple: (Order, decimal triggerPrice)
                        if (ps.Length == 2 && ps[0].ParameterType.IsAssignableFrom(stopOrder.GetType()) && ps[1].ParameterType == typeof(decimal))
                        {
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"MATCH: Simple signature (Order, decimal) â†’ invoking {mi.Name}");
                            var result = mi.Invoke(tm, new object[] { stopOrder, newStopPx });
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Invocation result: {result} â†’ return true");
                            return true;
                        }

                        // Firma multi-parÃ¡metro: (Order oldOrder, Order newOrder, bool, bool)
                        if (ps.Length >= 2 && ps.Length <= 6
                            && ps[0].ParameterType.IsAssignableFrom(stopOrder.GetType())
                            && ps[1].ParameterType.IsAssignableFrom(stopOrder.GetType()))
                        {
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"MATCH: (Order, Order, ...) signature â†’ creating modified order");

                            // Crear orden modificada clonando la original
                            var modifiedOrder = new Order
                            {
                                Portfolio      = stopOrder.Portfolio,
                                Security       = stopOrder.Security,
                                Direction      = stopOrder.Direction,
                                Type           = stopOrder.Type,
                                TriggerPrice   = newStopPx,  // â† AQUÃ EL NUEVO TRIGGER
                                Price          = stopOrder.Price,
                                QuantityToFill = stopOrder.QuantityToFill,
                                OCOGroup       = stopOrder.OCOGroup,
                                IsAttached     = stopOrder.IsAttached,
                                Comment        = stopOrder.Comment
                            };

                            // Intentar copiar ReduceOnly si estÃ¡ disponible
                            TrySetReduceOnly(modifiedOrder);
                            TrySetCloseOnTrigger(modifiedOrder);

                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Created modified order: trigger {stopOrder.TriggerPrice:F2} â†’ {newStopPx:F2}");

                            var args = new object[ps.Length];
                            args[0] = stopOrder;       // oldOrder
                            args[1] = modifiedOrder;   // newOrder con nuevo TriggerPrice
                            for (int i = 2; i < ps.Length; i++)
                            {
                                args[i] = ps[i].ParameterType == typeof(bool) ? true
                                       : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                            }

                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Args: oldOrder={stopOrder.Comment} newTrigger={newStopPx:F2} + {ps.Length-2} bools");
                            var result = mi.Invoke(tm, args);
                            if (EnableLogging) DebugLog.W("RM/BE/MOD", $"Invocation result: {result} â†’ return true");
                            return true;
                        }
                    }
                }

                if (EnableLogging) DebugLog.W("RM/BE/MOD", "NO modify methods found â†’ return false (will use cancel+recreate)");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/BE/MOD", $"EXCEPTION: {ex.GetType().Name} - {ex.Message}");
                if (EnableLogging && ex.InnerException != null) DebugLog.W("RM/BE/MOD", $"INNER: {ex.InnerException.Message}");
            }
            return false;
        }

        private RiskEngine _engine;

        // =================== Stop-to-Flat (RM Close) ===================
        // Cuando el usuario pulsa el botÃƒÆ’Ã‚Â³n rojo de ATAS (Stop Strategy),
        // queremos: cancelar brackets propios y hacer FLATTEN de la posiciÃƒÆ’Ã‚Â³n.
        private bool _stopToFlat = false;
        private DateTime _rmStopGraceUntil = DateTime.MinValue;     // mientras now<=esto, estamos drenando cancel/fill
        private const int _rmStopGraceMs = 900;                     // holgura post-cancel/flatten (antes 2200)
        private DateTime _nextStopSweepAt = DateTime.MinValue;
        private const int _stopSweepEveryMs = 250;                  // sweep periÃƒÆ’Ã‚Â³dico durante el stop

        // ==== Diagnostics / Build stamp ====
        private const string BuildStamp = "RM.Manual/stop-to-flat 2025-09-30T22:00Z";

        // ==== Post-Close grace & timeouts ====
        private DateTime _postCloseUntil = DateTime.MinValue; // if now <= this ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ inGrace
        private readonly int _postCloseGraceMs = 2200;        // un poco mÃƒÆ’Ã‚Â¡s de holgura tras Close
        private readonly int _cleanupWaitMs = 150;            // wait before aggressive cleanup (ms) (antes 300)
        private readonly int _maxRetryMs = 2000;              // absolute escape from WAIT (ms)

        // ==== State snapshots for external-close detection ====
        private bool _hadRmBracketsPrevTick = false;          // were there RM brackets last tick?
        private int  _prevNet = 0;                            // last net position snapshot
        private DateTime _lastExternalCloseAt = DateTime.MinValue;
        private const int ExternalCloseDebounceMs = 600;      // antes 1500

        // ==== Attach protection ====
        private DateTime _lastAttachArmAt = DateTime.MinValue;
        private const int AttachProtectMs = 400;              // proteger el attach mÃƒÆ’Ã‚Â¡s tiempo (antes 1200)

        // --- Estado de net para detectar 0ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ÃƒÂ¢Ã¢â‚¬Â° 0 (entrada) ---
        private bool _pendingAttach = false;
        private decimal _pendingEntryPrice = 0m;
        private DateTime _pendingSince = DateTime.MinValue;
        private int _pendingDirHint = 0;                 // +1/-1 si logramos leerlo del Order
        private int _pendingFillQty = 0;                 // qty del fill manual (si la API lo expone)
        private readonly int _attachThrottleMs = 80;     // consolidaciÃƒÆ’Ã‚Â³n mÃƒÆ’Ã‚Â­nima (antes 200)
        private readonly int _attachDeadlineMs = 90;     // fallback rÃƒÆ’Ã‚Â¡pido si el net no llega (antes 120)
        private readonly System.Collections.Generic.List<Order> _liveOrders = new();
        private readonly object _liveOrdersLock = new();
        // Ancla estructural capturada en el fill
        private int _pendingPrevBarIdxAtFill = -1;
        private decimal _pendingAnchorHigh = 0m;
        private decimal _pendingAnchorLow  = 0m;
        private int _pendingAnchorBarAtFill = -1;

        // Helper property for compatibility
        private bool IsActivated => ManageManualEntries;

        private void ResetAttachState(string reason = "")
        {
            _pendingAttach = false;
            _pendingEntryPrice = 0m;
            _pendingPrevBarIdxAtFill = -1;
            _pendingAnchorBarAtFill = -1;
            _pendingAnchorHigh = _pendingAnchorLow = 0m;
            _pendingFillQty = 0;
            _beArmed = false; _beDone = false; _beTargetPx = 0m;
            _beArmedAtPrice = _beMaxReached = _beMinReached = 0m;  // Limpiar tracking BE
            _trailArmed = false;
            _trailLastStepIdx = -1;
            _trailBaselinePx = _trailMaxReached = _trailMinReached = 0m;
            _lastTrailMoveBar = -1;
            _lastKnownStopPx = 0m;
            _trailRiskAbs = 0m;
            if (EnableLogging) DebugLog.W("RM/GATE", $"ResetAttachState: {reason}");
        }

        // Constructor explÃƒÆ’Ã‚Â­cito para evitar excepciones durante carga ATAS
        public RiskManagerManualStrategy()
        {
            try
            {
                // No inicializar aquÃƒÆ’Ã‚Â­ para evitar problemas de carga
                _engine = null;
                _targets = TargetsModel.FromLegacy(PresetTPs, TP1R, TP2R, TP3R, TP1pctunit, TP2pctunit, TP3pctunit);
            }
            catch
            {
                // Constructor sin excepciones para ATAS
            }
        }

        private void ApplyTargetsToLegacyConfig()
        {
            var act = (_targets ?? new TargetsModel()).ActiveOrdered();
            // Fallback si vacÃ­o: 100% @ 1R
            if (act.Count == 0) { PresetTPs = 1; TP1R = 1m; TP1pctunit = 100; TP2pctunit = TP3pctunit = 0; _useTargetsV2 = true; return; }

            PresetTPs = Math.Clamp(act.Count, 1, 3);
            // R multipliers
            TP1R = act.ElementAtOrDefault(0)?.R ?? 1m;
            TP2R = act.ElementAtOrDefault(1)?.R ?? 0m;
            TP3R = act.ElementAtOrDefault(2)?.R ?? 0m;
            // Splits (%)
            TP1pctunit = act.ElementAtOrDefault(0)?.Percent ?? 0;
            TP2pctunit = act.ElementAtOrDefault(1)?.Percent ?? 0;
            TP3pctunit = act.ElementAtOrDefault(2)?.Percent ?? 0;
            // Normaliza a 100 por si acaso (Ãºltimo absorbe)
            var sum = TP1pctunit + TP2pctunit + TP3pctunit;
            if (sum != 100)
            {
                var list = new List<int> { TP1pctunit, TP2pctunit, TP3pctunit }.Take(PresetTPs).ToList();
                for (int i = 0; i < list.Count; i++) list[i] = (int)Math.Max(0, Math.Round(100m * list[i] / Math.Max(1, sum)));
                var diff = 100 - list.Sum(); list[^1] = Math.Max(1, list[^1] + diff);
                TP1pctunit = list.ElementAtOrDefault(0);
                TP2pctunit = PresetTPs >= 2 ? list.ElementAtOrDefault(1) : 0;
                TP3pctunit = PresetTPs >= 3 ? list.ElementAtOrDefault(2) : 0;
            }
            // Legacy OFF si usamos Targets: evita solapes en constructores que aÃºn lean legacy
            _useTargetsV2 = true;

            if (EnableLogging)
                DebugLog.W("RM/CFG", $"Targets configured: {(_targets ?? new TargetsModel()).ToSummary()}");
        }

        private RiskEngine GetEngine()
        {
            if (_engine == null)
            {
                try
                {
                    _engine = new RiskEngine();
                }
                catch
                {
                    // Fallback seguro
                    _engine = null;
                }
            }
            return _engine;
        }

        private bool IsFirstTickOf(int currentBar)
        {
            if (currentBar != _lastSeenBar) { _lastSeenBar = currentBar; return true; }
            return false;
        }

        // Lee net de forma robusta priorizando TradingManager.Position (net de CUENTA)
        private int ReadNetPosition()
        {
            var snap = ReadPositionSnapshot();
            return snap.NetQty;
        }

        private (int NetQty, decimal AvgPrice) ReadPositionSnapshot()
        {
            try
            {
                if (Portfolio == null || Security == null)
                    return (0, 0m);

                // 0) PRIMERO: TradingManager.Position (CUENTA seleccionada)
                try
                {
                    var tm = this.TradingManager;
                    var tmPos = tm?.GetType().GetProperty("Position")?.GetValue(tm);
                    if (tmPos != null)
                    {
                        int netQty = 0; decimal avgPrice = 0m;
                        foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                        {
                            var p = tmPos.GetType().GetProperty(name);
                            if (p != null)
                            {
                                var v = p.GetValue(tmPos);
                                if (v != null) { netQty = Convert.ToInt32(v); break; }
                            }
                        }
                        foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                        {
                            var p = tmPos.GetType().GetProperty(name);
                            if (p != null)
                            {
                                var v = p.GetValue(tmPos);
                                if (v != null) { avgPrice = Convert.ToDecimal(v); if (avgPrice > 0m) break; }
                            }
                        }
                        if (EnableLogging)
                            DebugLog.W("RM/SNAP", $"TM.Position net={netQty} avg={avgPrice:F2}");
                        return (netQty, avgPrice);
                    }
                } catch { /* fallback */ }

                // 1) Portfolio.GetPosition(Security)
                try
                {
                    var getPos = Portfolio.GetType().GetMethod("GetPosition", new[] { Security.GetType() });
                    if (getPos != null)
                    {
                        var pos = getPos.Invoke(Portfolio, new object[] { Security });
                        if (pos != null)
                        {
                            var netQty = 0;
                            var avgPrice = 0m;

                            // Leer Net/Amount/Qty/Position
                            foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                            {
                                var p = pos.GetType().GetProperty(name);
                                if (p != null)
                                {
                                    var v = p.GetValue(pos);
                                    if (v != null)
                                    {
                                        netQty = Convert.ToInt32(v);
                                        break;
                                    }
                                }
                            }

                            // Leer AvgPrice/AveragePrice/EntryPrice/Price
                            foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                            {
                                var p = pos.GetType().GetProperty(name);
                                if (p != null)
                                {
                                    var v = p.GetValue(pos);
                                    if (v != null)
                                    {
                                        avgPrice = Convert.ToDecimal(v);
                                        if (avgPrice > 0m) break;
                                    }
                                }
                            }

                            if (EnableLogging && (netQty != 0 || avgPrice > 0m))
                                DebugLog.W("RM/SNAP", $"pos avgPrice={avgPrice:F2} net={netQty} (source=Portfolio.GetPosition)");
                            return (netQty, avgPrice);
                        }
                    }
                }
                catch { /* seguir al fallback */ }

                // 2) Iterar Portfolio.Positions
                try
                {
                    var positionsProp = Portfolio.GetType().GetProperty("Positions");
                    var positions = positionsProp?.GetValue(Portfolio) as System.Collections.IEnumerable;
                    if (positions != null)
                    {
                        foreach (var pos in positions)
                        {
                            var secProp = pos.GetType().GetProperty("Security");
                            var secStr = secProp?.GetValue(pos)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(secStr) && (Security?.ToString() ?? "") == secStr)
                            {
                                var netQty = 0;
                                var avgPrice = 0m;

                                // Leer Net/Amount/Qty/Position
                                foreach (var name in new[] { "Net", "Amount", "Qty", "Position" })
                                {
                                    var p = pos.GetType().GetProperty(name);
                                    if (p != null)
                                    {
                                        var v = p.GetValue(pos);
                                        if (v != null)
                                        {
                                            netQty = Convert.ToInt32(v);
                                            break;
                                        }
                                    }
                                }

                                // Leer AvgPrice/AveragePrice/EntryPrice/Price
                                foreach (var name in new[] { "AveragePrice", "AvgPrice", "EntryPrice", "Price" })
                                {
                                    var p = pos.GetType().GetProperty(name);
                                    if (p != null)
                                    {
                                        var v = p.GetValue(pos);
                                        if (v != null)
                                        {
                                            avgPrice = Convert.ToDecimal(v);
                                            if (avgPrice > 0m) break;
                                        }
                                    }
                                }

                                if (EnableLogging && (netQty != 0 || avgPrice > 0m))
                                    DebugLog.W("RM/SNAP", $"pos avgPrice={avgPrice:F2} net={netQty} (source=Portfolio.Positions)");
                                return (netQty, avgPrice);
                            }
                        }
                    }
                }
                catch { /* devolver valores por defecto */ }
            }
            catch { }
            return (0, 0m);
        }

        // Sondea la cuenta real (TradingManager.Account / Portfolio) por reflexiÃ³n.
        // Busca en orden: Equity, NetLiquidation, Balance, AccountBalance, Cash.
        // Devuelve el primer valor > 0m encontrado.
        private decimal ReadAccountEquityUSD()
        {
            try
            {
                // 1) TradingManager.Account.* (Equity/Balance/NetLiquidation/Cash)
                var tm = this.TradingManager;
                var acct = tm?.GetType().GetProperty("Account")?.GetValue(tm);
                if (acct != null)
                {
                    foreach (var name in new[] { "Equity", "NetLiquidation", "Balance", "AccountBalance", "Cash" })
                    {
                        var p = acct.GetType().GetProperty(name);
                        if (p == null) continue;
                        try
                        {
                            var v = Convert.ToDecimal(p.GetValue(acct));
                            if (v > 0m) return v;
                        }
                        catch { }
                    }
                }
                // 2) Portfolio.* (Equity/Balance/Cash)
                if (Portfolio != null)
                {
                    foreach (var name in new[] { "Equity", "Balance", "Cash", "AccountBalance" })
                    {
                        var p = Portfolio.GetType().GetProperty(name);
                        if (p == null) continue;
                        try
                        {
                            var v = Convert.ToDecimal(p.GetValue(Portfolio));
                            if (v > 0m) return v;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return 0m;
        }

        // Normaliza cadenas para comparar (mayÃºsculas, sin espacios/guiones, sin acentos)
        private static string K(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            s = s.ToUpperInvariant();
            s = s.Replace(" ", "").Replace("-", "").Replace("_", "");
            s = s.Replace("Ã‰","E").Replace("Ã","A").Replace("Ã","I").Replace("Ã“","O").Replace("Ãš","U").Replace("Ã‘","N");
            return s;
        }

        private decimal ResolveTickValueUsd(string securityNameOrCode, string overrides, decimal fallback)
        {
            var key = K(securityNameOrCode);

            // 1) Overrides de la UI: "MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10;MICROEMININASDAQ100=0.5"
            if (!string.IsNullOrWhiteSpace(overrides))
            {
                foreach (var pair in overrides.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=');
                    if (kv.Length != 2) continue;
                    var k = K(kv[0]);
                    var vRaw = kv[1].Trim().Replace(',', '.');
                    if (!decimal.TryParse(vRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) continue;

                    // Coincidencia amplia: si el nombre/cÃ³digo contiene la clave normalizada
                    if (key.Contains(k)) return v;
                }
            }

            // 2) HeurÃ­sticas por si el broker no da el cÃ³digo del sÃ­mbolo
            if (key.Contains("MICROEMININASDAQ") || key.StartsWith("MNQ")) return 0.5m;
            if (key.StartsWith("NQ")) return 5m;
            if (key.StartsWith("MES")) return 1.25m;
            if (key.StartsWith("ES")) return 12.5m;
            if (key.Contains("MICROGOLD") || key.StartsWith("MGC")) return 1m;
            if (key.StartsWith("GC")) return 10m;

            return fallback;
        }

        private decimal ResolveTickValueUSD()
        {
            return ResolveTickValueUsd(Security?.ToString() ?? "", TickValueOverrides ?? "", FallbackTickValueUsd);
        }

        // Devuelve el objeto Position nativo de ATAS (para usar ClosePosition del TradingManager).
        private object GetAtasPositionObject()
        {
            try
            {
                if (Portfolio == null || Security == null) return null;
                var getPos = Portfolio.GetType().GetMethod("GetPosition", new[] { Security.GetType() });
                if (getPos != null)
                {
                    var pos = getPos.Invoke(Portfolio, new object[] { Security });
                    return pos; // puede ser null si no hay posiciÃƒÆ’Ã‚Â³n
                }
            }
            catch { }
            return null;
        }

        private decimal ExtractAvgFillPrice(Order order)
        {
            foreach (var name in new[] { "AvgPrice", "AveragePrice", "AvgFillPrice", "Price" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = Convert.ToDecimal(p.GetValue(order));
                    if (v > 0m) return v;
                }
                catch { }
            }
            return 0m; // NADA de GetCandle() aquÃƒÆ’Ã‚Â­
        }

        private int ExtractFilledQty(Order order)
        {
            foreach (var name in new[] { "Filled", "FilledQuantity", "Quantity", "QuantityToFill", "Volume", "Lots" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = p.GetValue(order);
                    if (v == null) continue;
                    var q = Convert.ToInt32(v);
                    if (q > 0) return q;
                }
                catch { }
            }
            return 0;
        }

        private int ExtractDirFromOrder(Order order)
        {
            foreach (var name in new[] { "Direction", "Side", "OrderDirection", "OrderSide", "TradeSide" })
            {
                try
                {
                    var p = order.GetType().GetProperty(name);
                    if (p == null) continue;
                    var s = p.GetValue(order)?.ToString() ?? "";
                    if (s.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0) return +1;
                    if (s.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0) return -1;
                }
                catch { }
            }
            return 0;
        }

        /// <summary>
        /// Obtiene el R múltiple del TP configurado para trigger del BE desde el modelo Targets (UI nueva).
        /// NO depende del % de cobro: aunque sea 0 (TP virtual), devuelve el R correcto.
        /// </summary>
        private decimal GetBeRMultipleFromTargets()
        {
            try
            {
                var tgt = Targets; // Lee desde UI "Targets (clic para editar)"
                if (tgt == null) return 1m; // Fallback seguro

                // Seleccionar TP según BeTriggerTp (1, 2 o 3)
                var row = BeTriggerTp switch
                {
                    1 => tgt.TP1,
                    2 => tgt.TP2,
                    3 => tgt.TP3,
                    _ => tgt.TP1 // Fallback a TP1
                };

                var r = row?.R ?? 0m;
                return r > 0m ? r : 1m; // Fallback si R inválido
            }
            catch
            {
                return 1m; // Fallback en caso de error
            }
        }


        protected override void OnCalculate(int bar, decimal value)
        {
            if (!IsActivated) return;

            // Refresca equity en UI (1Ã—/s)
            if (DateTime.UtcNow >= _nextEquityProbeAt)
            {
                AccountEquitySnapshot = ReadAccountEquityUSD();
                _nextEquityProbeAt = DateTime.UtcNow.AddSeconds(1);
                if (EnableLogging) DebugLog.W("RM/SNAP", $"Equityâ‰ˆ{AccountEquitySnapshot:F2} USD");

                // Inicializar session starting equity la primera vez
                if (_sessionStartingEquity == 0m)
                {
                    // Priorizar AccountEquityOverride si estÃ¡ configurado
                    if (AccountEquityOverride > 0m)
                    {
                        _sessionStartingEquity = AccountEquityOverride;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Session started with OVERRIDE equity: {_sessionStartingEquity:F2} USD");
                    }
                    else if (AccountEquitySnapshot > 0m)
                    {
                        _sessionStartingEquity = AccountEquitySnapshot;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Session started with detected equity: {_sessionStartingEquity:F2} USD");
                    }
                    else
                    {
                        // Ãšltimo fallback: usar 10000 USD como base
                        _sessionStartingEquity = 10000m;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Session started with DEFAULT equity (no account data): {_sessionStartingEquity:F2} USD");
                    }
                }

                // Actualizar Session P&L = Realized + Unrealized
                UpdateSessionPnL();
            }

            // === TRAILING: acumular extremos desde el armado ===
            if (_trailArmed)
            {
                try
                {
                    var current = SafeGetCandle(bar);
                    if (current != null)
                    {
                        if (_trailMaxReached == 0m) _trailMaxReached = current.Close;
                        if (_trailMinReached == 0m) _trailMinReached  = current.Close;
                        if (current.High > _trailMaxReached) _trailMaxReached = current.High;
                        if (current.Low  < _trailMinReached) _trailMinReached  = current.Low;
                        TLog("TRACE", $"hi={current.High:F2} lo={current.Low:F2} c={current.Close:F2} " +
                                    $"max={_trailMaxReached:F2} min={_trailMinReached:F2}");
                    }
                }
                catch { }
            }

            // Heartbeat del estado de Stop-to-Flat (visible en logs)
            if (EnableLogging && IsFirstTickOf(bar))
            {
                var now = DateTime.UtcNow;
                DebugLog.W("RM/HEARTBEAT",
                    $"bar={bar} t={GetCandle(bar).Time:HH:mm:ss} mode={SizingMode} be={BreakEvenMode} trail={TrailingMode} build={BuildStamp} graceUntil={_postCloseUntil:HH:mm:ss.fff} inGrace={(now <= _postCloseUntil)}");
                DebugLog.W("RM/STOP", $"tick={bar} inStop={_stopToFlat} stopGraceUntil={_rmStopGraceUntil:HH:mm:ss.fff} inGrace={(now <= _rmStopGraceUntil)}");
            }

            // Si estamos parando (Stop-to-Flat), no armes/adjuntes brackets nuevos
            if (_stopToFlat)
            {
                // Limpieza pasiva mientras drenamos: reporta si siguen vivos SL/TP
                var flat = Math.Abs(ReadNetPositionSafe()) == 0;
                var live = HasLiveRmBrackets();
                if (EnableLogging)
                    DebugLog.W("RM/STOP", $"drain: flat={flat} liveBrackets={live} inGrace={(DateTime.UtcNow <= _rmStopGraceUntil)}");

                // Barrido periÃƒÆ’Ã‚Â³dico durante el stop: re-cancelar y re-flatten si hace falta (sin duplicar)
                var now = DateTime.UtcNow;
                if (now >= _nextStopSweepAt && now <= _rmStopGraceUntil)
                {
                    CancelNonBracketWorkingOrders("stop-sweep");
                    // reintento preferente: flatten nativo
                    var closedAgain = TryClosePositionViaTradingManager();
                    if (!closedAgain)
                        EnsureFlattenOutstanding("stop-sweep");
                    _nextStopSweepAt = now.AddMilliseconds(_stopSweepEveryMs);
                }
                // No retornamos de OnCalculate global: simplemente dejamos que no se dispare TryAttachBracketsNow()
            }

            // Fallback por barra si quedÃƒÆ’Ã‚Â³ pendiente
            if (_pendingAttach && (DateTime.UtcNow - _pendingSince).TotalMilliseconds >= _attachThrottleMs)
                TryAttachBracketsNow();

            // === Net & external-close detection (with attach protection) ===
            try
            {
                var currentNet = ReadNetPosition();
                var isFlat = Math.Abs(currentNet) == 0;
                var hadBrNow = HasLiveRmBrackets();

                bool transitionClose = (_prevNet != 0 && currentNet == 0);
                bool bracketsEdgeClose = (_hadRmBracketsPrevTick && !hadBrNow && isFlat);
                bool recentAttach = _pendingAttach && (DateTime.UtcNow - _lastAttachArmAt).TotalMilliseconds < AttachProtectMs;
                bool debounce = (DateTime.UtcNow - _lastExternalCloseAt).TotalMilliseconds < ExternalCloseDebounceMs;

                if ((transitionClose || bracketsEdgeClose) && !debounce)
                {
                    CancelResidualBrackets("external close detected");
                    if (!recentAttach) _pendingAttach = false; // <- NO matar attach reciÃƒÆ’Ã‚Â©n armado
                    _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                    _lastExternalCloseAt = DateTime.UtcNow;
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", $"External close ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ grace until={_postCloseUntil:HH:mm:ss.fff}, recentAttach={recentAttach}");
                }

                // Update prev snapshots for next tick
                _hadRmBracketsPrevTick = hadBrNow;
                _prevNet = currentNet;
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"OnCalculate net/check EX: {ex.Message}");
            }

            // 4) Limpia solo si realmente estamos "idle" (sin attach armado/BE ni brackets) con hysteresis
            try
            {
                var net = Math.Abs(ReadNetPosition());
                var hasLive = HasLiveRmBrackets(includeNone: true);
                var justArmed = _pendingAttach && (DateTime.UtcNow - _lastAttachArmAt).TotalMilliseconds < (AttachProtectMs + 400);
                // No limpies si hay BE armado: evita desarmarlo por snapshots 0
                if (!_beArmed && net == 0 && !hasLive && !justArmed)
                {
                    ResetAttachState("flat idle");
                    if (_postCloseUntil < DateTime.UtcNow)
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(250);
                }
            }
            catch { /* defensivo */ }

            // === BE por TOUCH de precio (funciona con TP real o virtual) ===
            try
            {
                if (_beArmed && !_beDone && BreakEvenMode == RmBeMode.OnTPTouch)
                {
                    var snap = ReadPositionSnapshot();
                    // inPos robusto: acepta net de snapshot, net previo, o "en gracia" si Virtual BE
                    var inPos = Math.Abs(snap.NetQty) != 0 || Math.Abs(_prevNet) != 0;
                    if (!inPos && VirtualBreakEven) inPos = true;

                    if (inPos && _beTargetPx > 0m)
                    {
                        var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);
                        var (last, hi, lo) = GetLastPriceTriplet();
                        var dir = Math.Sign(snap.NetQty != 0 ? snap.NetQty : (_prevNet != 0 ? _prevNet : _beDirHint));

                        // Toque intrabar: LONG usa High, SHORT usa Low
                        bool touched = dir > 0 ? (hi >= _beTargetPx) : (lo <= _beTargetPx);

                        if (EnableLogging)
                            DebugLog.W("RM/BE/TRACE",
                                $"inPos={inPos} dir={(dir > 0 ? "LONG" : "SHORT")} tgt={_beTargetPx:F2} last={last:F2} hi={hi:F2} lo={lo:F2} touched={touched}");

                        if (touched)
                        {
                            var refPx = snap.AvgPrice > 0m ? snap.AvgPrice : last;
                            var bePx = ComputeBePrice(dir, refPx, tickSize);
                            var newTrigger = ClampBeTrigger(bePx);
                            if (newTrigger != bePx && EnableLogging)
                                DebugLog.W("RM/BE/SAFE", $"clamp {bePx:F2} â†’ {newTrigger:F2} (last={GetLastPriceSafe():F2})");
                            if (EnableLogging) DebugLog.W("RM/BE", $"TOUCH trigger @ {_beTargetPx:F2} ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ move SL to BE {newTrigger:F2}");
                            MoveAllRmStopsTo(newTrigger, "BE touch");
                            _beDone = true;
                        }
                    }
                }
            }
            catch (Exception ex) { DebugLog.W("RM/BE", $"BE touch check EX: {ex.Message}"); }

            // === TRAILING: Process trailing stop logic ===
            if (_trailArmed && TrailingMode != RmTrailMode.Off)
            {
                try
                {
                    var current = SafeGetCandle(bar);
                    if (current != null)
                        ProcessTrailing(current);
                }
                catch (Exception ex) { if (EnableLogging) DebugLog.W("RM/TRAIL", $"ProcessTrailing invocation EX: {ex.Message}"); }
            }
        }

        protected override void OnOrderChanged(Order order)
        {
            // ==== Enhanced logging for ALL order events ====
            try
            {
                var c = order?.Comment ?? "";
                var st = order.Status();
                if (EnableLogging)
                    DebugLog.W("RM/EVT", $"OnOrderChanged: id={order?.Id} comment='{c}' status={st} side={order?.Direction} qty={order?.QuantityToFill} canceled={order?.Canceled}");
                if (_stopToFlat && EnableLogging)
                    DebugLog.W("RM/STOP", $"EVT: id={order?.Id} comment='{c}' status={st} qty={order?.QuantityToFill} canceled={order?.Canceled}");

                // Track also EXTERNAL orders (ChartTrader) for later mass-cancel on Stop
                // Scope to this instrument+portfolio
                if (order?.Security?.ToString() == Security?.ToString() &&
                    order?.Portfolio?.ToString() == Portfolio?.ToString())
                {
                    lock (_liveOrdersLock)
                    {
                        _liveOrders.Add(order);
                        if (_liveOrders.Count > 512)
                            _liveOrders.RemoveRange(0, _liveOrders.Count - 512);
                    }
                }
            }
            catch { /* ignore logging issues */ }

            try
            {
                if (!IsActivated || !ManageManualEntries) return;

                var comment = order?.Comment ?? "";
                var st = order.Status();

                // 3a) Ignorar fills de cierre manual/estrategia
                if (comment.Equals("Close position", StringComparison.OrdinalIgnoreCase))
                {
                    if (st == OrderStatus.Filled || st == OrderStatus.PartlyFilled)
                    {
                        // Cuarentena corta para evitar attach reentrante
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(800);
                        ResetAttachState("Close position fill");
                    }
                    if (EnableLogging) DebugLog.W("RM/GATE", "Skip arming on 'Close position' fill");
                    return;
                }

                if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged: comment={comment} status={st}");

                // Ignora la 468 y mis propias RM
                if (comment.StartsWith(IgnorePrefix))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: IgnorePrefix detected ({IgnorePrefix})");
                    return;
                }
                if (comment.StartsWith(OwnerPrefix))
                {
                    // Tracking del STOP RM para trailing: guardar precio y riesgo cuando el SL pasa a Placed/Active
                    var isRmOrder = true;
                    var isSl = comment.StartsWith(OwnerPrefix + "SL:");
                    if (isRmOrder && isSl)
                    {
                        if (st == OrderStatus.Placed || st == OrderStatus.PartlyFilled)
                        {
                            // Capturar trigger price (órdenes Stop usan TriggerPrice, no Price)
                            var trig = 0m;
                            try { trig = Convert.ToDecimal(order.TriggerPrice); } catch { /* reflection-safe */ }
                            _lastKnownStopPx = (trig > 0m ? trig : order.Price);
                            // Si aún no capturamos el riesgo inicial, hazlo ahora
                            if (_trailRiskAbs <= 0m)
                            {
                                var entry = _pendingEntryPrice != 0m ? _pendingEntryPrice : _currentPositionEntryPrice;
                                if (entry > 0m && _lastKnownStopPx > 0m)
                                {
                                    _trailRiskAbs = Math.Abs(entry - _lastKnownStopPx);
                                    TLog("INIT", $"riskAbs={_trailRiskAbs:F2} entry={entry:F2} stop0={_lastKnownStopPx:F2}");
                                }
                            }
                        }
                    }

                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: OwnerPrefix detected ({OwnerPrefix})");
                    return;
                }

                // Keep legacy "Close position" detection (when comment is present)
                if (comment.IndexOf("Close position", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pendingAttach = false;
                    CancelResidualBrackets("user pressed Close (comment match)");
                    var graceUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                    _postCloseUntil = graceUntil;
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", $"Grace window opened after Close (EVT), until={graceUntil:HH:mm:ss.fff}");
                    return;
                }

                // SÃƒÆ’Ã‚Â³lo nos interesan fills/parciales
                if (!(st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                {
                    if (EnableLogging) DebugLog.W("RM/ORD", $"OnOrderChanged SKIP: status={st} (not filled/partly)");
                    return;
                }

                // Marca attach pendiente; la direcciÃƒÆ’Ã‚Â³n la deduciremos con el net por barra o ahora mismo
                _pendingDirHint = ExtractDirFromOrder(order);
                _pendingFillQty = ExtractFilledQty(order);

                // Solo guardar entryPrice si el order tiene AvgPrice vÃƒÆ’Ã‚Â¡lido; si no, lo obtendremos de la posiciÃƒÆ’Ã‚Â³n
                var orderAvgPrice = ExtractAvgFillPrice(order);
                _pendingEntryPrice = orderAvgPrice > 0m ? orderAvgPrice : 0m;

                // Anclar la "prev-bar" en el instante del fill (N-1 respecto a la barra visible ahora)
                try
                {
                    var curBar  = CurrentBar;
                    var prevBar = CurrentBar - 1;
                    var cur  = prevBar >= 0 ? GetCandle(prevBar) : null;  // N (CurrentBar-1 es la barra de fill)
                    var prev = CurrentBar > 1 ? GetCandle(CurrentBar - 2) : null;  // N-1

                    bool prevExists   = prev != null;
                    bool prevSameDir  = prevExists && (
                        (_pendingDirHint > 0 && prev.Close > prev.Open) ||   // LONG â†’ N-1 debe ser alcista
                        (_pendingDirHint < 0 && prev.Close < prev.Open)      // SHORT â†’ N-1 debe ser bajista
                    );
                    bool geometricOk  = prevExists && cur != null && (
                        (_pendingDirHint > 0 && prev.Low  < cur.Low) ||
                        (_pendingDirHint < 0 && prev.High > cur.High)
                    );

                    // Solo usamos N-1 si (existe) y (pasa la geometrÃ­a) y
                    // (o bien no exigimos coincidencia de color o coincide el color)
                    bool canUsePrev = prevExists && geometricOk &&
                                      (!PrevBarMustMatchEntryDir || prevSameDir);

                    // Ancla definitiva
                    int anchorBar = canUsePrev ? (CurrentBar - 2) : (CurrentBar - 1);
                    _pendingPrevBarIdxAtFill = CurrentBar - 2;  // siempre guardamos dÃ³nde estarÃ­a N-1
                    _pendingAnchorBarAtFill  = anchorBar;

                    var anchor = GetCandle(anchorBar);
                    _pendingAnchorHigh = anchor?.High ?? 0m;
                    _pendingAnchorLow  = anchor?.Low  ?? 0m;

                    if (EnableLogging)
                        DebugLog.W("RM/STOPMODE", $"Anchor captured at fill: {(canUsePrev ? "N-1" : "N")} bar={_pendingAnchorBarAtFill} " +
                            $"high={_pendingAnchorHigh:F2} low={_pendingAnchorLow:F2} dir={(_pendingDirHint > 0 ? "LONG" : "SHORT")} " +
                            $"prevSameDir={prevSameDir} geomOk={geometricOk} (curBar={CurrentBar})");
                }
                catch
                {
                    _pendingPrevBarIdxAtFill = -1;
                    _pendingAnchorBarAtFill = -1;
                    _pendingAnchorHigh = _pendingAnchorLow = 0m;
                }

                // Armar attach con protecciÃƒÆ’Ã‚Â³n temporal
                _pendingAttach = true;
                _pendingSince = DateTime.UtcNow;
                _lastAttachArmAt = _pendingSince;

                if (EnableLogging)
                    DebugLog.W("RM/ORD", $"Manual order DETECTED ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ pendingAttach=true | dir={_pendingDirHint} fillQty={_pendingFillQty} entryPx={_pendingEntryPrice:F2}");

                // === Track position entry for P&L ===
                // Intentar obtener precio de entry de mÃºltiples fuentes
                var entryPriceForTracking = _pendingEntryPrice;
                if (entryPriceForTracking <= 0m)
                {
                    // Fallback 1: leer desde posiciÃ³n actual
                    var snapForEntry = ReadPositionSnapshot();
                    entryPriceForTracking = snapForEntry.AvgPrice;
                    if (EnableLogging) DebugLog.W("RM/PNL", $"Entry price from order=0, trying position avgPrice={entryPriceForTracking:F2}");
                }

                if (entryPriceForTracking <= 0m)
                {
                    // Fallback 2: usar precio de la Ãºltima barra (mejor aproximaciÃ³n)
                    entryPriceForTracking = GetLastPriceSafe();
                    if (EnableLogging) DebugLog.W("RM/PNL", $"Entry price from position=0, using last bar price={entryPriceForTracking:F2}");
                }

                if (entryPriceForTracking > 0m && _pendingFillQty > 0)
                {
                    TrackPositionEntry(entryPriceForTracking, _pendingFillQty, _pendingDirHint);
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/PNL", $"SKIP TrackPositionEntry: entryPx={entryPriceForTracking:F2} fillQty={_pendingFillQty}");
                }

                TryAttachBracketsNow(); // intenta en el mismo tick; si el gate decide WAIT, ya lo reintentarÃƒÆ’Ã‚Â¡ OnCalculate
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"OnOrderChanged EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }

            // === BE por FILL real del TP ===
            try
            {
                if (_beArmed && !_beDone && BreakEvenMode == RmBeMode.OnTPFill)
                {
                    var c = order?.Comment ?? "";
                    var st = order.Status();
                    if (c.StartsWith(OwnerPrefix + "TP:") && (st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                    {
                        // Confirmar lado y entrada
                        var snap = ReadPositionSnapshot();
                        var dir = Math.Sign(snap.NetQty != 0 ? snap.NetQty : _beDirHint);
                        if (dir != 0)
                        {
                            var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);
                            var bePx = ComputeBePrice(dir, snap.AvgPrice > 0m ? snap.AvgPrice : ExtractAvgFillPrice(order), tickSize);
                            var newTrigger = ClampBeTrigger(bePx);
                            if (newTrigger != bePx && EnableLogging)
                                DebugLog.W("RM/BE/SAFE", $"clamp {bePx:F2} â†’ {newTrigger:F2} (last={GetLastPriceSafe():F2})");
                            if (EnableLogging) DebugLog.W("RM/BE", $"FILL trigger by TP ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ move SL to BE {newTrigger:F2}");
                            MoveAllRmStopsTo(newTrigger, "BE fill");
                            _beDone = true;
                        }
                    }
                }
            }
            catch { /* tolerante */ }

            // === Track TP/SL fills for P&L ===
            try
            {
                var c = order?.Comment ?? "";
                var st = order.Status();
                if ((c.StartsWith(OwnerPrefix + "TP:") || c.StartsWith(OwnerPrefix + "SL:"))
                    && (st == OrderStatus.Filled || st == OrderStatus.PartlyFilled))
                {
                    if (EnableLogging) DebugLog.W("RM/PNL", $"TP/SL fill detected: comment={c} status={st}");

                    var exitPrice = ExtractAvgFillPrice(order);
                    var filledQty = ExtractFilledQty(order);

                    if (EnableLogging) DebugLog.W("RM/PNL", $"Exit details: price={exitPrice:F2} qty={filledQty}");

                    // Fallback: si exitPrice es 0, usar order.Price
                    if (exitPrice <= 0m)
                    {
                        exitPrice = order.Price;
                        if (EnableLogging) DebugLog.W("RM/PNL", $"Using order.Price as fallback: {exitPrice:F2}");
                    }

                    if (exitPrice > 0m && filledQty > 0)
                    {
                        // DirecciÃ³n: TP/SL son Ã³rdenes de cierre, asÃ­ que estÃ¡n en direcciÃ³n opuesta a la posiciÃ³n
                        // Si fue LONG â†’ TP/SL son SELL â†’ dir original = +1
                        // Si fue SHORT â†’ TP/SL son BUY â†’ dir original = -1
                        var orderDir = ExtractDirFromOrder(order);
                        var positionDir = -orderDir;  // invertir porque es orden de cierre

                        if (EnableLogging) DebugLog.W("RM/PNL", $"Calling TrackPositionClose: exitPx={exitPrice:F2} qty={filledQty} dir={positionDir}");
                        TrackPositionClose(exitPrice, filledQty, positionDir);
                    }
                    else
                    {
                        if (EnableLogging) DebugLog.W("RM/PNL", $"SKIP TrackPositionClose: exitPx={exitPrice:F2} qty={filledQty}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"Track TP/SL fill EX: {ex.Message}");
            }
        }

        // New: capture newly seen orders too (fires when order is registered)
        protected override void OnNewOrder(Order order)
        {
            try
            {
                if (order == null) return;
                if (EnableLogging)
                    DebugLog.W("RM/EVT", $"OnNewOrder: id={order.Id} comment='{order.Comment}' type={order.Type} status={order.Status()} qty={order.QuantityToFill}");
                if (order.Security?.ToString() == Security?.ToString() &&
                    order.Portfolio?.ToString() == Portfolio?.ToString())
                {
                    lock (_liveOrdersLock)
                    {
                        _liveOrders.Add(order);
                        if (_liveOrders.Count > 512)
                            _liveOrders.RemoveRange(0, _liveOrders.Count - 512);
                    }
                }
            }
            catch { }
        }

        // =================== Stop Strategy hooks ===================
        protected override void OnStarted()
        {
            try
            {
                AccountEquitySnapshot = ReadAccountEquityUSD();
                if (EnableLogging) DebugLog.W("RM/SNAP", $"Equity init ÃƒÂ¢Ã¢â‚¬ Â°Ã‹â€  {AccountEquitySnapshot:F2} USD");

                _stopToFlat = false;
                _rmStopGraceUntil = DateTime.MinValue;
                _nextStopSweepAt   = DateTime.MinValue;
                if (EnableLogging) DebugLog.W("RM/STOP", "Started ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ reset stop-to-flat flags");
            }
            catch { }
        }

        protected override void OnStopping()
        {
            try
            {
                _stopToFlat = true;
                _rmStopGraceUntil = DateTime.UtcNow.AddMilliseconds(_rmStopGraceMs);
                _nextStopSweepAt   = DateTime.UtcNow; // primer barrido inmediato
                if (EnableLogging) DebugLog.W("RM/STOP", $"OnStopping ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ engage StopToFlat, grace until={_rmStopGraceUntil:HH:mm:ss.fff}");

                // 1) Cancelar brackets + cualquier otra orden viva del instrumento
                // Evitar limpiar si acabamos de armar attach (protecciÃ³n anti-cancel inmediato)
                var freshAttach = _pendingAttach &&
                                  (DateTime.UtcNow - _lastAttachArmAt).TotalMilliseconds < (AttachProtectMs + 250);
                if (!freshAttach)
                {
                    CancelResidualBrackets("stop-to-flat");
                    CancelNonBracketWorkingOrders("stop-to-flat");
                }
                else if (EnableLogging)
                {
                    DebugLog.W("RM/STOP", "Skip cleanup due to fresh attach (anti-cancel)");
                }

                // 2) FLATTEN: intentar SIEMPRE el cierre nativo y dejar fallback armado
                var snap = ReadPositionSnapshot();
                if (EnableLogging) DebugLog.W("RM/STOP", $"Stop snapshot: net={snap.NetQty} avg={snap.AvgPrice:F2} (via TM/Portfolio)");
                var tmClosed = TryClosePositionViaTradingManager();
                if (EnableLogging) DebugLog.W("RM/STOP", $"ClosePosition attempt (TM) result={tmClosed}");
                // 3) Fallback garantizado: EnsureFlattenOutstanding no duplica y no-op si net==0
                EnsureFlattenOutstanding("OnStopping");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/STOP", $"OnStopping EX: {ex.Message}");
            }
            // No bloquees aquÃƒÆ’Ã‚Â­: ATAS seguirÃƒÆ’Ã‚Â¡ el ciclo; terminamos de drenar en eventos/ticks
        }

        protected override void OnStopped()
        {
            try
            {
                if (EnableLogging) DebugLog.W("RM/STOP", "OnStopped ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ strategy stopped (final)");
                _stopToFlat = false;
                _rmStopGraceUntil = DateTime.MinValue;

                // Reset Session P&L tracking
                if (EnableLogging) DebugLog.W("RM/PNL", $"Session ended - Final P&L: {SessionPnL:F2} USD (Realized: {_sessionRealizedPnL:F2})");
                _sessionStartingEquity = 0m;
                _currentPositionEntryPrice = 0m;
                _currentPositionQty = 0;
                _sessionRealizedPnL = 0m;
                SessionPnL = 0m;
            }
            catch { }
        }

        // Usa exclusivamente la ancla capturada (N-1; o N si no hay N-1).
        private (decimal Price, string Debug) ComputeStructureStopPx(int dir, decimal tickSize)
        {
            var outside     = PrevBarOffsetSide == RmPrevBarOffsetSide.Outside;

            // Si por algÃºn motivo no hay ancla, reintenta: N-1 si existe, si no N
            decimal hi = _pendingAnchorHigh, lo = _pendingAnchorLow;
            int anchorBar = _pendingAnchorBarAtFill;
            if (hi <= 0m && lo <= 0m)
            {
                int idx = CurrentBar > 1 ? (CurrentBar - 2) : Math.Max(0, CurrentBar - 1);
                var c = GetCandle(idx);
                hi = c?.High ?? 0m;
                lo = c?.Low  ?? 0m;
                anchorBar = idx;
                _pendingAnchorBarAtFill = idx;
            }

            // Determinar si estamos usando ancla N (contra-tendencia)
            bool isAnchorN = (anchorBar >= CurrentBar - 1);
            int ctExtra = isAnchorN ? CounterTrendExtraOffsetTicks : 0;
            int totalOffset = Math.Max(0, PrevBarOffsetTicks) + ctExtra;

            decimal extreme = dir > 0 ? lo : hi;
            decimal structSl = dir > 0
                ? ShrinkPrice(extreme - totalOffset * tickSize)   // LONG: por debajo de la mecha
                : ShrinkPrice(extreme + totalOffset * tickSize);  // SHORT: por encima de la mecha

            var dbg = $"anchor={(isAnchorN ? "N" : "N-1")} offBase={PrevBarOffsetTicks} ctExtra={ctExtra} " +
                      $"totalOff={totalOffset} â†’ SL={structSl:F2}";
            return (structSl, dbg);
        }

        private void TryAttachBracketsNow()
        {
            try
            {
                // Si acabamos de armar attach, no bloquees por una grace antigua
                if (_pendingAttach && DateTime.UtcNow <= _postCloseUntil)
                    _postCloseUntil = DateTime.MinValue;
                // Gate 1: cuarentena post-close
                if (DateTime.UtcNow <= _postCloseUntil)
                {
                    if (EnableLogging) DebugLog.W("RM/GATE", $"HOLD attach (in post-close grace until {_postCloseUntil:HH:mm:ss.fff})");
                    return;
                }
                // Gate 2: requiere attach armado
                if (!_pendingAttach)
                {
                    if (EnableLogging) DebugLog.W("RM/GATE", "Skip attach: _pendingAttach=false");
                    return;
                }
                var netAbs = Math.Abs(ReadNetPosition());
                // No hacemos return aquÃ­: el gate inferior decidirÃ¡ si WAIT / ATTACH / FALLBACK

                // Si estamos parando, no adjuntar nada (evita re-entradas durante stop)
                if (_stopToFlat)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", "Skipping attach: strategy is stopping");
                    _pendingAttach = false;
                    return;
                }

                // 0) Pre-check & diagnostics
                var now = DateTime.UtcNow;
                // Para los cierres por Stop usamos la gracia local de stop
                var inGrace = (now <= _postCloseUntil) || (now <= _rmStopGraceUntil);
                if (EnableLogging)
                    DebugLog.W("RM/ATTACH", $"Pre-check: pendingSince={_pendingSince:HH:mm:ss.fff} inGrace={inGrace} (closeGrace={(now <= _postCloseUntil)} stopGrace={(now <= _rmStopGraceUntil)})");

                // TelemetrÃƒÆ’Ã‚Â­a de estados de brackets antes del check
                LogOrderStateHistogram("pre-attach");

                // 1) Are there live brackets? (only SL:/TP:, ignore ENF)
                var any468  = HasLiveOrdersWithPrefix(IgnorePrefix);
                var anyRmSl = HasLiveOrdersWithPrefix(OwnerPrefix + "SL:");
                var anyRmTp = HasLiveOrdersWithPrefix(OwnerPrefix + "TP:");
                var anyBrackets = any468 || anyRmSl || anyRmTp;
                if (EnableLogging)
                    DebugLog.W("RM/ATTACH", $"Bracket check: any468={any468} anyRmSl={anyRmSl} anyRmTp={anyRmTp} anyBrackets={anyBrackets}");

                if (anyBrackets && !inGrace)
                {
                    var waitedMs = (int)(now - _pendingSince).TotalMilliseconds;
                    if (waitedMs > _maxRetryMs)
                    {
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs); // bypass
                        if (EnableLogging)
                            DebugLog.W("RM/GRACE", $"MaxRetry {waitedMs}ms ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ forcing grace & proceeding");
                        // sigue sin return
                    }
                    else if (waitedMs > _cleanupWaitMs)
                    {
                        CancelResidualBrackets($"cleanup timeout waited={waitedMs}ms");
                        _postCloseUntil = DateTime.UtcNow.AddMilliseconds(_postCloseGraceMs);
                        _pendingSince = DateTime.UtcNow;
                        if (EnableLogging)
                            DebugLog.W("RM/GRACE", $"Cleanup timeout ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ grace reset to {_postCloseUntil:HH:mm:ss.fff}");
                        return; // reintenta
                    }
                    else
                    {
                        if (EnableLogging)
                            DebugLog.W("RM/WAIT", $"live brackets ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ retry (waited={waitedMs}ms)");
                        return;
                    }
                }
                else if (anyBrackets && inGrace)
                {
                    if (EnableLogging)
                        DebugLog.W("RM/GRACE", "post-close grace ACTIVE ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ ignoring live-brackets block");
                }

                // 2) Gate: si estamos en GRACE, saltamos el gate y vamos a FALLBACK ya mismo
                var netNow = ReadNetPosition();
                var gateWaitedMs = (int)(DateTime.UtcNow - _pendingSince).TotalMilliseconds;
                if (EnableLogging) DebugLog.W("RM/GATE", $"Gate check: _prevNet={_prevNet} netNow={netNow} waitedMs={gateWaitedMs} deadline={_attachDeadlineMs} inGrace={inGrace}");

                if (!inGrace)
                {
                    if (Math.Abs(_prevNet) == 0 && Math.Abs(netNow) > 0)
                    {
                        if (EnableLogging) DebugLog.W("RM/GATE", $"VALID TRANSITION: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ ATTACH");
                    }
                    else
                    {
                        if (gateWaitedMs < _attachDeadlineMs)
                        {
                            if (EnableLogging) DebugLog.W("RM/GATE", $"WAITING: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms < deadline={_attachDeadlineMs}ms ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ WAIT");
                            return;
                        }
                        if (!(AllowAttachFallback && _pendingDirHint != 0))
                        {
                            _pendingAttach = false;
                            if (EnableLogging) DebugLog.W("RM/GATE", $"ABORT: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ no fallback allowed");
                            if (EnableLogging) DebugLog.W("RM/ABORT", "flat after TTL");
                            return;
                        }
                        if (EnableLogging) DebugLog.W("RM/GATE", $"FALLBACK: prevNet={_prevNet} netNow={netNow} elapsed={gateWaitedMs}ms ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ ATTACH(FALLBACK) dirHint={_pendingDirHint}");
                    }
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/GATE", $"GRACE BYPASS: skipping net gate ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ ATTACH(FALLBACK) dirHint={_pendingDirHint}");
                }

                var dir = Math.Abs(netNow) > 0 ? Math.Sign(netNow) : _pendingDirHint;
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Direction determined: dir={dir} (netNow={netNow}, dirHint={_pendingDirHint})");

                // qty OBJETIVO del plan:
                //  - Manual  ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ SIEMPRE la UI (ManualQty), no el net/fill
                //  - Riesgo  ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ la calcularÃƒÆ’Ã‚Â¡ el engine
                int manualQtyToUse = ManualQty;
                if (SizingMode == RmSizingMode.Manual)
                {
                    manualQtyToUse = Math.Clamp(ManualQty, Math.Max(1, MinQty), Math.Max(1, MaxQty));
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Manual mode: Using UI ManualQty={manualQtyToUse} (ignoring net/fill for TARGET)");
                }
                else
                {
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Risk-based sizing: engine will compute target qty");
                }

                // Instrumento (fallbacks)
                var tickSize = FallbackTickSize;
                try { tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize); } catch { }

                var secName = (Security?.ToString() ?? string.Empty);
                var tickValueResolved = ResolveTickValueUsd(secName, TickValueOverrides, FallbackTickValueUsd);
                var tickValue = tickValueResolved;

                if (EnableLogging)
                    DebugLog.W("RM/TICK", $"tickSize={tickSize} tickValueUSD={tickValue} symbolKey='{secName}' overrides='{TickValueOverrides}'");

                // precio de entrada: order ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ avgPrice posiciÃƒÆ’Ã‚Â³n ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ vela previa (Close)
                var entryPx = _pendingEntryPrice;
                if (entryPx <= 0m)
                {
                    var snap = ReadPositionSnapshot();
                    entryPx = snap.AvgPrice;
                    if (EnableLogging)
                        DebugLog.W("RM/PLAN", $"Using position avgPrice: entryPx={entryPx:F2} (orderPrice was {_pendingEntryPrice:F2})");
                }
                if (entryPx <= 0m)
                {
                    try
                    {
                        var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                        entryPx = GetCandle(barIdx).Close;
                        if (EnableLogging) DebugLog.W("RM/PLAN", $"Fallback candle price used: entryPx={entryPx:F2} (bar={barIdx})");
                    } catch { }
                    if (entryPx <= 0m) { if (EnableLogging) DebugLog.W("RM/PLAN", "No valid entryPx available ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ retry"); return; }
                }

                // === STOP por estructura (opcional) ===
                var approxStopTicks = Math.Max(1, DefaultStopTicks);
                decimal? overrideStopPx = null;
                try
                {
                    if (EnableLogging)
                        DebugLog.W("RM/STOPMODE", $"Active={StopPlacementMode} prevIdxAtFill={_pendingPrevBarIdxAtFill} offTicks={PrevBarOffsetTicks} side={PrevBarOffsetSide}");
                    if (StopPlacementMode == RmStopPlacement.PrevBarOppositeExtreme)
                    {
                        var (slPx, dbg) = ComputeStructureStopPx(dir, Convert.ToDecimal(tickSize));
                        overrideStopPx  = slPx;
                        approxStopTicks = Math.Max(1, (int)Math.Round(Math.Abs(entryPx - slPx) / tickSize));
                        if (EnableLogging)
                            DebugLog.W("RM/STOPMODE", $"STRUCT SL: {dbg} | ticksâ‰ˆ{approxStopTicks}");

                        // === Volatility floor: sÃ³lo si (opciÃ³n ON) y se usÃ³ N (no habÃ­a N-1) ===
                        EnforceVolatilityFloorIfNeeded(dir, entryPx, ref overrideStopPx, ref approxStopTicks, tickSize);
                    }
                } catch { /* fallback a DefaultStopTicks */ }

                var ctx = new MyAtas.Risk.Models.EntryContext(
                    Account: Portfolio?.ToString() ?? "DEFAULT",
                    Symbol: Security?.ToString() ?? "",
                    Direction: dir > 0 ? MyAtas.Risk.Models.Direction.Long : MyAtas.Risk.Models.Direction.Short,
                    EntryPrice: entryPx,
                    ApproxStopTicks: approxStopTicks,                      // <-- usa ticks desde N-1 si procede
                    TickSize: tickSize,
                    TickValueUSD: tickValue,
                    TimeUtc: DateTime.UtcNow
                );

                var sizingCfg = new MyAtas.Risk.Models.SizingConfig(
                    Mode: SizingMode.ToString(),
                    ManualQty: manualQtyToUse,
                    RiskUsd: RiskPerTradeUsd,
                    RiskPct: RiskPercentOfAccount,
                    AccountEquityOverride: (AccountEquityOverride > 0m)
                        ? AccountEquityOverride
                        : (AccountEquitySnapshot > 0m ? AccountEquitySnapshot : _sessionStartingEquity),
                    TickValueOverrides: TickValueOverrides,
                    UnderfundedPolicy: Underfunded,
                    MinQty: Math.Max(1, MinQty),
                    MaxQty: Math.Max(1, MaxQty)
                );

                // TPs desde UI (V2 primero; si no hay, usa legacy)
                var tg = GetTargetsSnapshot();

                // Logs de diagnóstico: mostrar cada fila de Targets antes de build
                if (EnableLogging)
                {
                    var t = Targets;
                    if (t != null)
                    {
                        DebugLog.W("RM/SPLIT/ROW", $"TP1: active={t.TP1?.Active ?? false} R={t.TP1?.R ?? 0:F2} pct={t.TP1?.Percent ?? 0}% {(t.TP1?.Percent > 0 ? "OK" : "SKIP (pct=0)")}");
                        DebugLog.W("RM/SPLIT/ROW", $"TP2: active={t.TP2?.Active ?? false} R={t.TP2?.R ?? 0:F2} pct={t.TP2?.Percent ?? 0}% {(t.TP2?.Percent > 0 ? "OK" : "SKIP (pct=0)")}");
                        DebugLog.W("RM/SPLIT/ROW", $"TP3: active={t.TP3?.Active ?? false} R={t.TP3?.R ?? 0:F2} pct={t.TP3?.Percent ?? 0}% {(t.TP3?.Percent > 0 ? "OK" : "SKIP (pct=0)")}");
                    }
                }

                var (tpR, tpSplits) = _RmSplitHelper.BuildTpArrays(tg.preset, tg.r1, tg.r2, tg.r3, tg.p1, tg.p2, tg.p3);

                if (EnableLogging)
                    DebugLog.W("RM/SPLIT", $"SRC={tg.src} -> preset={tg.preset} tpR=[{string.Join(",", tpR)}] splits=[{string.Join(",", tpSplits)}]");

                var bracketCfg = new MyAtas.Risk.Models.BracketConfig(
                    StopTicks: approxStopTicks,                            // <-- idem
                    SlOffsetTicks: 0m,
                    TpRMultiples: tpR,
                    Splits: tpSplits
                );

                var engine = GetEngine();
                if (engine == null) { if (EnableLogging) DebugLog.W("RM/PLAN", "Engine not available ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ abort"); return; }

                var plan = engine.BuildPlan(ctx, sizingCfg, bracketCfg, out var szReason);

                if (plan == null)
                {
                    if (EnableLogging) DebugLog.W("RM/PLAN", "BuildPlan ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ null");
                    return;
                }
                else
                {
                    if (EnableLogging)
                    {
                        DebugLog.W("RM/PLAN", $"Built plan: totalQty={plan.TotalQty} stop={plan.StopLoss?.Price:F2} tps={plan.TakeProfits?.Count} reason={plan.Reason}");

                        // Log de configuraciÃ³n de targets
                        var act = (_targets ?? new TargetsModel()).ActiveOrdered().ToList();
                        DebugLog.W("RM/TP", $"PLAN v2={_useTargetsV2} preset={PresetTPs} | " +
                            string.Join(" | ", act.Select((t, i) => $"TP{i + 1}: R={t.R} %={t.Percent} active={t.Active}")));
                    }
                }

                // === FALLBACK DE TP: Si motor devuelve precio 0, calcular localmente ===
                decimal slPxFromPlan = plan.StopLoss?.Price ?? 0m;
                if (slPxFromPlan <= 0m && overrideStopPx.HasValue) slPxFromPlan = overrideStopPx.Value;
                var slPxForR = slPxFromPlan > 0m ? slPxFromPlan
                    : (dir > 0 ? entryPx - approxStopTicks * tickSize
                               : entryPx + approxStopTicks * tickSize);
                var rPrice = dir > 0 ? (entryPx - slPxForR) : (slPxForR - entryPx);
                if (rPrice <= 0m) rPrice = Math.Max(1, approxStopTicks) * tickSize;

                // Construir lista local de TPs seguros (precio>0) a partir de R-multiples UI
                var tpRlocals = new System.Collections.Generic.List<decimal>();
                if (PresetTPs >= 1 && TP1pctunit > 0) tpRlocals.Add(Math.Max(0.25m, TP1R));
                if (PresetTPs >= 2 && TP2pctunit > 0) tpRlocals.Add(Math.Max(0.25m, TP2R));
                if (PresetTPs >= 3 && TP3pctunit > 0) tpRlocals.Add(Math.Max(0.25m, TP3R));

                // Validar y reconstruir TPs del plan
                var safeTps = new System.Collections.Generic.List<MyAtas.Risk.Models.BracketLeg>();
                for (int i = 0; i < plan.TakeProfits.Count; i++)
                {
                    var tp = plan.TakeProfits[i];
                    var px = tp.Price;
                    if (px <= 0m)
                    {
                        // Motor no puso precio vÃ¡lido â†’ calcular localmente
                        var rMult = (i < tpRlocals.Count) ? tpRlocals[i] : (tpRlocals.Count > 0 ? tpRlocals[tpRlocals.Count - 1] : 1m);
                        var raw = dir > 0 ? (entryPx + rMult * rPrice) : (entryPx - rMult * rPrice);
                        px = ShrinkPrice(raw);
                        if (EnableLogging) DebugLog.W("RM/TP-FALLBACK", $"TP[{i}] era 0 â†’ calculado: {px:F2} (R={rMult:F2})");
                    }
                    if (px > 0m) safeTps.Add(new MyAtas.Risk.Models.BracketLeg(px, tp.Quantity));
                }

                // Si no hay TPs vÃ¡lidos, crear uno en 1R con el 100% de qty
                if (safeTps.Count == 0)
                {
                    // Con BE virtual y/o trailing activos → PERMITIR 0 TPs reales
                    if (VirtualBreakEven || TrailingMode != RmTrailMode.Off)
                    {
                        if (EnableLogging) DebugLog.W("RM/TP-FALLBACK", "0 TPs permitido (VirtualBE/Trailing). Sin fallback.");
                    }
                    else
                    {
                        // Solo si NO usamos BE virtual ni Trailing: forzar un TP de seguridad
                        var px1 = ShrinkPrice(dir > 0 ? (entryPx + 1m * rPrice) : (entryPx - 1m * rPrice));
                        if (px1 > 0m)
                        {
                            safeTps.Add(new MyAtas.Risk.Models.BracketLeg(px1, Math.Max(1, plan.TotalQty)));
                            if (EnableLogging) DebugLog.W("RM/TP-FALLBACK", $"Forzado 1 TP en 1R: {px1:F2}");
                        }
                    }
                }

                // Sustituir TPs en plan
                if (safeTps.Count > 0)
                    plan = new MyAtas.Risk.Models.RiskPlan(plan.TotalQty, plan.StopLoss, safeTps, plan.OcoPolicy, plan.Reason + " [TP-safe]");

                // ===== "LA UI MANDA": Si EnforceManualQty estÃ¡ activo, usar ManualQty =====
                var manualTarget = Math.Max(MinQty, ManualQty);
                var targetForEnforce = EnforceManualQty ? manualTarget : plan.TotalQty;

                // Si estamos imponiendo cantidad manual y difiere del plan,
                // recalculamos los splits para que brackets y ENFORCE vayan alineados.
                if (EnforceManualQty && targetForEnforce != plan.TotalQty)
                {
                    var q = targetForEnforce;
                    var s1 = Math.Max(0, Math.Min(100, TP1pctunit));
                    var s2 = Math.Max(0, Math.Min(100, TP2pctunit));
                    var s3 = Math.Max(0, Math.Min(100, TP3pctunit));
                    var sum = Math.Max(1, s1 + s2 + s3);
                    int q1 = (int)Math.Floor(q * s1 / (decimal)sum);
                    int q2 = (int)Math.Floor(q * s2 / (decimal)sum);
                    int q3 = q - q1 - q2; // "resto" al Ãºltimo para evitar qty=0

                    // Extraer precios del plan antes de reconstruir
                    var tp1Px = plan.TakeProfits.Count > 0 ? plan.TakeProfits[0].Price : 0m;
                    var tp2Px = plan.TakeProfits.Count > 1 ? plan.TakeProfits[1].Price : 0m;
                    var tp3Px = plan.TakeProfits.Count > 2 ? plan.TakeProfits[2].Price : 0m;

                    // Reconstruir lista de TPs con nuevas cantidades (BracketLeg es immutable)
                    var newTps = new System.Collections.Generic.List<MyAtas.Risk.Models.BracketLeg>();
                    if (q1 > 0) newTps.Add(new MyAtas.Risk.Models.BracketLeg(tp1Px, q1));
                    if (q2 > 0) newTps.Add(new MyAtas.Risk.Models.BracketLeg(tp2Px, q2));
                    if (q3 > 0) newTps.Add(new MyAtas.Risk.Models.BracketLeg(tp3Px, q3));

                    // Reconstruir plan completo con nueva cantidad total y nuevos TPs (record positional)
                    plan = new MyAtas.Risk.Models.RiskPlan(
                        TotalQty: q,
                        StopLoss: plan.StopLoss,
                        TakeProfits: newTps,
                        OcoPolicy: plan.OcoPolicy,
                        Reason: plan.Reason + " [UI override]"
                    );

                    if (EnableLogging)
                    {
                        DebugLog.W("RM/PLAN", $"LA UI MANDA: Overriding plan qty {plan.TotalQty}â†’{q}, splits=[{q1},{q2},{q3}]");
                        DebugLog.W("RM/SPLIT", $"QTY ENFORCE -> q=[{string.Join(",", newTps.Select(t => t.Quantity))}] (planCount={newTps.Count})");
                    }
                }

                // ===== TARGET QTY por modo de dimensionado =====
                var riskPerContract = Math.Max(1, approxStopTicks) * tickValue; // USD por contrato
                int targetQty;
                if (SizingMode == RmSizingMode.Manual)
                {
                    targetQty = Math.Clamp(ManualQty, Math.Max(1, MinQty), Math.Max(1, MaxQty));
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"TARGET QTY (Manual) = {targetQty}");
                }
                else
                {
                    decimal budgetUsd = 0m;
                    if (SizingMode == RmSizingMode.FixedRiskUSD)
                        budgetUsd = Math.Max(0m, RiskPerTradeUsd);
                    else // PercentAccount
                    {
                        var equity = AccountEquityOverride > 0m ? AccountEquityOverride : AccountEquitySnapshot;
                        budgetUsd = Math.Max(0m, Math.Round(equity * (RiskPercentOfAccount / 100m), 2));
                    }

                    if (EnableLogging) DebugLog.W("RM/SIZING", $"Risk budget = {budgetUsd:F2} USD | risk/contract = {riskPerContract:F2} USD");
                    targetQty = (int)Math.Floor(budgetUsd / Math.Max(0.01m, riskPerContract));
                    targetQty = Math.Clamp(targetQty, Math.Max(0, MinQty), Math.Max(1, MaxQty));
                    if (targetQty <= 0)
                    {
                        if (Underfunded == MyAtas.Risk.Models.UnderfundedPolicy.Min1)
                        {
                            targetQty = 1;
                            if (EnableLogging) DebugLog.W("RM/SIZING", "Underfunded ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ forcing qty=1");
                        }
                        else
                        {
                            if (EnableLogging) DebugLog.W("RM/SIZING", "Underfunded ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ qty=0, abort attach");
                            _pendingAttach = false; return;
                        }
                    }
                    if (EnableLogging) DebugLog.W("RM/SIZING", $"TARGET QTY (Risk mode) = {targetQty}");
                }

                // ===== ENFORCEMENT (imponer SIEMPRE el objetivo del modo activo) =====
                targetForEnforce = (SizingMode == RmSizingMode.Manual)
                    ? Math.Clamp(ManualQty, Math.Max(1, MinQty), Math.Max(1, MaxQty))
                    : Math.Clamp(targetQty, Math.Max(1, MinQty), Math.Max(1, MaxQty)); // usar el que acabamos de calcular

                var currentNet = Math.Abs(ReadNetPosition());
                var filledHint = Math.Max(0, _pendingFillQty);
                var seen = Math.Max(currentNet, filledHint);
                var delta = targetForEnforce - seen;
                if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE: target={targetForEnforce} seen={seen} delta={delta}");

                if (delta != 0)
                {
                    var addSide = (dir > 0 ? OrderDirections.Buy : OrderDirections.Sell);
                    var cutSide = (dir > 0 ? OrderDirections.Sell : OrderDirections.Buy);

                    if (delta > 0)
                    {
                        // abrir la diferencia
                        if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE TRIGGER: MARKET {addSide} +{delta}");
                        SubmitRmMarket(addSide, delta); // (no reduce-only)
                    }
                    else
                    {
                        // cerrar el exceso con reduce-only
                        if (EnableLogging) DebugLog.W("RM/ENTRY", $"ENFORCE CUT: MARKET {cutSide} -{Math.Abs(delta)} (reduce-only)");
                        var o = new Order {
                            Portfolio = Portfolio, Security = Security,
                            Direction = cutSide, Type = OrderTypes.Market,
                            QuantityToFill = Math.Abs(delta),
                            Comment = $"{OwnerPrefix}ENF-CUT:{Guid.NewGuid():N}"
                        };
                        TrySetReduceOnly(o);
                        OpenOrder(o);
                    }
                }

                var coverSide = dir > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Cover side for brackets: coverSide={coverSide} (dir={dir})");

                // OCO 1:1 por TP ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â cada TP lleva su propio trozo de SL
                if (EnableLogging) DebugLog.W("RM/ATTACH", $"Starting bracket loop: TakeProfits.Count={plan.TakeProfits.Count}");

                // === CASO ESPECIAL: Solo SL (sin TPs) para VirtualBE/Trailing ===
                if (plan.TakeProfits.Count == 0)
                {
                    // No hay TPs reales (virtual BE/Trailing). Aun así debemos proteger con SL.
                    var slPriceToUse = overrideStopPx ?? plan.StopLoss.Price;
                    if (EnableLogging)
                        DebugLog.W("RM/ORD", $"SubmitRmStop SOLO-SL: side={coverSide} qty={targetForEnforce} triggerPx={slPriceToUse:F2} oco=<none>");
                    SubmitRmStop(/*oco*/ null, coverSide, targetForEnforce, slPriceToUse);

                    // Sembrar el estado del trailing con el stop recién planificado (evita race condition)
                    _lastKnownStopPx = slPriceToUse;
                    _trailBaselinePx = (_pendingEntryPrice != 0m ? _pendingEntryPrice : GetLastPriceSafe());
                    _trailMaxReached = _trailMinReached = _trailBaselinePx;

                    // Continuar con armado de BE/Trailing (ya hay SL vivo que podremos mover)
                    if (EnableLogging) DebugLog.W("RM/PLAN", "Attach DONE (SOLO SL)");
                    _pendingAttach = false;
                    _pendingPrevBarIdxAtFill = -1; // limpiar el ancla para la siguiente entrada

                    // Armar BE/Trailing será manejado por los bloques subsiguientes
                    // que ya están fuera del bucle de brackets
                }
                else
                {
                    // === CASO NORMAL: TPs con brackets OCO ===
                    // Reparto de cantidades por splits (sin ceros) - usar targetForEnforce
                    var splitQty = _RmSplitHelper.SplitQty(targetForEnforce, tpSplits);
                    if (EnableLogging) DebugLog.W("RM/SPLIT", $"total={targetForEnforce} -> [{string.Join(",", splitQty)}]");

                    for (int idx = 0; idx < plan.TakeProfits.Count && idx < splitQty.Length; idx++)
                {
                    var q = splitQty[idx];
                    if (q <= 0) { if (EnableLogging) DebugLog.W("RM/ORD", $"PAIR #{idx + 1}: skip (qty=0)"); continue; }
                    var tp = plan.TakeProfits[idx];

                    // === VALIDACIÃ“N: NO enviar TP con precio â‰¤ 0 ===
                    if (tp.Price <= 0m)
                    {
                        if (EnableLogging) DebugLog.W("RM/ORD", $"PAIR #{idx + 1}: SKIP TP (price=0)");
                        continue;
                    }

                    var ocoId = Guid.NewGuid().ToString("N");
                    var slPriceToUse = overrideStopPx ?? plan.StopLoss.Price;
                    SubmitRmStop(ocoId, coverSide, q, slPriceToUse);
                    SubmitRmLimit(ocoId, coverSide, q, tp.Price);
                    if (EnableLogging)
                        DebugLog.W("RM/ORD", $"PAIR #{idx + 1}: OCO SL {q}@{slPriceToUse:F2} + TP {q}@{tp.Price:F2} (dir={(dir>0?"LONG":"SHORT")})");
                    }

                    _pendingAttach = false;
                    _pendingPrevBarIdxAtFill = -1; // limpiar el ancla para la siguiente entrada
                    if (EnableLogging) DebugLog.W("RM/PLAN", "Attach DONE");
                } // fin del else (caso normal con TPs)

                // Armar el BE (vigilancia del TP trigger para mover SL a breakeven)
                if (BreakEvenMode != RmBeMode.Off)
                {
                    // NUEVO: Leer R múltiple desde Targets UI (no legacy TP1R/TP2R/TP3R)
                    // Funciona incluso si TP tiene % cobro = 0 (TP virtual para trigger BE)
                    decimal tpRMultiple = GetBeRMultipleFromTargets();

                    // LOG DE DIAGNÓSTICO: Verificar qué TP row se está usando
                    var tgt = Targets;
                    var row = BeTriggerTp == 1 ? tgt?.TP1 : BeTriggerTp == 2 ? tgt?.TP2 : tgt?.TP3;
                    if (EnableLogging)
                        DebugLog.W("RM/BE/CHECK", $"triggerTP={BeTriggerTp} uses R={row?.R:F2} pct={row?.Percent}% active={row?.Active} (virtual={(row?.Percent ?? 0) == 0})");

                    // Calcular distancia R desde el stop
                    var slPx = plan.StopLoss?.Price ?? 0m;
                    var r = dir > 0 ? (entryPx - slPx) : (slPx - entryPx);
                    if (r <= 0) r = tickSize; // fallback

                    // Precio de TP = entry Â± (R Ã— mÃºltiple)
                    var tpPxRaw = entryPx + (dir > 0 ? (tpRMultiple * r) : -(tpRMultiple * r));
                    _beTargetPx = ShrinkPrice(tpPxRaw);  // tick-safe
                    _beDirHint  = dir;
                    _beArmed    = true;
                    _beDone     = false;

                    // Capturar precio ACTUAL al armar BE (baseline para tracking)
                    try
                    {
                        var currentCandle = GetCandle(Math.Max(0, CurrentBar - 1));
                        _beArmedAtPrice = currentCandle?.Close ?? entryPx;
                        _beMaxReached = 0m;  // Se inicializarÃ¡ en primer tick de GetLastPriceTriplet
                        _beMinReached = 0m;
                    }
                    catch
                    {
                        _beArmedAtPrice = entryPx;
                        _beMaxReached = 0m;
                        _beMinReached = 0m;
                    }

                    if (EnableLogging)
                        DebugLog.W("RM/BE", $"ARMED → mode={BreakEvenMode} triggerTP={BeTriggerTp} R={tpRMultiple:F1} tpPx={_beTargetPx:F2} baseline={_beArmedAtPrice:F2} dir={(_beDirHint>0?"LONG":"SHORT")}");
                }
                else
                {
                    _beArmed = _beDone = false; _beTargetPx = 0m; _beDirHint = 0;
                    _beArmedAtPrice = _beMaxReached = _beMinReached = 0m;
                }

                // === TRAILING: armar baseline si está activo ===
                if (TrailingMode != RmTrailMode.Off)
                {
                    try
                    {
                        var currentCandle = GetCandle(Math.Max(0, CurrentBar - 1));
                        var px = currentCandle?.Close ?? entryPx;
                        _trailArmed = true;
                        _trailBaselinePx = px;
                        _trailMaxReached = px;
                        _trailMinReached = px;
                        _trailLastStepIdx = -1;
                        _lastTrailMoveBar = CurrentBar;
                        TLog("ARMED", $"mode={TrailingMode} base={px:F2} distTicks={TrailDistanceTicks} " +
                                    $"confirm={TrailConfirmBars} entry={_pendingEntryPrice:F2}/{_currentPositionEntryPrice:F2} " +
                                    $"stopTracked={_lastKnownStopPx:F2} riskAbs={_trailRiskAbs:F2}");
                    }
                    catch
                    {
                        _trailArmed = false;
                        if (EnableLogging) DebugLog.W("RM/TRAIL", "FAILED to arm (exception getting current candle)");
                    }
                }
                else
                {
                    _trailArmed = false;
                    _trailLastStepIdx = -1;
                    _trailBaselinePx = _trailMaxReached = _trailMinReached = 0m;
                    _lastTrailMoveBar = -1;
                }
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/PLAN", $"TryAttachBracketsNow EX: {ex.Message}");
            }
        }

        // ====================== RM ORDER SUBMISSION HELPERS ======================
        private void SubmitRmStop(string oco, OrderDirections side, int qty, decimal triggerPx)
        {
            if (qty <= 0) { if (EnableLogging) DebugLog.W("RM/ORD", "SubmitRmStop SKIP: qty<=0"); return; }
            var comment = $"{OwnerPrefix}SL:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop ENTER: side={side} qty={qty} triggerPx={triggerPx:F2} oco={oco} comment={comment}");

            try
            {
                var shrunkPx = ShrinkPrice(triggerPx);
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop: ShrinkPrice({triggerPx:F2}) ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Stop,
                    TriggerPrice = shrunkPx, // ÃƒÂ¢Ã¢â‚¬ Ã‚Â tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // ÃƒÂ¢Ã¢â‚¬ Ã‚Â cancelar al cerrar
                TrySetReduceOnly(order);             // ÃƒÂ¢Ã¢â‚¬ Ã‚Â no abrir nuevas
                TrySetCloseOnTrigger(order);         // ÃƒÂ¢Ã¢â‚¬ Ã‚Â cerrar al disparar

                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmStop: Calling OpenOrder() for SL");
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"STOP SENT: {side} {qty} @{order.TriggerPrice:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmStop EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        private void SubmitRmLimit(string oco, OrderDirections side, int qty, decimal price)
        {
            if (qty <= 0) { if (EnableLogging) DebugLog.W("RM/ORD", "SubmitRmLimit SKIP: qty<=0"); return; }
            var comment = $"{OwnerPrefix}TP:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit ENTER: side={side} qty={qty} price={price:F2} oco={oco} comment={comment}");

            try
            {
                var shrunkPx = ShrinkPrice(price);
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit: ShrinkPrice({price:F2}) ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ {shrunkPx:F2}");

                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Limit,
                    Price = shrunkPx,       // ÃƒÂ¢Ã¢â‚¬ Ã‚Â tick-safe
                    QuantityToFill = qty,
                    OCOGroup = oco,
                    IsAttached = true,
                    Comment = comment
                };
                order.AutoCancel = true;             // ÃƒÂ¢Ã¢â‚¬ Ã‚Â cancelar al cerrar
                TrySetReduceOnly(order);             // ÃƒÂ¢Ã¢â‚¬ Ã‚Â no abrir nuevas

                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmLimit: Calling OpenOrder() for TP");
                OpenOrder(order);
                DebugLog.W("RM/ORD", $"LIMIT SENT: {side} {qty} @{order.Price:F2} OCO={(oco ?? "none")}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmLimit EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        // === Market (enforcement) ===
        private void SubmitRmMarket(OrderDirections side, int qty)
        {
            var comment = $"{OwnerPrefix}ENF:{Guid.NewGuid():N}";
            if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmMarket ENTER: side={side} qty={qty} comment={comment}");

            try
            {
                var order = new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = side,
                    Type = OrderTypes.Market,
                    QuantityToFill = qty,
                    Comment = comment
                };
                // IMPORTANTE: no marcar ReduceOnly aquÃƒÆ’Ã‚Â­ ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â queremos abrir delta
                if (EnableLogging) DebugLog.W("RM/ORD", $"SubmitRmMarket: Calling OpenOrder() for {side} +{qty} (ReduceOnly=false)");
                OpenOrder(order);
                if (EnableLogging) DebugLog.W("RM/ORD", $"ENFORCE MARKET SENT: {side} +{qty} @{GetLastPriceSafe():F2} comment={comment}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/ORD", $"SubmitRmMarket EXCEPTION: {ex.Message} | Stack: {ex.StackTrace}");
            }
        }

        private decimal GetLastPriceSafe()
        {
            try
            {
                var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                return GetCandle(barIdx).Close;
            }
            catch { return 0m; }
        }

        private (decimal Last, decimal High, decimal Low) GetLastPriceTriplet()
        {
            try
            {
                var barIdx = Math.Max(0, Math.Min(CurrentBar - 1, CurrentBar));
                var c = GetCandle(barIdx);
                if (c == null) return (0m, 0m, 0m);

                // Si BE no armado â†’ datos normales de barra
                if (!_beArmed || _beArmedAtPrice == 0m)
                    return (c.Close, c.High, c.Low);

                // BE armado â†’ trackear extremos SOLO con el Ãºltimo precio (post-armado)
                var last = GetLastPriceSafe();  // Ãºltimo trade/close del tick actual
                if (_beMaxReached == 0m) _beMaxReached = _beArmedAtPrice;
                if (_beMinReached == 0m) _beMinReached = _beArmedAtPrice;

                _beMaxReached = Math.Max(_beMaxReached, last);
                _beMinReached = Math.Min(_beMinReached, last);

                return (last, _beMaxReached, _beMinReached);
            }
            catch { }
            return (0m, 0m, 0m);
        }

        // ==== Helpers: RM brackets detection & cleanup ====
        private bool HasLiveRmBrackets(bool includeNone = false)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return false;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!(c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:"))) continue;
                    var st = o.Status();
                    // Consider LIVE only when actively working according to ATAS enum:
                    // Placed (working) or PartlyFilled (still has remainder). Ignore None/Filled/Canceled.
                    // Considera temporalmente "None" como working si asÃ­ lo pedimos (latencia de registro)
                    if (!o.Canceled && (st == OrderStatus.Placed || st == OrderStatus.PartlyFilled || (includeNone && st == OrderStatus.None)))
                        return true;
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"HasLiveRmBrackets EX: {ex.Message}");
            }
            return false;
        }

        private void LogOrderStateHistogram(string tag)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;
                int slPlaced = 0, slPart = 0, tpPlaced = 0, tpPart = 0;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    var st = o.Status();
                    bool isSL = c.StartsWith(OwnerPrefix + "SL:");
                    bool isTP = c.StartsWith(OwnerPrefix + "TP:");
                    if (isSL && st == OrderStatus.Placed) slPlaced++;
                    else if (isSL && st == OrderStatus.PartlyFilled) slPart++;
                    else if (isTP && st == OrderStatus.Placed) tpPlaced++;
                    else if (isTP && st == OrderStatus.PartlyFilled) tpPart++;
                }
                if (EnableLogging)
                    DebugLog.W("RM/STATES", $"{tag}: SL(placed={slPlaced}, partly={slPart}) TP(placed={tpPlaced}, partly={tpPart})");
            }
            catch { }
        }

        private void CancelResidualBrackets(string reason)
        {
            try
            {
                var list = this.Orders;
                if (list == null) return;
                int canceled = 0;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!(c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:"))) continue;
                    var st = o.Status();
                    if (o.Canceled || st == OrderStatus.Canceled || st == OrderStatus.Filled) continue;
                    try { CancelOrder(o); canceled++; } catch { }
                }
                if (EnableLogging) DebugLog.W("RM/CLEAN", $"Canceled residual RM brackets (n={canceled}) reason='{reason}'");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"CancelResidualBrackets EX: {ex.Message}");
            }
        }

        private void CancelNonBracketWorkingOrders(string reason)
        {
            try
            {
                // UniÃƒÆ’Ã‚Â³n: ÃƒÆ’Ã‚Â³rdenes de la estrategia + externas detectadas (ChartTrader)
                var union = new System.Collections.Generic.List<Order>();
                if (this.Orders != null) union.AddRange(this.Orders);
                lock (_liveOrdersLock) union.AddRange(_liveOrders);

                var seen = new System.Collections.Generic.HashSet<string>();
                int canceled = 0, considered = 0;
                foreach (var o in union)
                {
                    if (o == null) continue;
                    // Mismo instrumento/portfolio (comparaciÃƒÆ’Ã‚Â³n laxa por ToString para evitar tipos internos)
                    if (o.Security?.ToString() != Security?.ToString()) continue;
                    if (o.Portfolio?.ToString() != Portfolio?.ToString()) continue;
                    var oid = o.Id ?? $"{o.GetHashCode()}";
                    if (!seen.Add(oid)) continue;

                    var c  = o.Comment ?? "";
                    var st = o.Status();
                    var isBracket = c.StartsWith(OwnerPrefix + "SL:") || c.StartsWith(OwnerPrefix + "TP:");
                    var isMyFlat  = c.StartsWith(OwnerPrefix + "STPFLAT:");
                    var isLive    = !o.Canceled && st != OrderStatus.Canceled && st != OrderStatus.Filled; // inclusivo: None/Placed/PartlyFilled
                    if (EnableLogging)
                        DebugLog.W("RM/CLEAN", $"consider cancel: id={oid} c='{c}' st={st} canceled={o.Canceled} isBracket={isBracket} isMyFlat={isMyFlat} isLive={isLive}");

                    if (isBracket || isMyFlat) continue; // brackets ya se limpian; no matar STPFLAT
                    considered++;
                    if (isLive)
                    {
                        if (TryCancelAnyOrder(o)) canceled++;
                    }
                }
                if (EnableLogging) DebugLog.W("RM/CLEAN", $"Canceled non-bracket working orders (n={canceled}/{considered}) reason='{reason}'");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"CancelNonBracketWorkingOrders EX: {ex.Message}");
            }
        }

        // Try to cancel with both Strategy API and TradingManager (external orders)
        private bool TryCancelAnyOrder(Order o)
        {
            try
            {
                // 1) Strategy-owned way (works for this strategy orders)
                try { CancelOrder(o); return true; } catch { /* might not belong to strategy */ }

                // 2) TradingManager (platform-level) ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â sync variant
                var tm = this.TradingManager;
                if (tm != null)
                {
                    var mi = tm.GetType().GetMethod("CancelOrder", new[] { typeof(Order), typeof(bool), typeof(bool) });
                    if (mi != null)
                    {
                        mi.Invoke(tm, new object[] { o, false, false });
                        return true;
                    }
                    // 2b) Async variant
                    var mia = tm.GetType().GetMethod("CancelOrderAsync", new[] { typeof(Order), typeof(bool), typeof(bool) });
                    if (mia != null)
                    {
                        var task = (System.Threading.Tasks.Task)mia.Invoke(tm, new object[] { o, false, false });
                        // fire-and-forget; assume submitted
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/ERR", $"TryCancelAnyOrder EX: {ex.Message}");
            }
            return false;
        }

        // === Additional order options via TradingManager ===
        private void TrySetReduceOnly(Order order)
        {
            try
            {
                var flags = TradingManager?
                    .GetSecurityTradingOptions()?
                    .CreateExtendedOptions(order.Type);
                if (flags is ATAS.DataFeedsCore.IOrderOptionReduceOnly ro)
                {
                    ro.ReduceOnly = true;   // evita abrir posiciÃƒÆ’Ã‚Â³n nueva
                    order.ExtendedOptions = flags;
                }
            } catch { }
        }

        // Flatten por TradingManager (cascada de firmas). Devuelve true si se invocÃƒÆ’Ã‚Â³ alguna variante.
        private bool TryClosePositionViaTradingManager()
        {
            try
            {
                var tm = this.TradingManager;
                if (tm == null)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", "TradingManager null ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ fallback MARKET");
                    return false;
                }

                var tmt = tm.GetType();

                // A) Con Position del TM (si existe)
                object posObj = null;
                try { posObj = tmt.GetProperty("Position")?.GetValue(tm); } catch { /* ignore */ }
                if (posObj == null)
                {
                    // Fallback: intentar obtener Position desde Portfolio.GetPosition(Security)
                    posObj = GetAtasPositionObject();
                }

                // 1) ClosePosition(Position, bool, bool)
                if (posObj != null)
                {
                    var mi1 = tmt.GetMethod("ClosePosition", new[] { posObj.GetType(), typeof(bool), typeof(bool) });
                    if (mi1 != null)
                    {
                        var ret = mi1.Invoke(tm, new object[] { posObj, false, true });
                        if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Position,false,true) invoked");
                        return (ret as bool?) ?? true;
                    }

                    // 2) ClosePositionAsync(Position, bool, bool)
                    var mi2 = tmt.GetMethod("ClosePositionAsync", new[] { posObj.GetType(), typeof(bool), typeof(bool) });
                    if (mi2 != null)
                    {
                        try
                        {
                            var task = mi2.Invoke(tm, new object[] { posObj, false, true }) as System.Threading.Tasks.Task;
                            if (EnableLogging) DebugLog.W("RM/STOP", "ClosePositionAsync(Position,false,true) invoked");
                            return true; // asumimos submit correcto
                        }
                        catch (Exception exa)
                        {
                            if (EnableLogging) DebugLog.W("RM/STOP", $"ClosePositionAsync EX: {exa.Message}");
                        }
                    }
                }

                // 3) ClosePosition(Portfolio, Security, bool, bool)
                var mi3 = tmt.GetMethod("ClosePosition", new[] { Portfolio?.GetType(), Security?.GetType(), typeof(bool), typeof(bool) });
                if (mi3 != null)
                {
                    mi3.Invoke(tm, new object[] { Portfolio, Security, true, true });
                    if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Portfolio,Security,true,true) invoked");
                    return true;
                }

                // 4) ClosePosition(Portfolio, Security)
                var mi4 = tmt.GetMethod("ClosePosition", new[] { Portfolio?.GetType(), Security?.GetType() });
                if (mi4 != null)
                {
                    mi4.Invoke(tm, new object[] { Portfolio, Security });
                    if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition(Portfolio,Security) invoked");
                    return true;
                }

                if (EnableLogging) DebugLog.W("RM/STOP", "ClosePosition* not found ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ fallback MARKET");
                return false;
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/STOP", $"TryClosePositionViaTradingManager EX: {ex.Message} ÃƒÂ¢Ã¢â‚¬ Ã¢â‚¬â„¢ fallback MARKET");
                return false;
            }
        }

        private void TrySetCloseOnTrigger(Order order)
        {
            try
            {
                var flags = TradingManager?
                    .GetSecurityTradingOptions()?
                    .CreateExtendedOptions(order.Type);
                if (flags is ATAS.DataFeedsCore.IOrderOptionCloseOnTrigger ct)
                {
                    ct.CloseOnTrigger = true; // cerrar cuando dispare
                    order.ExtendedOptions = flags;
                }
            } catch { }
        }

        private bool HasLiveOrdersWithPrefix(string prefix)
        {
            try
            {
                var list = this.Orders; // Strategy.Orders
                if (list == null) return false;
                return list.Any(o =>
                {
                    var c = o?.Comment ?? "";
                    if (!c.StartsWith(prefix)) return false;
                    // consideramos viva si NO estÃƒÆ’Ã‚Â¡ cancelada y NO estÃƒÆ’Ã‚Â¡ llena
                    var st = o.Status();
                    return !o.Canceled
                           && st != OrderStatus.Filled
                           && st != OrderStatus.Canceled;
                });
            }
            catch { return false; }
        }

        // Log de cancelaciÃƒÆ’Ã‚Â³n fallida (para ver por quÃƒÆ’Ã‚Â© queda algo "working")
        protected override void OnOrderCancelFailed(Order order, string message)
        {
            if (!EnableLogging) return;
            try
            {
                DebugLog.W("RM/STOP", $"OnOrderCancelFailed: id={order?.Id} comment='{order?.Comment}' status={order?.Status()} msg={message}");
            } catch { }
        }

        // ========= Helpers de neta y flatten =========
        // Siempre usa el snapshot de CUENTA (TM.Position/Portfolio). No uses CurrentPosition aquÃƒÆ’Ã‚Â­.
        private int ReadNetPositionSafe()
        {
            try { return ReadNetPosition(); } catch { return 0; }
        }

        private bool HasWorkingOrdersWithPrefix(string prefix)
        {
            try
            {
                var list = this.Orders; if (list == null) return false;
                foreach (var o in list)
                {
                    var c = o?.Comment ?? "";
                    if (!c.StartsWith(prefix)) continue;
                    var st = o.Status();
                    // "working" en ATAS = Placed o PartlyFilled (evitamos None/Filled/Canceled)
                    if (!o.Canceled && (st == OrderStatus.Placed || st == OrderStatus.PartlyFilled))
                        return true;
                }
            } catch { }
            return false;
        }

        // EnvÃƒÆ’Ã‚Â­a (si hace falta) la orden MARKET reduce-only para quedar flat.
        // Evita duplicarla si ya hay una STPFLAT "working".
        private void EnsureFlattenOutstanding(string reason)
        {
            try
            {
                var net = ReadNetPositionSafe();
                if (net == 0)
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding: already flat ({reason})");
                    return;
                }
                if (HasWorkingOrdersWithPrefix(OwnerPrefix + "STPFLAT:"))
                {
                    if (EnableLogging) DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding: STPFLAT already working ({reason})");
                    return;
                }
                var side = net > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                var qty  = Math.Abs(net);
                var comment = $"{OwnerPrefix}STPFLAT:{Guid.NewGuid():N}";
                var o = new Order
                {
                    Portfolio      = Portfolio,
                    Security       = Security,
                    Direction      = side,
                    Type           = OrderTypes.Market,
                    QuantityToFill = qty,
                    Comment        = comment
                };
                TrySetReduceOnly(o); // evita abrir si hay desincronizaciÃƒÆ’Ã‚Â³n
                OpenOrder(o);
                if (EnableLogging) DebugLog.W("RM/STOP", $"Flatten MARKET sent: {side} {qty} ({reason}) comment={comment}");
            }
            catch (Exception ex)
            {
                DebugLog.W("RM/STOP", $"EnsureFlattenOutstanding EX: {ex.Message}");
            }
        }

        // ====================== Session P&L Tracking ======================

        private void UpdateSessionPnL()
        {
            try
            {
                // Usar nuestras variables de tracking internas en lugar de ReadPositionSnapshot()
                // porque ReadPositionSnapshot() puede devolver 0 en replay/simulaciÃ³n
                var currentQty = _currentPositionQty;
                var avgPrice = _currentPositionEntryPrice;

                // Calcular P&L no realizado de posiciÃ³n abierta
                decimal unrealizedPnL = 0m;
                if (currentQty != 0 && avgPrice > 0m)
                {
                    var lastPrice = GetLastPriceSafe();
                    var tickValue = ResolveTickValueUSD();
                    var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);

                    // P&L = (LastPrice - AvgPrice) Ã— Qty Ã— (TickValue / TickSize)
                    // Para SHORT: P&L = (AvgPrice - LastPrice) Ã— Qty Ã— (TickValue / TickSize)
                    var priceDiff = currentQty > 0 ? (lastPrice - avgPrice) : (avgPrice - lastPrice);
                    var ticks = priceDiff / tickSize;
                    unrealizedPnL = ticks * tickValue * Math.Abs(currentQty);

                    if (EnableLogging && Math.Abs(unrealizedPnL) > 0.01m)
                        DebugLog.W("RM/PNL", $"Unrealized calc: currentQty={currentQty} entry={avgPrice:F2} last={lastPrice:F2} priceDiff={priceDiff:F2} ticks={ticks:F2} tickVal={tickValue:F2} â†’ unrealized={unrealizedPnL:F2}");
                }

                // Actualizar propiedades separadas
                SessionUnrealized = Math.Round(unrealizedPnL, 2);
                SessionPnL = Math.Round(_sessionRealizedPnL
                               + (IncludeUnrealizedInSession ? SessionUnrealized : 0m), 2);

                if (EnableLogging && (Math.Abs(SessionPnL) > 0.01m || Math.Abs(unrealizedPnL) > 0.01m))
                    DebugLog.W("RM/PNL", $"SessionPnL={SessionPnL:F2} (Realized={_sessionRealizedPnL:F2} Unrealized={unrealizedPnL:F2} IncludeUnreal={IncludeUnrealizedInSession})");
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"UpdateSessionPnL EX: {ex.Message}");
            }
        }

        // ====================== TRAILING STOP Processing ======================

        // === Helpers de vela/seguridad ===
        private IndicatorCandle SafeGetCandle(int idx)
        {
            if (idx < 0) return null;
            try { return GetCandle(idx); }
            catch { return null; }
        }

        private bool TryGetCurrentStop(out decimal stopPx)
        {
            // En este archivo no existe GetAttachedStopPriceSafe(); mantenemos nuestro tracking.
            stopPx = _lastKnownStopPx;
            if (stopPx <= 0m) { TLog("GATE", "STOP-READ-FAIL lastKnown=0"); return false; }
            TLog("READ", $"STOP={stopPx:F2}");
            return true;
        }

        // Calcula precio objetivo por múltiplo de R usando el riesgo capturado al poner el primer SL.
        private decimal CalcTargetPriceByR(decimal entry, int dir, decimal rMultiple)
        {
            if (_trailRiskAbs <= 0m || entry <= 0m || dir == 0) return 0m;
            var delta = rMultiple * _trailRiskAbs;
            return dir > 0 ? entry + delta : entry - delta;
        }

        private (decimal tp1, decimal tp2, decimal tp3) GetTpPricesForR()
        {
            var entry = _pendingEntryPrice != 0m ? _pendingEntryPrice : _currentPositionEntryPrice;
            var dir   = Math.Sign(_currentPositionQty != 0 ? _currentPositionQty : _beDirHint);
            if (entry == 0m || dir == 0) { TLog("GATE", $"TP-PRICES sin entry/dir (e={entry:F2}, dir={dir})"); return (0m,0m,0m); }

            var t  = Targets;
            var r1 = Math.Max(0.0000001m, t?.TP1?.R ?? 1m);
            var r2 = Math.Max(0m,           t?.TP2?.R ?? 0m);
            var r3 = Math.Max(0m,           t?.TP3?.R ?? 0m);

            var p1 = CalcTargetPriceByR(entry, dir, r1);
            var p2 = r2 > 0m ? CalcTargetPriceByR(entry, dir, r2) : 0m;
            var p3 = r3 > 0m ? CalcTargetPriceByR(entry, dir, r3) : 0m;
            TLog("TP", $"R: {r1:F2}/{r2:F2}/{r3:F2}  Px: {p1:F2}/{p2:F2}/{p3:F2}  risk={_trailRiskAbs:F2}");
            return (p1,p2,p3);
        }

        private int GetHighestTouchedStepForDir(int dir)
        {
            var (tp1, tp2, tp3) = GetTpPricesForR();
            int touched = -1;
            if (tp1 == 0m) return -1;
            if (dir > 0) {
                if (_trailMaxReached >= tp1) touched = 1;
                if (tp2 > 0m && _trailMaxReached >= tp2) touched = 2;
                if (tp3 > 0m && _trailMaxReached >= tp3) touched = 3;
            } else if (dir < 0) {
                if (_trailMinReached <= tp1) touched = 1;
                if (tp2 > 0m && _trailMinReached <= tp2) touched = 2;
                if (tp3 > 0m && _trailMinReached <= tp3) touched = 3;
            }
            TLog("STEP", $"dir={(dir>0?"LONG":"SHORT")} touched={touched} last={_trailLastStepIdx} " +
                         $"max={_trailMaxReached:F2} min={_trailMinReached:F2}");
            return touched;
        }

        private decimal TickSizeDec => Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);
        private decimal AddTicks(decimal price, int ticks, int sign) => price + (ticks * sign) * TickSizeDec;
        private bool BetterForLong(decimal newStop, decimal currStop) => newStop > currStop + 0.0000001m;
        private bool BetterForShort(decimal newStop, decimal currStop) => newStop < currStop - 0.0000001m;

        private void ProcessTrailing(IndicatorCandle current)
        {
            if (TrailingMode == RmTrailMode.Off) { TLog("GATE", "MODE=Off"); return; }
            if (!_trailArmed) { TLog("GATE", "NOT-ARMED"); return; }
            // En este archivo no hay deferral de fills; gate omitido.
            if (_currentPositionQty == 0) { TLog("GATE", "FLAT"); return; }
            if (_lastTrailMoveBar == CurrentBar) { TLog("GATE", "RATE-LIMIT one-move-per-bar"); return; }

            var dir = Math.Sign(_currentPositionQty);
            if (dir == 0) { TLog("GATE", "DIR=0"); return; }

            if (!TryGetCurrentStop(out var currStop)) return;

            decimal newStop = 0m;
            string  reason  = null;

            if (TrailingMode == RmTrailMode.TpToTp)
            {
                var touched = GetHighestTouchedStepForDir(dir);
                if (touched <= _trailLastStepIdx) { TLog("GATE", $"NO-NEW-STEP touched={touched} last={_trailLastStepIdx}"); return; }

                var (tp1, tp2, tp3) = GetTpPricesForR();
                var entryPx = _pendingEntryPrice != 0m ? _pendingEntryPrice : _currentPositionEntryPrice;
                var ts = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);

                if (touched == 1)
                {
                    // Primer salto: mover al BE con su offset
                    newStop = ComputeBePrice(dir, entryPx, ts);
                    reason  = $"TpToTp step=1 → BE (offset={BeOffsetTicks})";
                }
                else if (touched == 2)
                {
                    newStop = dir > 0 ? AddTicks(tp1, TrailDistanceTicks, -1)
                                      : AddTicks(tp1, TrailDistanceTicks, +1);
                    reason  = $"TpToTp step=2 ref={tp1:F2}";
                }
                else if (touched == 3)
                {
                    newStop = dir > 0 ? AddTicks(tp2, TrailDistanceTicks, -1)
                                      : AddTicks(tp2, TrailDistanceTicks, +1);
                    reason  = $"TpToTp step=3 ref={tp2:F2}";
                }

                if (newStop > 0m)
                {
                    TLog("CALC", $"TpToTp step={touched} newStop={newStop:F2} reason={reason}");
                }
                _trailLastStepIdx = touched;
            }
            else if (TrailingMode == RmTrailMode.BarByBar)
            {
                // BarByBar clásico: usa la vela N-1 (prev bar extreme ± offset)
                var prevBar = SafeGetCandle(CurrentBar - 1);
                if (prevBar == null) { TLog("GATE", "BarByBar prev-bar null"); return; }

                if (dir > 0)
                {
                    // LONG: candidato = low de N-1 - distanceTicks (más alto = mejora)
                    newStop = AddTicks(prevBar.Low, TrailDistanceTicks, -1);
                    reason = $"BarByBar LONG prevLow={prevBar.Low:F2}";
                }
                else
                {
                    // SHORT: candidato = high de N-1 + distanceTicks (más bajo = mejora)
                    newStop = AddTicks(prevBar.High, TrailDistanceTicks, +1);
                    reason = $"BarByBar SHORT prevHigh={prevBar.High:F2}";
                }
                TLog("CALC", $"{reason} dist={TrailDistanceTicks} newStop={newStop:F2}");
            }

            if (newStop <= 0m) { TLog("GATE", "newStop<=0"); return; }

            bool isBetter = dir > 0 ? BetterForLong(newStop, currStop)
                                    : BetterForShort(newStop, currStop);
            if (!isBetter) { TLog("KEEP", $"no-better curr={currStop:F2} cand={newStop:F2}"); return; }

            TLog("MOVE", $"from={currStop:F2} to={newStop:F2} dir={(dir>0?"LONG":"SHORT")} reason={reason}");
            // En este archivo TryModifyStopInPlace(Order,px) no sirve; usa el wrapper existente:
            MoveAllRmStopsTo(newStop, reason ?? "TRAIL");
            _lastTrailMoveBar = CurrentBar;
        }

        private void TrackPositionClose(decimal exitPrice, int qty, int direction)
        {
            try
            {
                if (_currentPositionQty == 0 || _currentPositionEntryPrice == 0m)
                {
                    if (EnableLogging) DebugLog.W("RM/PNL", "TrackPositionClose: No position tracked to close");
                    return;
                }

                var tickValue = ResolveTickValueUSD();
                var tickSize = Convert.ToDecimal(Security?.TickSize ?? FallbackTickSize);

                // Calcular P&L de la posiciÃ³n cerrada
                // Para LONG: P&L = (ExitPrice - EntryPrice) Ã— Qty Ã— (TickValue / TickSize)
                // Para SHORT: P&L = (EntryPrice - ExitPrice) Ã— Qty Ã— (TickValue / TickSize)
                var priceDiff = direction > 0 ? (exitPrice - _currentPositionEntryPrice) : (_currentPositionEntryPrice - exitPrice);
                var ticks = priceDiff / tickSize;
                var tradePnL = ticks * tickValue * Math.Abs(qty);

                _sessionRealizedPnL += tradePnL;

                if (EnableLogging)
                    DebugLog.W("RM/PNL", $"Position close: Entry={_currentPositionEntryPrice:F2} Exit={exitPrice:F2} Qty={qty} Dir={direction} â†’ P&L={tradePnL:F2} (Total Realized={_sessionRealizedPnL:F2})");

                // Actualizar posición actual conservando el signo (LONG/SHORT)
                var remainingAbs = Math.Abs(_currentPositionQty) - Math.Abs(qty);
                if (remainingAbs <= 0)
                {
                    _currentPositionQty = 0;
                    _currentPositionEntryPrice = 0m;
                    if (EnableLogging) DebugLog.W("RM/PNL", "Position fully closed");
                }
                else
                {
                    var sign = Math.Sign(_currentPositionQty); // +1 long, -1 short
                    _currentPositionQty = remainingAbs * sign;
                    if (EnableLogging) DebugLog.W("RM/PNL", $"Position partially closed → remainingQty={_currentPositionQty}");
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"TrackPositionClose EX: {ex.Message}");
            }
        }

        private void TrackPositionEntry(decimal entryPrice, int qty, int direction)
        {
            try
            {
                if (_currentPositionQty == 0)
                {
                    // Nueva posiciÃ³n
                    _currentPositionEntryPrice = entryPrice;
                    _currentPositionQty = qty * direction;  // signed: +LONG / -SHORT
                    if (EnableLogging)
                        DebugLog.W("RM/PNL", $"Position entry: Price={entryPrice:F2} Qty={qty} Dir={direction} â†’ Tracking started");
                }
                else
                {
                    // Incremento de posiciÃ³n existente (promedio ponderado)
                    var totalQty = Math.Abs(_currentPositionQty) + Math.Abs(qty);
                    _currentPositionEntryPrice = (_currentPositionEntryPrice * Math.Abs(_currentPositionQty) + entryPrice * Math.Abs(qty)) / totalQty;
                    _currentPositionQty = totalQty * direction;
                    if (EnableLogging)
                        DebugLog.W("RM/PNL", $"Position add: NewEntry={entryPrice:F2} Qty={qty} â†’ AvgEntry={_currentPositionEntryPrice:F2} TotalQty={totalQty}");
                }
            }
            catch (Exception ex)
            {
                if (EnableLogging) DebugLog.W("RM/PNL", $"TrackPositionEntry EX: {ex.Message}");
            }
        }

    }
}