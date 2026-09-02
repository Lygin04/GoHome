using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GoHome.Ui.Design;

/// <summary>Что набирают в поле — от этого зависят разбор и допустимые знаки.</summary>
internal enum FieldKind
{
    /// <summary>Произвольный текст: пометка к дате, путь.</summary>
    Free,

    /// <summary>Время суток, «9:05» или «09:05».</summary>
    Time,

    /// <summary>Продолжительность, «8:00» — часы и минуты.</summary>
    Duration,

    /// <summary>Дата, «14.08.2026».</summary>
    Date,
}

/// <summary>
/// Поле ввода с проверкой: рамка и состояния нарисованы, ввод настоящий.
/// </summary>
/// <remarks>
/// Одно поле на все случаи. Отдельные контролы под время, продолжительность и дату
/// отличались бы только разбором строки, а выглядели и вели себя одинаково — это была бы
/// не пятёрка примитивов, а четыре копии одного.
/// <para>
/// Не <see cref="DateTimePicker"/> и не <see cref="NumericUpDown"/>: первый перекрасить
/// нельзя вовсе — это системный контрол целиком, — а второй приносит с собой системные
/// стрелки. Да и набрать «09:05» быстрее, чем накрутить его стрелками.
/// </para>
/// <para>
/// Рамку, фон и состояния рисует само поле, а вводом занимается настоящий
/// <see cref="TextBox"/> без рамки внутри. Своя реализация ввода означала бы свой курсор,
/// своё выделение, свой буфер обмена и свой ввод с раскладок — всё это уже написано.
/// </para>
/// </remarks>
internal sealed class DesignField : Control, IPaletteAware
{
    /// <summary>Прозрачность ореола фокуса из дизайна: rgba(accent, .26).</summary>
    private const int FocusHaloAlpha = 66;

    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly TextBox _input;

    public DesignField()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);

        _input = new TextBox { BorderStyle = BorderStyle.None };

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

    /// <summary>Что набирают в поле.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FieldKind Kind
    {
        get;
        set
        {
            field = value;
            _input.MaxLength = value switch
            {
                FieldKind.Time or FieldKind.Duration => 5,
                FieldKind.Date => 10,
                _ => 0,
            };

            Rescale();
            Invalidate();
        }
    } = FieldKind.Free;

    /// <inheritdoc/>
    [AllowNull]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override string Text
    {
        get => _input.Text;
        set
        {
            Filling = true;
            _input.Text = value ?? string.Empty;
            Filling = false;
            Invalidate();
        }
    }

    /// <summary>Время суток. <c>null</c> — набрано то, чего не разобрать.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeOnly? Time
    {
        get => ParseTime(_input.Text);
        set => Text = value?.ToString("HH\\:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Продолжительность.
    /// </summary>
    /// <remarks>
    /// Набирается теми же часами и минутами, что и время суток, поэтому и разбирается так же.
    /// Сутки целиком в поле не влезают — рабочего дня в двадцать четыре часа не бывает,
    /// а двадцать три пятьдесят девять набрать можно.
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeSpan? Duration
    {
        get => Time?.ToTimeSpan();
        set => Time = value is { } span && span < TimeSpan.FromDays(1)
            ? TimeOnly.FromTimeSpan(span)
            : null;
    }

    /// <summary>Дата. <c>null</c> — набрано то, чего не разобрать.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateOnly? Date
    {
        get => DateOnly.TryParseExact(_input.Text.Trim(), ["dd.MM.yyyy", "d.M.yyyy"], Russian, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
        set => Text = value?.ToString("dd.MM.yyyy", Russian) ?? string.Empty;
    }

    /// <summary>
    /// Значение отвергнуто проверкой снаружи.
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
    public bool IsValid => !Rejected && Kind switch
    {
        FieldKind.Time or FieldKind.Duration => Time is not null,
        FieldKind.Date => Date is not null,
        _ => true,
    };

    /// <summary>Поле заполняется кодом, и события при этом сыплются — это не правка человека.</summary>
    private bool Filling { get; set; }

    /// <summary>Ставит фокус в само поле ввода.</summary>
    public new void Focus() => _input.Focus();

    /// <inheritdoc/>
    public void RefreshPalette() => Invalidate();

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
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
            Draw.Focus(e.Graphics, ClientRectangle, Color.FromArgb(FocusHaloAlpha, palette.Accent), metrics);
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

    /// <summary>Разбирает «9:05», «09:05» и «0905» — человек набирает по-разному.</summary>
    private static TimeOnly? ParseTime(string text)
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

    /// <summary>Во время и в дату не набирается ничего, кроме цифр и разделителя.</summary>
    private void RejectForeignKeys(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || Kind == FieldKind.Free)
        {
            return;
        }

        var separator = Kind == FieldKind.Date ? '.' : ':';
        if (!char.IsAsciiDigit(e.KeyChar) && e.KeyChar != separator)
        {
            e.Handled = true;
        }
    }

    private void Rescale()
    {
        var metrics = Metrics.Of(this);

        // Числа моноширинные, произвольный текст — обычным: столбик цифр должен стоять
        // ровно, а пометка к дате читается как текст.
        var fonts = Typography.Of(metrics);
        _input.Font = Kind == FieldKind.Free ? fonts.Control : fonts.Number;

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
