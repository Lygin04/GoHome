using System.ComponentModel;

namespace GoHome.Ui.Design;

/// <summary>
/// Раздел настроек: заголовок, пояснение и строки «подпись — контрол».
/// </summary>
/// <remarks>
/// Не <see cref="GroupBox"/>: его рамку рисует система, и в тёмной теме она остаётся
/// светлой. Да и рамка здесь не нужна — разделы отделяет заголовок и линия над строкой,
/// как в дизайне.
/// <para>
/// Подписи строк рисуются, а не выкладываются подписями: на пять разделов их набралось бы
/// под сорок, и каждая была бы отдельным контролом со своим фоном.
/// </para>
/// </remarks>
internal sealed class SettingsSection : Panel, IPaletteAware
{
    private const int RowPadding = 12;

    private readonly List<Row> _rows = [];

    public SettingsSection()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>Заголовок раздела — прописными вразрядку.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label { get; init; } = string.Empty;

    /// <summary>Пояснение под заголовком.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Note { get; init; } = string.Empty;

    /// <summary>Сколько места хочет раздел целиком.</summary>
    public int PreferredHeight
    {
        get
        {
            var metrics = Metrics.Of(this);
            var height = HeaderHeight(metrics);

            foreach (var row in _rows)
            {
                height += RowHeight(row, metrics);
            }

            return height;
        }
    }

    /// <summary>
    /// Добавляет строку настройки.
    /// </summary>
    /// <param name="title">Название настройки.</param>
    /// <param name="note">Пояснение под названием. Пустое — строки не будет.</param>
    /// <param name="controls">Контролы справа, слева направо.</param>
    public void Add(string title, string note, params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        _rows.Add(new Row(title, note, controls));
        Controls.AddRange(controls);
        PerformLayout();
        Invalidate();
    }

    /// <inheritdoc/>
    public void RefreshPalette()
    {
        BackColor = Parent?.BackColor ?? Palette.Current().Window;
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
        var y = HeaderHeight(metrics);

        foreach (var row in _rows)
        {
            var height = RowHeight(row, metrics);
            var right = Width;

            // Справа налево: последний контрол прижат к краю, остальные вырастают влево.
            foreach (var control in row.Controls.Reverse())
            {
                control.Location = new Point(
                    right - control.Width,
                    y + ((height - control.Height) / 2));

                right = control.Left - metrics.Space(2);
            }

            y += height;
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        var metrics = Metrics.Of(this);
        var fonts = Typography.Of(metrics);

        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        var y = 0;

        if (Label.Length > 0)
        {
            fonts.DrawLabel(e.Graphics, Label, new Point(0, y), palette.Faint);
            y += fonts.Label.Height + metrics.Scale(2);
        }

        if (Note.Length > 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                Note,
                fonts.Caption,
                new Rectangle(0, y, Width, NoteHeight(metrics)),
                palette.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        y = HeaderHeight(metrics);

        foreach (var row in _rows)
        {
            var height = RowHeight(row, metrics);
            Draw.Separator(e.Graphics, 0, Width, y, palette.LineSoft, metrics);

            var left = row.Controls.Where(control => control.Visible).Select(control => control.Left).DefaultIfEmpty(Width).Min();
            var textWidth = Math.Max(metrics.Scale(80), left - metrics.Space(4));
            var top = y + metrics.Scale(RowPadding);

            TextRenderer.DrawText(
                e.Graphics,
                row.Title,
                fonts.Body,
                new Rectangle(0, top, textWidth, fonts.Body.Height),
                Enabled ? palette.Ink : palette.Faint,
                Draw.Flat);

            if (row.Note.Length > 0)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    row.Note,
                    fonts.Caption,
                    new Rectangle(0, top + fonts.Body.Height + metrics.Scale(2), textWidth, fonts.Caption.Height),
                    palette.Muted,
                    Draw.Flat);
            }

            y += height;
        }
    }

    private int HeaderHeight(Metrics metrics)
    {
        if (Label.Length == 0 && Note.Length == 0)
        {
            return 0;
        }

        var fonts = Typography.Of(metrics);
        var height = 0;

        if (Label.Length > 0)
        {
            height += fonts.Label.Height + metrics.Scale(2);
        }

        if (Note.Length > 0)
        {
            height += NoteHeight(metrics);
        }

        return height + metrics.Space(3);
    }

    /// <summary>
    /// Сколько строк занимает пояснение раздела.
    /// </summary>
    /// <remarks>
    /// Переносится по словам: обрезать пояснение многоточием — значит оставить человека
    /// без второй половины объяснения, ради которого оно и написано.
    /// </remarks>
    private int NoteHeight(Metrics metrics) =>
        Note.Length == 0
            ? 0
            : TextRenderer.MeasureText(
                Note,
                Typography.Of(metrics).Caption,
                new Size(Math.Max(metrics.Scale(120), Width), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

    private int RowHeight(Row row, Metrics metrics)
    {
        var fonts = Typography.Of(metrics);
        var text = fonts.Body.Height + (row.Note.Length > 0 ? metrics.Scale(2) + fonts.Caption.Height : 0);
        var controls = row.Controls.Select(control => control.Height).DefaultIfEmpty(0).Max();

        return Math.Max(text, controls) + (metrics.Scale(RowPadding) * 2);
    }

    /// <summary>Одна строка настройки.</summary>
    private readonly record struct Row(string Title, string Note, Control[] Controls);
}
