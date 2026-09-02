using System.ComponentModel;
using System.Globalization;

namespace GoHome.Ui.Design;

/// <summary>
/// Поле времени: часы и минуты, моноширинно, 30 в высоту.
/// </summary>
/// <remarks>
/// Не <see cref="DateTimePicker"/>: тот перекрасить нельзя вовсе — это системный контрол
/// целиком, без режима отрисовки владельцем. В тёмной теме он остаётся светлым.
/// <para>
/// Рамку, фон и состояния рисует само поле, а вводом занимается настоящий
/// <see cref="TextBox"/> без рамки внутри. Своя реализация ввода означала бы свой курсор,
/// своё выделение, свой буфер обмена и свой ввод с раскладок — всё это уже написано,
/// и переписывать это ради рамки не стоит.
/// </para>
/// </remarks>
internal sealed class TimeField : Control
{
    /// <summary>Прозрачность ореола фокуса из дизайна: rgba(accent, .26).</summary>
    private const int FocusHaloAlpha = 66;

    private readonly TextBox _input;

    public TimeField()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);

        _input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            TextAlign = HorizontalAlignment.Left,
            MaxLength = 5,
        };

        _input.GotFocus += (_, _) => Invalidate();
        _input.LostFocus += (_, _) => Invalidate();
        _input.TextChanged += (_, _) => OnValueEdited();
        _input.KeyPress += RejectForeignKeys;

        Controls.Add(_input);
        TabStop = false;

        Size = new Size(Metrics.Of(this).Scale(96), Metrics.Of(this).ControlHeight);
        Rescale();
    }

    /// <summary>Значение изменил человек, а не код.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Введённое время. <c>null</c> — набрано что-то, чего не разобрать.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeOnly? Value
    {
        get => Parse(_input.Text);
        set
        {
            Filling = true;
            _input.Text = value?.ToString("HH\\:mm", CultureInfo.InvariantCulture) ?? string.Empty;
            Filling = false;
            Invalidate();
        }
    }

    /// <summary>
    /// Значение отвергнуто проверкой снаружи — например, конец интервала раньше начала.
    /// </summary>
    /// <remarks>
    /// Отдельно от неразобранного текста: «12:40» разбирается прекрасно, но интервалом
    /// быть перестаёт, и красным поле должно стать в обоих случаях.
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Rejected
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Invalidate();
            }
        }
    }

    /// <summary>Введено то, что можно принять.</summary>
    public bool IsValid => !Rejected && Value is not null;

    /// <summary>Поле заполняется кодом, и события при этом сыплются — это не правка человека.</summary>
    private bool Filling { get; set; }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Colors();
        var metrics = Metrics.Of(this);
        var focused = _input.Focused;

        var face = !Enabled
            ? palette.TextField.Disabled
            : !IsValid ? palette.TextField.Error
            : focused ? palette.TextField.Focus
            : palette.TextField.Rest;

        Draw.Surface(e.Graphics, ClientRectangle, face, metrics.RadiusControl, Draw.LineWidth(metrics));

        _input.BackColor = face.Back;
        _input.ForeColor = face.Ink;

        // Ореол фокуса — тот же контур, что и у остальных примитивов, только полупрозрачный:
        // в дизайне у этой тени нулевой радиус размытия, размывать нечего.
        if (focused && Enabled)
        {
            Draw.Focus(
                e.Graphics,
                ClientRectangle,
                Color.FromArgb(FocusHaloAlpha, palette.Accent),
                metrics);
        }
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _input.Focus();
    }

    /// <inheritdoc/>
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _input.Enabled = Enabled;
        Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Rescale();
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Rescale();
    }

    /// <inheritdoc/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = Metrics.Of(this).ControlHeight;
        Rescale();
    }

    private static Palette Colors() => Palette.Current();

    /// <summary>Разбирает «9:05», «09:05» и «0905» — человек набирает по-разному.</summary>
    private static TimeOnly? Parse(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length == 4 && trimmed.All(char.IsAsciiDigit))
        {
            trimmed = string.Concat(trimmed.AsSpan(0, 2), ":", trimmed.AsSpan(2, 2));
        }

        return TimeOnly.TryParseExact(
            trimmed,
            ["H:mm", "HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Во время не набирается ничего, кроме цифр и двоеточия.</summary>
    private static void RejectForeignKeys(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar) && e.KeyChar != ':')
        {
            e.Handled = true;
        }
    }

    private void Rescale()
    {
        var metrics = Metrics.Of(this);
        _input.Font = Typography.Of(metrics).Number;

        var padding = metrics.Scale(10);
        _input.SetBounds(
            padding,
            Math.Max(0, (Height - _input.PreferredHeight) / 2),
            Math.Max(0, Width - (padding * 2)),
            _input.PreferredHeight);
    }

    private void OnValueEdited()
    {
        // Набранное заново — повод снять внешний отказ: проверять его будет тот, кто ставил.
        Rejected = false;
        Invalidate();

        if (!Filling)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
