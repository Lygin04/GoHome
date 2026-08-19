using System.Drawing.Drawing2D;
using System.Globalization;
using GoHome.Core;

namespace GoHome.Ui;

/// <summary>
/// Столбчатая диаграмма по дням: столбец на день, линия нормы поперёк.
/// </summary>
/// <remarks>
/// Рисуется вручную теми же средствами, что и кольцо в трее. Готовой диаграммы для
/// WinForms в современном .NET не осталось, а тянуть ради семи столбиков стороннюю
/// библиотеку — это зависимость, которой у проекта нет и быть не должно.
/// <para>
/// Размеры считаются от масштаба экрана: на 150% всё то же самое, только крупнее.
/// Двойная буферизация обязательна — без неё диаграмма мигает при каждом изменении
/// размера окна.
/// </para>
/// </remarks>
internal sealed class DayBarChart : Control
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly ToolTip _tip = new();

    private PeriodStats? _stats;
    private ChartPalette _palette = ChartPalette.Current();
    private int _hover = -1;

    public DayBarChart()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
            true);

        DoubleBuffered = true;
    }

    /// <summary>Показывает период. Пустой период — это тоже период, и рисуется он молча.</summary>
    public void Display(PeriodStats stats, ChartPalette palette)
    {
        _stats = stats;
        _palette = palette;
        _hover = -1;
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

        if (_stats is not { } stats || stats.Days.Count == 0)
        {
            return;
        }

        var plot = Plot();
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        var scale = Scale(stats);
        DrawGrid(graphics, plot, scale);
        DrawBars(graphics, plot, stats, scale);
        DrawGoals(graphics, plot, stats, scale);

        if (stats.IsEmpty)
        {
            DrawCentered(graphics, plot, "В этом периоде ещё ничего не отработано");
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);

        var index = IndexAt(e.X);
        if (index == _hover)
        {
            return;
        }

        _hover = index;
        _tip.SetToolTip(this, index >= 0 && _stats is { } stats ? Describe(stats.Days[index]) : string.Empty);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Что подписать над столбцом при наведении.</summary>
    internal static string Describe(DaySummary day)
    {
        ArgumentNullException.ThrowIfNull(day);

        var title = day.Date.ToString("d MMMM, dddd", Russian);

        if (day.State == WorkState.NotStarted)
        {
            return day.IsDayOff ? $"{title}: нерабочий день" : $"{title}: данных нет";
        }

        return day.IsDayOff
            ? $"{title}: {WorkTimeFormat.Duration(day.Worked)} в нерабочий день"
            : $"{title}: {WorkTimeFormat.Duration(day.Worked)} из {WorkTimeFormat.Duration(day.Goal)}";
    }

    /// <summary>Шкала: до скольких часов рисовать и через сколько часов ставить линии.</summary>
    private static (int Hours, int Step) Scale(PeriodStats stats)
    {
        // Ноль тоже надо чем-то нарисовать: час высотой во всё полотно.
        var peak = Math.Max(1d, stats.Peak.TotalHours);
        var step = peak <= 12d ? 1 : peak <= 24d ? 2 : 4;
        var hours = (int)(Math.Ceiling(peak / step) * step);

        return (Math.Max(hours, step), step);
    }

    private Rectangle Plot() => Rectangle.FromLTRB(
        LogicalToDeviceUnits(44),
        LogicalToDeviceUnits(10),
        Width - LogicalToDeviceUnits(12),
        Height - LogicalToDeviceUnits(22));

    private void DrawGrid(Graphics graphics, Rectangle plot, (int Hours, int Step) scale)
    {
        using var pen = new Pen(_palette.Grid);

        for (var hour = 0; hour <= scale.Hours; hour += scale.Step)
        {
            var y = YOf(plot, hour, scale.Hours);
            graphics.DrawLine(pen, plot.Left, y, plot.Right, y);

            TextRenderer.DrawText(
                graphics,
                hour.ToString(CultureInfo.InvariantCulture) + " ч",
                Font,
                new Rectangle(0, y - LogicalToDeviceUnits(9), plot.Left - LogicalToDeviceUnits(6), LogicalToDeviceUnits(18)),
                _palette.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawBars(Graphics graphics, Rectangle plot, PeriodStats stats, (int Hours, int Step) scale)
    {
        var slot = plot.Width / (double)stats.Days.Count;
        var width = Math.Max(2, (int)(slot * 0.62));
        var radius = LogicalToDeviceUnits(3);

        for (var index = 0; index < stats.Days.Count; index++)
        {
            var day = stats.Days[index];
            var left = plot.Left + (int)((slot * index) + ((slot - width) / 2d));

            // Нерабочий день помечен всегда, даже если в него не работали: у него нет нормы,
            // и линия нормы к нему не относится — это должно быть видно и на пустом столбце.
            if (day.IsDayOff)
            {
                using var back = new SolidBrush(_palette.Empty);
                graphics.FillRectangle(back, left, plot.Top, width, plot.Height);
            }

            if (day.Worked > TimeSpan.Zero)
            {
                var height = (int)Math.Round(plot.Height * day.Worked.TotalHours / scale.Hours);
                if (height > 0)
                {
                    using var brush = new SolidBrush(_palette.BarColor(day.IsDayOff, day.GoalReached));
                    using var path = Rounded(new Rectangle(left, plot.Bottom - height, width, height), radius);
                    graphics.FillPath(brush, path);
                }
            }

            DrawDayLabel(graphics, day, left, width, slot, plot);
        }
    }

    /// <summary>
    /// Линия нормы. Одинаковая у всех дней — сплошная поперёк; разная — штрихом над каждым днём.
    /// </summary>
    /// <remarks>
    /// Одна линия при разных целях врала бы: сокращённый предпраздничный день оказался бы
    /// недоработанным, хотя закрыт полностью.
    /// </remarks>
    private void DrawGoals(Graphics graphics, Rectangle plot, PeriodStats stats, (int Hours, int Step) scale)
    {
        var goals = stats.Days
            .Where(day => !day.IsDayOff && day.Goal > TimeSpan.Zero)
            .Select(day => day.Goal)
            .ToList();

        if (goals.Count == 0)
        {
            return;
        }

        using var pen = new Pen(_palette.Goal) { DashStyle = DashStyle.Dash };

        if (goals.Distinct().Count() == 1)
        {
            var y = YOf(plot, goals[0].TotalHours, scale.Hours);
            graphics.DrawLine(pen, plot.Left, y, plot.Right, y);
            return;
        }

        var slot = plot.Width / (double)stats.Days.Count;
        var width = Math.Max(2, (int)(slot * 0.62));

        for (var index = 0; index < stats.Days.Count; index++)
        {
            var day = stats.Days[index];
            if (day.IsDayOff || day.Goal <= TimeSpan.Zero)
            {
                continue;
            }

            var left = plot.Left + (int)((slot * index) + ((slot - width) / 2d));
            var y = YOf(plot, day.Goal.TotalHours, scale.Hours);
            graphics.DrawLine(pen, left, y, left + width, y);
        }
    }

    /// <summary>Подпись дня. В месяце подписи не влезают все — тогда остаются опорные числа.</summary>
    private void DrawDayLabel(Graphics graphics, DaySummary day, int left, int width, double slot, Rectangle plot)
    {
        var crowded = slot < LogicalToDeviceUnits(26);
        if (crowded && day.Date.Day != 1 && day.Date.Day % 5 != 0)
        {
            return;
        }

        var text = crowded
            ? day.Date.Day.ToString(CultureInfo.InvariantCulture)
            : day.Date.ToString("ddd d", Russian);

        TextRenderer.DrawText(
            graphics,
            text,
            Font,
            new Rectangle(left - LogicalToDeviceUnits(10), plot.Bottom + LogicalToDeviceUnits(3), width + LogicalToDeviceUnits(20), LogicalToDeviceUnits(16)),
            day.IsDayOff ? _palette.Muted : _palette.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawCentered(Graphics graphics, Rectangle plot, string text) =>
        TextRenderer.DrawText(
            graphics,
            text,
            Font,
            plot,
            _palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

    private static int YOf(Rectangle plot, double hours, int total) =>
        plot.Bottom - (int)Math.Round(plot.Height * hours / total);

    private static GraphicsPath Rounded(Rectangle box, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));

        if (diameter <= 0)
        {
            path.AddRectangle(box);
            return path;
        }

        // Скругление только сверху: снизу столбец стоит на оси и упирается в неё.
        path.AddArc(box.Left, box.Top, diameter, diameter, 180f, 90f);
        path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270f, 90f);
        path.AddLine(box.Right, box.Bottom, box.Left, box.Bottom);
        path.CloseFigure();
        return path;
    }

    private int IndexAt(int x)
    {
        if (_stats is not { } stats || stats.Days.Count == 0)
        {
            return -1;
        }

        var plot = Plot();
        if (plot.Width <= 0 || x < plot.Left || x >= plot.Right)
        {
            return -1;
        }

        var index = (int)((x - plot.Left) / (plot.Width / (double)stats.Days.Count));
        return Math.Clamp(index, 0, stats.Days.Count - 1);
    }
}
