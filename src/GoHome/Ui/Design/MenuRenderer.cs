namespace GoHome.Ui.Design;

/// <summary>
/// Отрисовщик меню трея.
/// </summary>
/// <remarks>
/// Единственное место, где штатная подмена отрисовщика уместна и достаточна: окно меню
/// принадлежит системе, своим его не сделать, а всё, что внутри, <see cref="ToolStrip"/>
/// отдаёт рисовать сюда. Тень и скругление самого окна меню при этом остаются системными —
/// нарисовать их нельзя, и не нужно.
/// </remarks>
internal sealed class MenuRenderer : ToolStripRenderer
{
    /// <inheritdoc/>
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        using var back = new SolidBrush(Palette.Current().MenuBack);
        e.Graphics.FillRectangle(back, e.AffectedBounds);
    }

    /// <inheritdoc/>
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        var metrics = Sizes(e.ToolStrip);

        using var pen = new Pen(palette.MenuLine, Draw.LineWidth(metrics));
        e.Graphics.DrawRectangle(
            pen,
            0,
            0,
            e.ToolStrip.Width - Draw.LineWidth(metrics),
            e.ToolStrip.Height - Draw.LineWidth(metrics));
    }

    /// <summary>
    /// Полоса под значками не рисуется вовсе.
    /// </summary>
    /// <remarks>
    /// Значков в меню нет, а системная полоса под них — это вертикальная линия другого
    /// цвета вдоль всего меню, которая ни на что не указывает.
    /// </remarks>
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        using var back = new SolidBrush(Palette.Current().MenuBack);
        e.Graphics.FillRectangle(back, e.AffectedBounds);
    }

    /// <inheritdoc/>
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        var metrics = Sizes(e.ToolStrip);
        var bounds = new Rectangle(Point.Empty, e.Item.Size);

        using (var back = new SolidBrush(palette.MenuBack))
        {
            e.Graphics.FillRectangle(back, bounds);
        }

        if (e.Item is { Selected: true, Enabled: true })
        {
            var inset = Rectangle.Inflate(bounds, -metrics.Scale(4), -metrics.Scale(1));
            Draw.Surface(
                e.Graphics,
                inset,
                new Face(palette.MenuHover, Color.Transparent, palette.Ink),
                metrics.RadiusControl,
                Draw.LineWidth(metrics));
        }
    }

    /// <inheritdoc/>
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        var metrics = Sizes(e.ToolStrip);
        var y = e.Item.Height / 2;

        using (var back = new SolidBrush(palette.MenuBack))
        {
            e.Graphics.FillRectangle(back, new Rectangle(Point.Empty, e.Item.Size));
        }

        Draw.Separator(e.Graphics, metrics.Scale(10), e.Item.Width - metrics.Scale(10), y, palette.MenuLine, metrics);
    }

    /// <inheritdoc/>
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();

        // Выход подписан опасным цветом: это единственный пункт, после которого учёт
        // останавливается совсем.
        e.TextColor = !e.Item.Enabled
            ? palette.Faint
            : e.Item.Tag as string == DangerTag ? palette.MenuDangerInk : palette.Ink;

        e.TextFormat |= TextFormatFlags.NoPrefix;
        base.OnRenderItemText(e);
    }

    /// <inheritdoc/>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Галочка рисуется своей: системная приходит из темы Windows и в тёмном меню
        // остаётся тёмной.
        var palette = Palette.Current();
        var metrics = Sizes(e.ToolStrip);
        var box = e.ImageRectangle;
        var size = metrics.Scale(9);

        var left = box.Left + ((box.Width - size) / 2);
        var top = box.Top + ((box.Height - size) / 2);

        var previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var pen = new Pen(palette.Accent, Math.Max(1f, metrics.Exact(1.6))))
        {
            e.Graphics.DrawLines(
                pen,
                [
                    new PointF(left, top + (size * 0.55f)),
                    new PointF(left + (size * 0.38f), top + size),
                    new PointF(left + size, top),
                ]);
        }

        e.Graphics.SmoothingMode = previous;
    }

    /// <summary>Пометка на пункте, который подписывается опасным цветом.</summary>
    public const string DangerTag = "danger";

    /// <summary>
    /// Метрики полосы меню.
    /// </summary>
    /// <remarks>
    /// У части событий отрисовки полоса может оказаться пустой — тогда берём масштаб
    /// основного экрана: рисовать всё равно надо, а без метрик рисовать нечем.
    /// </remarks>
    private static Metrics Sizes(ToolStrip? strip) =>
        strip is null ? new Metrics(96) : Metrics.Of(strip);
}
