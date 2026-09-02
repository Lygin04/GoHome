using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace GoHome.Ui.Design;

/// <summary>
/// Полоса дня: работа, отлучки, отметка текущего момента и метка цели.
/// </summary>
/// <remarks>
/// Что рисовать, решает <see cref="DayBand"/> — здесь только заливка прямоугольников.
/// Разделение не формальное: границы диапазона и разрезание дня отлучками проверяются
/// числами, а не разглядыванием картинки.
/// <para>
/// Засчитанная отлучка в дизайне — акцент с прозрачностью 0.42. Настоящего слоя
/// с прозрачностью здесь нет, но фон под полосой известен всегда, поэтому цвет смешивается
/// заранее и рисуется сплошным.
/// </para>
/// </remarks>
internal sealed class DayTimeline : Control
{
    /// <summary>Прозрачность засчитанной отлучки из дизайна.</summary>
    private const double PaidBreakAlpha = 0.42;

    private DayBand? _band;

    public DayTimeline()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);

        TabStop = false;
        Height = PreferredHeight;
    }

    /// <summary>Полоса стала уже — метки часов редеют, а сама полоса становится ниже.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Narrow
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Height = PreferredHeight;
                Invalidate();
            }
        }
    }

    /// <summary>Начало выбранной отлучки — она обводится на полосе.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTimeOffset? Selected
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

    /// <summary>Высота полосы вместе с осью часов под ней.</summary>
    public int PreferredHeight
    {
        get
        {
            var metrics = Metrics.Of(this);
            var band = Narrow ? metrics.TimelineHeightNarrow : metrics.TimelineHeight;
            return band + metrics.Scale(7) + Typography.Of(metrics).Tick.Height;
        }
    }

    /// <summary>Показывает полосу.</summary>
    public void Show(DayBand band)
    {
        _band = band;
        Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Palette.Current();
        var metrics = Metrics.Of(this);
        var graphics = e.Graphics;

        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            graphics.FillRectangle(back, ClientRectangle);
        }

        var height = Narrow ? metrics.TimelineHeightNarrow : metrics.TimelineHeight;
        var track = new Rectangle(0, 0, Math.Max(0, Width - 1), height);

        Draw.Surface(
            graphics,
            track,
            new Face(palette.Track, palette.LineSoft, palette.Ink),
            metrics.RadiusControl,
            Draw.LineWidth(metrics));

        if (_band is not { } band || band.Span <= TimeSpan.Zero)
        {
            return;
        }

        // Отрезки обрезаются по скруглению полосы: иначе крайние заливки вылезают за углы.
        var saved = graphics.Save();
        using (var clip = Draw.RoundedPath(track, metrics.RadiusControl))
        {
            graphics.SetClip(clip);

            foreach (var segment in band.Segments)
            {
                Fill(graphics, band, segment, track, palette);
            }

            graphics.Restore(saved);
        }

        if (band.Goal is { } goal)
        {
            DashedMark(graphics, X(band, goal, track), track, palette.Goal, metrics);
        }

        if (band.Now is { } now)
        {
            NowMark(graphics, X(band, now, track), track, palette.Ink, metrics);
        }

        Ticks(graphics, band, track, palette, metrics);
    }

    /// <inheritdoc/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = PreferredHeight;
        Invalidate();
    }

    private void Fill(Graphics graphics, DayBand band, BandSegment segment, Rectangle track, Palette palette)
    {
        var left = X(band, segment.Start, track);
        var right = X(band, segment.End, track);

        // Отрезок в минуту на широкой полосе — доля пикселя. Показать его надо всё равно:
        // пропущенная отлучка означает, что полоса врёт про непрерывную работу.
        var width = Math.Max(1, right - left);

        var colour = segment.Kind switch
        {
            BandKind.Work => palette.Accent,
            BandKind.PaidBreak => Palette.Blend(palette.Accent, palette.Track, PaidBreakAlpha),
            _ => palette.Unpaid,
        };

        using (var brush = new SolidBrush(colour))
        {
            graphics.FillRectangle(brush, left, track.Top, width, track.Height);
        }

        if (Selected is { } selected && segment.Kind != BandKind.Work && segment.Start == selected)
        {
            var metrics = Metrics.Of(this);
            var thickness = Math.Max(1, metrics.FocusWidth);

            using var pen = new Pen(palette.Ink, thickness);
            graphics.DrawRectangle(
                pen,
                left + (thickness / 2),
                track.Top + (thickness / 2),
                Math.Max(0, width - thickness),
                Math.Max(0, track.Height - thickness));
        }
    }

    private void Ticks(Graphics graphics, DayBand band, Rectangle track, Palette palette, Metrics metrics)
    {
        var fonts = Typography.Of(metrics);
        var top = track.Bottom + metrics.Scale(7);

        foreach (var tick in band.Ticks)
        {
            var label = tick.ToString(Narrow ? "HH" : "HH:mm", CultureInfo.InvariantCulture);
            var size = TextRenderer.MeasureText(label, fonts.Tick, Size.Empty, TextFormatFlags.NoPadding);
            var x = X(band, tick, track) - (size.Width / 2);

            // Крайние метки прижимаются к краю, а не вылезают за него.
            x = Math.Clamp(x, 0, Math.Max(0, Width - size.Width));

            TextRenderer.DrawText(
                graphics,
                label,
                fonts.Tick,
                new Point(x, top),
                palette.Faint,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    private static void DashedMark(Graphics graphics, int x, Rectangle track, Color colour, Metrics metrics)
    {
        using var pen = new Pen(colour, Math.Max(1, metrics.Scale(1))) { DashStyle = DashStyle.Dash };
        graphics.DrawLine(pen, x, track.Top, x, track.Bottom);
    }

    /// <summary>Отметка «сейчас»: линия во всю высоту с кружком сверху, как в дизайне.</summary>
    private static void NowMark(Graphics graphics, int x, Rectangle track, Color colour, Metrics metrics)
    {
        var overhang = metrics.Scale(4);
        var thickness = Math.Max(1, metrics.Scale(2));
        var dot = metrics.Scale(7);

        using (var brush = new SolidBrush(colour))
        {
            graphics.FillRectangle(
                brush,
                x - (thickness / 2),
                track.Top - overhang,
                thickness,
                track.Height + (overhang * 2));

            var previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillEllipse(brush, x - (dot / 2), track.Top - overhang + metrics.Scale(1), dot, dot);
            graphics.SmoothingMode = previous;
        }
    }

    private static int X(DayBand band, DateTimeOffset at, Rectangle track) =>
        track.Left + (int)Math.Round(band.Fraction(at) * track.Width);
}
