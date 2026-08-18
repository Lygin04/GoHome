using System.ComponentModel;

namespace GoHome.Ui;

/// <summary>
/// Ввод продолжительности часами и минутами. Одним полем с десятичной дробью это
/// вводится хуже: «7,5 часа» человек считает в уме, а «7 ч 30 мин» просто набирает.
/// </summary>
internal sealed class DurationBox : UserControl
{
    private readonly NumericUpDown _hours;
    private readonly NumericUpDown _minutes;

    private bool _filling;

    public DurationBox()
    {
        _hours = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 24,
            Width = 52,
            TextAlign = HorizontalAlignment.Right,
        };

        _minutes = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 59,
            Increment = 5,
            Width = 52,
            TextAlign = HorizontalAlignment.Right,
        };

        _hours.ValueChanged += (_, _) => Announce();
        _minutes.ValueChanged += (_, _) => Announce();

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
        };

        layout.Controls.Add(_hours);
        layout.Controls.Add(new Label { Text = "ч", AutoSize = true, Padding = new Padding(2, 6, 6, 0) });
        layout.Controls.Add(_minutes);
        layout.Controls.Add(new Label { Text = "мин", AutoSize = true, Padding = new Padding(2, 6, 0, 0) });

        Controls.Add(layout);
        AutoSize = true;
        Height = _hours.Height + 4;
    }

    /// <summary>Значение изменил человек, а не код.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Введённая продолжительность.</summary>
    /// <remarks>Контрол собирается кодом и в дизайнер не попадает — сериализовать нечего.</remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeSpan Value
    {
        get => new((int)_hours.Value, (int)_minutes.Value, 0);
        set
        {
            _filling = true;
            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            _hours.Value = Math.Min((int)clamped.TotalHours, (int)_hours.Maximum);
            _minutes.Value = clamped.Minutes;
            _filling = false;
        }
    }

    private void Announce()
    {
        if (!_filling)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}