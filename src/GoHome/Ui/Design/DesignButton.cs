using System.ComponentModel;

namespace GoHome.Ui.Design;

/// <summary>Вид кнопки. Различаются они не оттенком, а тем, что меняется между состояниями.</summary>
internal enum ButtonKind
{
    /// <summary>Обычная: между состояниями меняется рамка.</summary>
    Neutral,

    /// <summary>Основная: между состояниями меняется заливка целиком.</summary>
    Primary,

    /// <summary>Опасная: в покое ни подложки, ни рамки — только красный текст.</summary>
    Danger,
}

/// <summary>
/// Кнопка из дизайна: высота 30, скругление 6, три вида по четыре состояния.
/// </summary>
/// <remarks>
/// Не <see cref="Button"/> с перекрашенными свойствами: системная кнопка не принимает
/// эту палитру — в тёмной теме у неё остаётся системная рамка, а состояние «нажато»
/// рисуется системным градиентом.
/// </remarks>
internal sealed class DesignButton : DesignControl
{
    private const int HorizontalPadding = 13;

    public DesignButton()
    {
        Height = Sizes.ControlHeight;
    }

    /// <summary>Вид кнопки.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind
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
    } = ButtonKind.Neutral;

    /// <inheritdoc/>
    public override Size GetPreferredSize(Size proposed)
    {
        var text = TextRenderer.MeasureText(Text, Fonts.Control, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(text.Width + (Sizes.Scale(HorizontalPadding) * 2), Sizes.ControlHeight);
    }

    /// <summary>Подгоняет ширину под надпись — с отступами из дизайна.</summary>
    public void FitToText() => Size = GetPreferredSize(Size.Empty);

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Colors;
        var metrics = Sizes;
        var mood = CurrentMood;

        var style = Kind switch
        {
            ButtonKind.Primary => palette.PrimaryButton,
            ButtonKind.Danger => palette.DangerButton,
            _ => palette.NeutralButton,
        };

        var face = style[mood];

        // Фон родителя виден там, где у кнопки нет своей подложки, — у опасной в покое.
        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        Draw.Surface(e.Graphics, ClientRectangle, face, metrics.RadiusControl, Draw.LineWidth(metrics));

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Fonts.Control,
            ClientRectangle,
            face.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (ShowsFocus)
        {
            // Внутрь: кнопки в дизайне стоят вплотную, и наружный контур залез бы на соседа.
            Draw.Focus(e.Graphics, ClientRectangle, palette.Accent, metrics, inside: true);
        }
    }

    /// <inheritdoc/>
    protected override void Rescale() => Height = Sizes.ControlHeight;
}

/// <summary>
/// Квадратная кнопка со стрелкой: шаг на день назад или вперёд, шаг периода в статистике.
/// </summary>
/// <remarks>
/// Отдельный контрол, а не <see cref="DesignButton"/> с текстом «‹»: стрелка рисуется
/// геометрией, чтобы оставаться чёткой на любом масштабе. Знак из шрифта на 26 точках
/// в разных гарнитурах выглядит по-разному и висит не по центру.
/// </remarks>
internal sealed class StepButton : DesignControl
{
    public StepButton()
    {
        Size = new Size(Sizes.StepperSize, Sizes.StepperSize);
    }

    /// <summary>Куда ведёт шаг.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Chevron Direction
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
    } = Chevron.Left;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Colors;
        var metrics = Sizes;
        var face = palette.NeutralButton[CurrentMood];

        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        // Подложка у шага — цвет карточки, а не поля: в дизайне он тише обычной кнопки.
        var quiet = Enabled
            ? face with { Back = CurrentMood == Mood.Rest ? palette.Card : face.Back }
            : face;

        Draw.Surface(e.Graphics, ClientRectangle, quiet, metrics.RadiusControl, Draw.LineWidth(metrics));

        // Стрелка вдвое уже своей высоты — как в дизайне. В квадрате она разъезжается
        // и перестаёт читаться шевроном.
        var glyph = metrics.GlyphSize;
        var box = new Rectangle(
            (Width - (glyph / 2)) / 2,
            (Height - glyph) / 2,
            glyph / 2,
            glyph);

        Draw.Arrow(
            e.Graphics,
            box,
            Enabled ? (CurrentMood == Mood.Rest ? palette.Muted : palette.Ink) : palette.GlyphDisabled,
            Direction,
            metrics);

        if (ShowsFocus)
        {
            Draw.Focus(e.Graphics, ClientRectangle, palette.Accent, metrics, inside: true);
        }
    }

    /// <inheritdoc/>
    protected override void Rescale() => Size = new Size(Sizes.StepperSize, Sizes.StepperSize);
}
