using GoHome.Core;

namespace GoHome.Ui.Design;

/// <summary>
/// Правка границ отлучки: начало, конец, длительность и причина отказа.
/// </summary>
/// <remarks>
/// Дизайн вставляет эту панель прямо под выбранную строку списка. Здесь она стоит под
/// списком отдельным блоком: строки списка одной высоты, и расталкивать их ради одной
/// раскрытой означало бы завести второй способ раскладывать список. Смысл тот же —
/// правка рядом с тем, что правится.
/// <para>
/// Причина отказа приходит снаружи, из тех же правил, по которым правка сохраняется.
/// Своя проверка в форме разошлась бы с настоящей ровно тогда, когда это дороже всего.
/// </para>
/// </remarks>
internal sealed class BreakEditor : Panel, IPaletteAware
{
    private readonly DesignField _start = new() { Kind = FieldKind.Time };
    private readonly DesignField _end = new() { Kind = FieldKind.Time };
    private readonly DesignButton _save;
    private readonly DesignButton _cancel;

    private string _refusal = string.Empty;

    public BreakEditor()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);

        _save = new DesignButton { Kind = ButtonKind.Primary, Text = "Сохранить" };
        _save.Click += (_, _) => Accepted?.Invoke(this, EventArgs.Empty);

        _cancel = new DesignButton { Text = "Отмена" };
        _cancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);

        _start.ValueChanged += (_, _) => Revalidate();
        _end.ValueChanged += (_, _) => Revalidate();

        Controls.AddRange([_start, _end, _save, _cancel]);
    }

    /// <summary>Человек нажал «Сохранить».</summary>
    public event EventHandler? Accepted;

    /// <summary>Человек отказался от правки.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Значения изменились — пора спросить, годятся ли они.</summary>
    public event EventHandler? Edited;

    /// <summary>Что набрано сейчас. <c>null</c> — набрано то, чего не разобрать.</summary>
    public (TimeOnly Start, TimeOnly End)? Value =>
        _start.Time is { } from && _end.Time is { } to ? (from, to) : null;

    /// <summary>Высота панели: одна строка полей с подписями и отступами.</summary>
    public int PreferredHeight
    {
        get
        {
            var metrics = Metrics.Of(this);
            var fonts = Typography.Of(metrics);
            return metrics.Space(4)
                + fonts.Label.Height
                + metrics.Space(2)
                + fonts.Caption.Height
                + metrics.Scale(5)
                + metrics.ControlHeight
                + metrics.Space(2)
                + fonts.Caption.Height
                + metrics.Space(4);
        }
    }

    /// <summary>Открывает правку на этих границах.</summary>
    public void Open(DateTimeOffset start, DateTimeOffset end)
    {
        _start.Time = TimeOnly.FromDateTime(start.DateTime);
        _end.Time = TimeOnly.FromDateTime(end.DateTime);
        _refusal = string.Empty;
        Refuse(null);
        PerformLayout();
        Invalidate();
    }

    /// <summary>Показывает причину отказа и запрещает сохранение.</summary>
    public void Refuse(string? refusal)
    {
        _refusal = refusal ?? string.Empty;

        var valid = _refusal.Length == 0 && Value is not null;
        _save.Enabled = valid;
        _start.Rejected = !valid && _start.Time is null;
        _end.Rejected = _refusal.Length > 0 || _end.Time is null;

        Invalidate();
    }

    /// <summary>Ставит фокус в поле начала — правку начинают с него.</summary>
    public void FocusStart() => _start.Focus();

    /// <inheritdoc/>
    public void RefreshPalette()
    {
        BackColor = Palette.Current().Card;
        Invalidate(invalidateChildren: true);
    }

    /// <inheritdoc/>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshPalette();
    }

    /// <inheritdoc/>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var metrics = Metrics.Of(this);
        var fonts = Typography.Of(metrics);
        var inset = metrics.Space(4);

        var top = inset + fonts.Label.Height + metrics.Space(2) + fonts.Caption.Height + metrics.Scale(5);
        var field = metrics.Scale(96);

        _start.SetBounds(inset, top, field, metrics.ControlHeight);
        _end.SetBounds(inset + field + metrics.Space(5), top, field, metrics.ControlHeight);

        _save.FitToText();
        _cancel.FitToText();

        var right = Width - inset;
        _cancel.Location = new Point(right - _cancel.Width, top);
        _save.Location = new Point(_cancel.Left - _save.Width - metrics.Space(2), top);
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        var metrics = Metrics.Of(this);
        var fonts = Typography.Of(metrics);
        var graphics = e.Graphics;

        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            graphics.FillRectangle(back, ClientRectangle);
        }

        Draw.Surface(
            graphics,
            new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)),
            new Face(palette.Card, palette.Line, palette.Ink),
            metrics.RadiusCard,
            Draw.LineWidth(metrics));

        var inset = metrics.Space(4);
        fonts.DrawLabel(graphics, "Правка времени", new Point(inset, inset), palette.Faint);

        var captionTop = inset + fonts.Label.Height + metrics.Space(2);
        var field = metrics.Scale(96);

        TextRenderer.DrawText(
            graphics,
            "Начало",
            fonts.Caption,
            new Point(inset, captionTop),
            palette.Muted,
            NoPad);

        TextRenderer.DrawText(
            graphics,
            "Конец",
            fonts.Caption,
            new Point(inset + field + metrics.Space(5), captionTop),
            palette.Muted,
            NoPad);

        // Длительность считается на лету: она и есть ответ на вопрос «сколько получится».
        var length = Value is { } value && value.End > value.Start
            ? WorkTimeFormat.Duration(value.End - value.Start)
            : "—";

        TextRenderer.DrawText(
            graphics,
            "Длительность " + length,
            fonts.Caption,
            new Point(inset + (field * 2) + (metrics.Space(5) * 2), _start.Top + metrics.Scale(7)),
            palette.Muted,
            NoPad);

        if (_refusal.Length > 0)
        {
            TextRenderer.DrawText(
                graphics,
                _refusal,
                fonts.Caption,
                new Rectangle(
                    inset,
                    _start.Bottom + metrics.Space(2),
                    Math.Max(0, Width - (inset * 2)),
                    fonts.Caption.Height),
                palette.Danger,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    private const TextFormatFlags NoPad = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private void Revalidate()
    {
        Invalidate();
        Edited?.Invoke(this, EventArgs.Empty);
    }
}
