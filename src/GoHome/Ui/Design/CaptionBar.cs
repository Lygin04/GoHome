using System.ComponentModel;
using System.Drawing.Drawing2D;
using GoHome.Interop;

namespace GoHome.Ui.Design;

/// <summary>Кнопка в полосе заголовка.</summary>
internal enum CaptionButton
{
    /// <summary>Курсор не над кнопкой.</summary>
    None,

    /// <summary>Свернуть.</summary>
    Minimize,

    /// <summary>Развернуть или вернуть прежний размер.</summary>
    Maximize,

    /// <summary>Закрыть.</summary>
    Close,
}

/// <summary>
/// Полоса заголовка, нарисованная приложением.
/// </summary>
/// <remarks>
/// Системную рамку окно при этом не теряет: стили остаются обычными, и всё, что Windows
/// делает сама, продолжает работать — см. <see cref="DesignForm"/>. Эта полоса отвечает
/// только за то, как заголовок выглядит и что происходит по кнопкам.
/// <para>
/// Перетаскивание, разворот двойным щелчком и системное меню по правому щелчку полоса
/// не обрабатывает вовсе. Везде, кроме кнопок, она отвечает <c>HTTRANSPARENT</c>, и вопрос
/// достаётся окну — а оно отвечает <c>HTCAPTION</c>, после чего перетаскиванием занимается
/// система. Своя реализация перетаскивания потеряла бы прилипание к краям и сочетания
/// с клавишей Windows, и восстановить их вручную нельзя.
/// </para>
/// <para>
/// Кнопки намеренно не участвуют в обходе по Tab — как и в любом системном окне. С клавиатуры
/// к ним ведёт Alt+Пробел: системное меню целиком, вместе с «Переместить» и «Размер».
/// Оно работает само, потому что стиль окна остался на месте.
/// </para>
/// </remarks>
internal sealed class CaptionBar : Control
{
    private readonly List<(CaptionButton Kind, Rectangle Bounds)> _buttons = [];

    private CaptionButton _hover = CaptionButton.None;
    private CaptionButton _pressed = CaptionButton.None;

