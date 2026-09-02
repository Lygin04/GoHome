namespace GoHome.Ui.Design;

/// <summary>
/// Полотно, которое рисует себя само и берёт фон у родителя.
/// </summary>
/// <remarks>
/// Текст в окнах рисуется, а не выкладывается подписями: десяток подписей на окно означал бы
/// десяток контролов, у каждого со своим фоном и своим выравниванием. Рисование даёт ту же
/// картинку одним проходом и той же палитрой, что и всё остальное.
/// <para>
/// Двойная буферизация обязательна: без неё полотно мерцает при изменении размера окна.
/// </para>
/// </remarks>
internal abstract class DrawnPanel : Control
{
    /// <inheritdoc cref="Draw.Flat"/>
    protected const TextFormatFlags Flat = Draw.Flat;

    /// <inheritdoc cref="Draw.Tight"/>
    protected const TextFormatFlags Tight = Draw.Tight;

    /// <inheritdoc cref="Draw.Middle"/>
    protected const TextFormatFlags Middle = Draw.Middle;

    protected DrawnPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);

        TabStop = false;
    }

    /// <summary>Размеры дизайна под текущий масштаб экрана.</summary>
    protected Metrics Sizes => Metrics.Of(this);

    /// <summary>Шрифты под текущий масштаб экрана.</summary>
    protected Typography Fonts => Typography.Of(Sizes);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        var metrics = Metrics.Of(this);
        Render(e.Graphics, palette, metrics, Typography.Of(metrics));
    }

    /// <inheritdoc/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    /// <summary>Нарисовать содержимое. Фон уже залит цветом родителя.</summary>
    protected abstract void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts);
}
