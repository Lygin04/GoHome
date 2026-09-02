using System.Drawing.Drawing2D;

namespace GoHome.Ui.Design;

/// <summary>Куда смотрит стрелка.</summary>
internal enum Chevron
{
    /// <summary>Назад.</summary>
    Left,

    /// <summary>Вперёд.</summary>
    Right,
}

/// <summary>
/// Общие приёмы рисования: скруглённые прямоугольники, рамки, контур фокуса, значки.
/// </summary>
/// <remarks>
/// Собрано в одном месте, потому что расхождение здесь заметнее всего: скруглённая на два
/// пикселя иначе кнопка бросается в глаза сильнее, чем неверный оттенок.
/// <para>
/// Сглаживание включается только там, где есть кривая или диагональ. Прямые линии по целым
/// пикселям сглаживание не улучшает, а мылит — и мылит тем заметнее, чем крупнее масштаб.
/// </para>
/// </remarks>
internal static class Draw
{
    /// <summary>
    /// Обычные правила разметки текста: без подчёркиваний по амперсанду и с многоточием.
    /// </summary>
    /// <remarks>
    /// Подчёркивание по амперсанду в нарисованном тексте не нужно нигде: ускорителей у него
    /// нет, а адрес файла с амперсандом превратился бы в подчёркнутую букву.
    /// </remarks>
    public const TextFormatFlags Flat = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    /// <summary>То же, но без отступов, которые TextRenderer добавляет по краям.</summary>
    public const TextFormatFlags Tight = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    /// <summary>То же, что <see cref="Flat"/>, по центру строки.</summary>
    public const TextFormatFlags Middle = TextFormatFlags.VerticalCenter | Flat;

    /// <summary>Скруглённый прямоугольник как контур.</summary>
    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        // Скругление не бывает больше половины меньшей стороны: иначе дуги перехлёстываются
        // и контур выворачивается наизнанку.
        var corner = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));

        if (corner == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = corner * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>Заливает и обводит прямоугольник по описанию состояния.</summary>
    /// <remarks>
    /// Прозрачный цвет означает «не рисовать»: у опасной кнопки в покое нет ни подложки,
    /// ни рамки, и заливать её прозрачной кистью — значит стирать то, что под ней.
    /// </remarks>
    public static void Surface(Graphics graphics, Rectangle bounds, Face face, int radius, int thickness)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = radius > 0 ? SmoothingMode.AntiAlias : SmoothingMode.None;

        if (face.HasBack)
        {
            using var path = RoundedPath(bounds, radius);
            using var brush = new SolidBrush(face.Back);
            graphics.FillPath(brush, path);
        }

        if (face.HasLine)
        {
            // Рамка рисуется по середине линии, поэтому прямоугольник поджимается на половину
            // толщины — иначе внешняя половина уходит за край и обрезается.
            var inset = Inset(bounds, thickness);
            using var path = RoundedPath(inset, Math.Max(0, radius - (thickness / 2)));
            using var pen = new Pen(face.Line, thickness);
            graphics.DrawPath(pen, path);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>
    /// Контур фокуса с клавиатуры.
    /// </summary>
    /// <remarks>
    /// В дизайне это мягкий ореол, но радиус его размытия нулевой — то есть сплошной контур
    /// той же толщины и того же цвета и есть он сам. Размывать нечего.
    /// <para>
    /// Наружу контур уходит там, где вокруг есть место, и внутрь — там, где элемент прижат
    /// к соседям: у строки списка наружный контур перекрыла бы соседняя строка.
    /// </para>
    /// </remarks>
    public static void Focus(Graphics graphics, Rectangle bounds, Color color, Metrics metrics, bool inside = false)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var thickness = Math.Max(1, metrics.FocusWidth);
        var offset = inside ? -metrics.FocusInset : metrics.FocusInset;
        var ring = Rectangle.Inflate(bounds, offset, offset);

        if (ring.Width <= 0 || ring.Height <= 0)
        {
            return;
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var path = RoundedPath(Inset(ring, thickness), metrics.RadiusControl))
        using (var pen = new Pen(color, thickness))
        {
            graphics.DrawPath(pen, path);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>Квадратик типа в строке списка и в легенде.</summary>
    public static void Marker(Graphics graphics, Rectangle bounds, Color color, Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var path = RoundedPath(bounds, metrics.Scale(2)))
        using (var brush = new SolidBrush(color))
        {
            graphics.FillPath(brush, path);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>Стрелка шага: назад к прошлому дню, вперёд к следующему.</summary>
    public static void Arrow(Graphics graphics, Rectangle bounds, Color color, Chevron direction, Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var thickness = Math.Max(1f, metrics.Exact(1.4));
        var middle = bounds.Top + (bounds.Height / 2f);
        var near = direction == Chevron.Left ? bounds.Right : bounds.Left;
        var far = direction == Chevron.Left ? bounds.Left : bounds.Right;

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLines(
                pen,
                [
                    new PointF(near, bounds.Top),
                    new PointF(far, middle),
                    new PointF(near, bounds.Bottom),
                ]);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>Горизонтальный разделитель — без сглаживания, иначе размажется в серое.</summary>
    public static void Separator(Graphics graphics, int left, int right, int y, Color color, Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.None;

        using (var pen = new Pen(color, Math.Max(1, metrics.Scale(1))))
        {
            graphics.DrawLine(pen, left, y, right, y);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>Толщина рамки в пикселях текущего масштаба. Тоньше пикселя рамок не бывает.</summary>
    public static int LineWidth(Metrics metrics) => Math.Max(1, metrics.Scale(1));

    /// <summary>
    /// Поджимает прямоугольник под середину пера, чтобы рамка легла внутрь, а не наполовину
    /// за краем. Перо рисует по середине линии, и без поджатия внешняя половина обрезается.
    /// </summary>
    private static Rectangle Inset(Rectangle bounds, int thickness)
    {
        var half = thickness / 2;

        return Rectangle.FromLTRB(
            bounds.Left + half,
            bounds.Top + half,
            bounds.Right - 1 - half,
            bounds.Bottom - 1 - half);
    }
}