    public CaptionBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);

        // В обход по Tab полоса не встаёт: заголовок окна — не содержимое формы.
        SetStyle(ControlStyles.Selectable, false);

        Dock = DockStyle.Top;
        TabStop = false;

        // Высота ставится сразу, не дожидаясь хендла: пристыкованная полоса нулевой высоты
        // успевает отдать своё место содержимому, и то раскладывается не туда.
        Height = Metrics.Of(this).CaptionHeight;
    }

    /// <summary>Подпись в полосе. Не <see cref="Control.Text"/>: тот у формы свой.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
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
    } = string.Empty;

    /// <summary>Окно активно. Неактивное уводит заголовок и значки в блёклый.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsActiveWindow
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
    } = true;

    /// <summary>Высоту полосу задаёт дизайн, а не раскладка.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScale();
    }

    /// <inheritdoc/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyScale();
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        var metrics = Metrics.Of(this);
        var fonts = Typography.Of(metrics);
        var graphics = e.Graphics;

        using (var back = new SolidBrush(palette.Caption))
        {
            graphics.FillRectangle(back, ClientRectangle);
        }

        using (var line = new Pen(palette.LineSoft))
        {
            graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
        }

        LayoutButtons(metrics);

        var quiet = IsActiveWindow ? palette.Muted : palette.Faint;
        var iconSize = metrics.Scale(15);
        var left = metrics.Space(3);

        DrawMark(
            graphics,
            new Rectangle(left, (Height - iconSize) / 2, iconSize, iconSize),
            IsActiveWindow ? palette.Accent : palette.Faint,
            palette.Line,
            metrics);

        left += iconSize + metrics.Scale(9);

        var titleWidth = (_buttons.Count > 0 ? _buttons[0].Bounds.Left : Width) - left - metrics.Space(2);
        if (titleWidth > 0)
        {
            TextRenderer.DrawText(
                graphics,
                Caption,
                fonts.Control,
                new Rectangle(left, 0, titleWidth, Height - 1),
                quiet,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        foreach (var (kind, bounds) in _buttons)
        {
            DrawButton(graphics, kind, bounds, palette, metrics, quiet);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ArgumentNullException.ThrowIfNull(e);
        SetHover(ButtonAt(e.Location));
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(CaptionButton.None);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button == MouseButtons.Left && ButtonAt(e.Location) is var button and not CaptionButton.None)
        {
            _pressed = button;
            Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        ArgumentNullException.ThrowIfNull(e);

        var pressed = _pressed;
        _pressed = CaptionButton.None;
        Invalidate();

        // Кнопка срабатывает, только если её и нажали, и отпустили: уведённый курсор отменяет.
        if (e.Button == MouseButtons.Left && pressed != CaptionButton.None && ButtonAt(e.Location) == pressed)
        {
            Invoke(pressed);
        }
    }

    /// <summary>
    /// Отдаёт вопрос «что под курсором» окну везде, кроме своих кнопок.
    /// </summary>
    /// <remarks>
    /// Полоса растяжения вдоль верхнего края проходит поверх кнопок, и там окно отвечает
    /// раньше: иначе за верхний край окна можно было бы потянуть везде, кроме правого
    /// верхнего угла, — ровно там, куда за ним и тянутся.
    /// </remarks>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmNcHitTest)
        {
            var metrics = Metrics.Of(this);
            var point = PointToClient(new Point(m.LParam.ToInt32()));

            var overButton = point.Y >= metrics.ResizeBorder && ButtonAt(point) != CaptionButton.None;
            m.Result = overButton ? NativeMethods.HtClient : NativeMethods.HtTransparent;
            return;
        }

        base.WndProc(ref m);
    }

    private void ApplyScale()
    {
        var metrics = Metrics.Of(this);
        Height = metrics.CaptionHeight;
        LayoutButtons(metrics);
        Invalidate();
    }

    /// <summary>Раскладывает кнопки справа налево: закрытие всегда у самого края.</summary>
    private void LayoutButtons(Metrics metrics)
    {
        _buttons.Clear();

        var form = FindForm();
        var size = metrics.CaptionButton;
        var right = Width;

        Place(CaptionButton.Close);

        // Разворот есть только там, где он разрешён. У формы дня и настроек его нет вовсе:
        // недоступная кнопка обещала бы действие, которого не будет.
        if (form?.MaximizeBox == true)
        {
            Place(CaptionButton.Maximize);
        }

        if (form?.MinimizeBox == true)
        {
            Place(CaptionButton.Minimize);
        }

        _buttons.Reverse();

        void Place(CaptionButton kind)
        {
            right -= size.Width;
            _buttons.Add((kind, new Rectangle(right, 0, size.Width, size.Height)));
        }
    }

    private CaptionButton ButtonAt(Point point)
    {
        foreach (var (kind, bounds) in _buttons)
        {
            if (bounds.Contains(point))
            {
                return kind;
            }
        }

        return CaptionButton.None;
    }

    private void SetHover(CaptionButton button)
    {
        if (_hover != button)
        {
            _hover = button;
            Invalidate();
        }
    }

    private void Invoke(CaptionButton button)
    {
        if (FindForm() is not { } form)
        {
            return;
        }

        switch (button)
        {
            case CaptionButton.Minimize:
                form.WindowState = FormWindowState.Minimized;
                break;

            case CaptionButton.Maximize:
                form.WindowState = form.WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
                break;

            case CaptionButton.Close:
                form.Close();
                break;
        }
    }

    private void DrawButton(
        Graphics graphics,
        CaptionButton kind,
        Rectangle bounds,
        Palette palette,
        Metrics metrics,
        Color quiet)
    {
        var hovered = _hover == kind;
        var held = _pressed == kind && hovered;
        var closing = kind == CaptionButton.Close;

        var ink = quiet;

        // Подложка у неактивного окна не рисуется вовсе — кроме закрытия: оно красное
        // и в неактивном окне, как в самой Windows.
        if (hovered && (IsActiveWindow || closing))
        {
            var back = closing
                ? palette.CaptionClose
                : held ? palette.CaptionPressed : palette.CaptionHover;

            using var brush = new SolidBrush(back);
            graphics.FillRectangle(brush, bounds);
            ink = closing ? palette.CaptionCloseInk : palette.Ink;
        }

        var glyph = metrics.CaptionGlyphSize;
        var box = new Rectangle(
            bounds.X + ((bounds.Width - glyph) / 2),
            bounds.Y + ((bounds.Height - glyph) / 2),
            glyph,
            glyph);

        var thickness = Math.Max(1, metrics.Scale(1));
        using var pen = new Pen(ink, thickness);

        switch (kind)
        {
            case CaptionButton.Minimize:
                // Прямая линия по целым пикселям: сглаживание тут только мылит.
                graphics.SmoothingMode = SmoothingMode.None;
                var y = box.Y + (box.Height / 2);
                graphics.DrawLine(pen, box.Left, y, box.Right, y);
                break;

            case CaptionButton.Maximize:
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.DrawRectangle(pen, box.X, box.Y, box.Width - thickness, box.Height - thickness);
                break;

            case CaptionButton.Close:
                // Диагонали без сглаживания — лесенка, поэтому здесь оно нужно.
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, box.Left, box.Top, box.Right, box.Bottom);
                graphics.DrawLine(pen, box.Right, box.Top, box.Left, box.Bottom);
                break;
        }

        graphics.SmoothingMode = SmoothingMode.None;
    }

    /// <summary>
    /// Значок приложения — то же кольцо, что и в трее, но нарисованное здесь заново.
    /// </summary>
    /// <remarks>
    /// Именно геометрией, а не картинкой: значок обязан быть чётким на любом масштабе
    /// и менять цвет вместе с темой. Заполнение у него постоянное — это опознавательный
    /// знак приложения, а не показание счётчика.
    /// </remarks>
    private static void DrawMark(Graphics graphics, Rectangle bounds, Color accent, Color rail, Metrics metrics)
    {
        var thickness = metrics.Exact(15 * 3.4 / 24);
        var inset = thickness / 2f;
        var circle = RectangleF.Inflate(bounds, -inset, -inset);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var track = new Pen(rail, thickness))
        {
            graphics.DrawEllipse(track, circle);
        }

        using (var arc = new Pen(accent, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawArc(arc, circle, -90f, 270f);
        }

        graphics.SmoothingMode = SmoothingMode.None;
    }
}
