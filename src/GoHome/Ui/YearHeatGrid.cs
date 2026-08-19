using System.Drawing.Drawing2D;
using System.Globalization;
using GoHome.Core;

namespace GoHome.Ui;

/// <summary>
/// Год календарной сеткой: клетка на день, насыщенность по доле от нормы.
/// </summary>
/// <remarks>
/// Двести пятьдесят столбцов нечитаемы ни на каком экране, а сетка на том же месте
/// читается сразу: недели идут столбцами, дни недели — строками, и провалы в отпуске
/// или в декабре видны одним взглядом.
/// </remarks>
internal sealed class YearHeatGrid : Control
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Подписываются не все дни недели: иначе строки не читаются.</summary>
    private static readonly int[] LabelledRows = [0, 2, 4];

    private readonly ToolTip _tip = new();

    private PeriodStats? _stats;
    private ChartPalette _palette = ChartPalette.Current();
    private DateOnly? _hover;

    public YearHeatGrid()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
            true);

        DoubleBuffered = true;
    }

    /// <summary>Показывает год.</summary>
    public void Display(PeriodStats stats, ChartPalette palette)
    {
        _stats = stats;
        _palette = palette;
        _hover = null;
        BackColor = palette.Surface;
        _tip.SetToolTip(this, string.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(_palette.Surface);

        if (_stats is not { } stats || stats.Days.Count == 0 || Measure(stats) is not { } layout)
        {
            return;
        }

        DrawWeekdays(graphics, layout);
        DrawMonths(graphics, stats, layout);

        foreach (var day in stats.Days)
        {
            var box = BoxOf(day.Date, layout);
            using var brush = new SolidBrush(ColorOf(day));
            using var path = Rounded(box, LogicalToDeviceUnits(2));
            graphics.FillPath(brush, path);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);

        var date = DateAt(e.Location);
        if (date == _hover)
        {
            return;
        }

        _hover = date;

        var day = date is { } found && _stats is { } stats
            ? stats.Days.FirstOrDefault(candidate => candidate.Date == found)
            : null;

        _tip.SetToolTip(this, day is null ? string.Empty : DayBarChart.Describe(day));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Раскладка сетки. <c>null</c> — окно так мало, что рисовать нечего.</summary>
    private Layouted? Measure(PeriodStats stats)
    {
        var left = LogicalToDeviceUnits(26);
        var top = LogicalToDeviceUnits(18);
        var gap = Math.Max(1, LogicalToDeviceUnits(2));

        var first = HistoryCalculator.WeekStart(stats.Range.Start);
        var last = HistoryCalculator.WeekStart(stats.Range.End);
        var columns = ((last.DayNumber - first.DayNumber) / 7) + 1;

        var cell = (int)Math.Min(
            (Width - left - LogicalToDeviceUnits(8)) / (double)columns,
            (Height - top - LogicalToDeviceUnits(8)) / 7d);

        if (cell < 3)
        {
            return null;
        }

        // Клетка квадратная, поэтому в широком окне сетка занимает не всю высоту.
        // Оставлять весь запас снизу некрасиво: сетка встаёт по центру.
        var free = Math.Max(0, Height - top - (cell * 7));
        return new Layouted(left, top + (free / 2), cell, gap, first);
    }

    private void DrawWeekdays(Graphics graphics, Layouted layout)
    {
        foreach (var row in LabelledRows)
        {
            var name = Russian.DateTimeFormat.AbbreviatedDayNames[(row + 1) % 7];

            TextRenderer.DrawText(
                graphics,
                name,
                Font,
                new Rectangle(0, layout.Top + (row * layout.Cell), layout.Left - layout.Gap, layout.Cell),
                _palette.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawMonths(Graphics graphics, PeriodStats stats, Layouted layout)
    {
        var month = 0;

        foreach (var day in stats.Days)
        {
            if (day.Date.Month == month)
            {
                continue;
            }

            month = day.Date.Month;
            var box = BoxOf(day.Date, layout);

            TextRenderer.DrawText(
                graphics,
                Russian.DateTimeFormat.AbbreviatedMonthNames[month - 1],
                Font,
                new Rectangle(box.Left, 0, layout.Cell * 4, layout.Top),
                _palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// Цвет клетки. Насыщенность — доля от нормы, но не с нуля: день с получасом работы
    /// должен отличаться от дня, в который не приходили вовсе.
    /// </summary>
    private Color ColorOf(DaySummary day)
    {
        if (day.Worked <= TimeSpan.Zero)
        {
            return day.IsDayOff ? _palette.DayOff : _palette.Empty;
        }

        var share = day.Goal > TimeSpan.Zero
            ? Math.Clamp(day.Worked.TotalSeconds / day.Goal.TotalSeconds, 0d, 1d)
            : 1d;

        return Blend(_palette.Empty, _palette.BarColor(day.IsDayOff, day.GoalReached), 0.25 + (0.75 * share));
    }

    private Rectangle BoxOf(DateOnly date, Layouted layout)
    {
        var column = (HistoryCalculator.WeekStart(date).DayNumber - layout.First.DayNumber) / 7;
        var row = ((int)date.DayOfWeek + 6) % 7;

        return new Rectangle(
            layout.Left + (column * layout.Cell),
            layout.Top + (row * layout.Cell),
            layout.Cell - layout.Gap,
            layout.Cell - layout.Gap);
    }

    private DateOnly? DateAt(Point point)
    {
        if (_stats is not { } stats || Measure(stats) is not { } layout)
        {
            return null;
        }

        var column = (point.X - layout.Left) / layout.Cell;
        var row = (point.Y - layout.Top) / layout.Cell;

        if (point.X < layout.Left || point.Y < layout.Top || row is < 0 or > 6)
        {
            return null;
        }

        var date = layout.First.AddDays((column * 7) + row);
        return stats.Range.Contains(date) ? date : null;
    }

    private static Color Blend(Color from, Color to, double share)
    {
        var clamped = Math.Clamp(share, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.R + ((to.R - from.R) * clamped)),
            (int)Math.Round(from.G + ((to.G - from.G) * clamped)),
            (int)Math.Round(from.B + ((to.B - from.B) * clamped)));
    }

    private static GraphicsPath Rounded(Rectangle box, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));

        if (diameter <= 0 || box.Width <= 0 || box.Height <= 0)
        {
            path.AddRectangle(box);
            return path;
        }

        path.AddArc(box.Left, box.Top, diameter, diameter, 180f, 90f);
        path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270f, 90f);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    /// <summary>Посчитанная раскладка сетки.</summary>
    private readonly record struct Layouted(int Left, int Top, int Cell, int Gap, DateOnly First);
}
