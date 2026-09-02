using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace GoHome.Ui.Design;

/// <summary>
/// Переключатель из дизайна: 38 × 22, кружок 18.
/// </summary>
/// <remarks>
/// Не <see cref="CheckBox"/>: галочка Windows и переключатель — разные обещания. Галочка
/// говорит «отмечено», переключатель — «работает прямо сейчас», и в настройках дизайна
/// везде второе.
/// <para>
/// Перекладывание кружка не анимируется. Переходов дизайн не просит нигде, а таймер ради
/// одного движения — это перерисовка каждые несколько миллисекунд на каждый переключатель.
/// </para>
/// </remarks>
internal sealed class DesignSwitch : DesignControl
{
    public DesignSwitch()
    {
        Size = Sizes.Switch;
    }

    /// <summary>Значение изменил человек.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Включено.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
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

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Colors;
        var metrics = Sizes;
        var graphics = e.Graphics;

        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            graphics.FillRectangle(back, ClientRectangle);
        }

        var track = !Enabled
            ? palette.LineSoft
            : Checked ? palette.Accent : palette.Line;

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Скругление в половину высоты — это и есть «999px» из дизайна.
        using (var path = Draw.RoundedPath(ClientRectangle, Height / 2))
        using (var brush = new SolidBrush(track))
        {
            graphics.FillPath(brush, path);
        }

        var knob = metrics.SwitchKnob;
        var inset = metrics.SwitchInset;
        var left = Checked ? Width - knob - inset : inset;

        using (var brush = new SolidBrush(Enabled ? palette.SwitchKnob : palette.SwitchKnobDisabled))
        {
            graphics.FillEllipse(brush, left, inset, knob, knob);
        }

        graphics.SmoothingMode = previous;

        if (ShowsFocus)
        {
            // Наружу: переключатель стоит в конце строки настройки, места вокруг хватает.
            Draw.Focus(graphics, ClientRectangle, palette.Accent, metrics);
        }
    }

    /// <inheritdoc/>
    protected override void Activate()
    {
        Checked = !Checked;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        base.Activate();
    }

    /// <inheritdoc/>
    protected override void Rescale() => Size = Sizes.Switch;
}
