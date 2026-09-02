using System.Drawing.Drawing2D;
using GoHome.Core;

namespace GoHome.Ui.Design;

/// <summary>
/// Шапка меню трея: кольцо, счётчик, полоса до цели, приход и прогноз ухода.
/// </summary>
/// <remarks>
/// Отвечает на вопрос «сколько ещё» без открытия окна и платит за это высотой меню.
/// Кольцо здесь рисуется палитрой приложения, а не панели задач: оно лежит на фоне меню,
/// а не на панели. У значка в трее правило прежнее — он следует панели задач.
/// </remarks>
internal sealed class TraySummary : DrawnPanel
{
    private DaySummary? _day;

    /// <summary>Сколько места занимает шапка целиком.</summary>
    public int PreferredHeight =>
        Sizes.Space(3)
        + Fonts.CardNumber.Height
        + Sizes.Scale(3)
        + Fonts.Caption.Height
        + Sizes.Space(3)
        + Sizes.Scale(6)
        + Sizes.Space(2)
        + Fonts.Caption.Height
        + Sizes.Space(3);

    /// <summary>Показывает сводку дня.</summary>
    public void Show(DaySummary day)
    {
        _day = day;
        Invalidate();
    }

    /// <inheritdoc/>
    protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
    {
        // Фон берётся у меню, а не у родителя: хозяин контрола в полосе меню — сама полоса,
        // и её цвет к палитре приложения отношения не имеет.
        using (var back = new SolidBrush(palette.MenuBack))
        {
            graphics.FillRectangle(back, ClientRectangle);
        }

        if (_day is not { } day)
        {
            return;
        }

        var inset = metrics.Space(3);
        var ring = metrics.Scale(40);
        var top = inset;

        var mood = day.IsDayOff ? palette.Muted
            : day.GoalReached ? palette.Goal
            : day.State == WorkState.Working ? palette.Accent
            : palette.Unpaid;

        DrawRing(graphics, new Rectangle(inset, top, ring, ring), day.Progress, mood, palette.Track, metrics);

        var left = inset + ring + metrics.Space(3);

        TextRenderer.DrawText(
            graphics,
            WorkTimeFormat.Duration(day.Worked),
            fonts.CardNumber,
            new Point(left, top),
            palette.Ink,
            Tight);

        var note = day.State == WorkState.NotStarted
            ? "день ещё не начат"
            : day.IsDayOff ? "нерабочий день"
            : day.GoalReached ? "норма отработана"
            : $"осталось {WorkTimeFormat.Duration(day.Remaining)} до {WorkTimeFormat.Duration(day.Goal)}";

        TextRenderer.DrawText(
            graphics,
            note,
            fonts.Caption,
            new Rectangle(
                left,
                top + fonts.CardNumber.Height + metrics.Scale(3),
                Math.Max(0, Width - left - inset),
                fonts.Caption.Height),
            palette.Muted,
            Flat);

        // ---- полоса до цели ---------------------------------------------------------
        var barTop = top + Math.Max(ring, fonts.CardNumber.Height + metrics.Scale(3) + fonts.Caption.Height)
            + metrics.Space(3);

        var bar = new Rectangle(inset, barTop, Math.Max(0, Width - (inset * 2)), metrics.Scale(6));

        using (var path = Draw.RoundedPath(bar, bar.Height / 2))
        using (var back = new SolidBrush(palette.Track))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(back, path);
            graphics.SmoothingMode = SmoothingMode.None;
        }

        var filled = (int)Math.Round(bar.Width * day.Progress);
        if (filled > 0)
        {
            using var path = Draw.RoundedPath(bar with { Width = filled }, bar.Height / 2);
            using var brush = new SolidBrush(mood);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
            graphics.SmoothingMode = SmoothingMode.None;
        }

        // ---- приход и прогноз ---------------------------------------------------------
        var footTop = bar.Bottom + metrics.Space(2);
        var arrived = day.ArrivedAt is { } at ? "пришёл " + WorkTimeFormat.Clock(at) : "прихода не было";

        TextRenderer.DrawText(
            graphics,
            arrived,
            fonts.Caption,
            new Rectangle(inset, footTop, Math.Max(0, Width - (inset * 2)), fonts.Caption.Height),
            palette.Muted,
            Flat);

        var right = day.State switch
        {
            WorkState.Working when day.ProjectedEnd is { } end => "освобожусь " + WorkTimeFormat.Clock(end),
            WorkState.OnBreak => "пауза",
            WorkState.Finished when day.LeftAt is { } gone => "ушёл " + WorkTimeFormat.Clock(gone),
            _ => string.Empty,
        };

        if (right.Length > 0)
        {
            TextRenderer.DrawText(
                graphics,
                right,
                fonts.Caption,
                new Rectangle(inset, footTop, Math.Max(0, Width - (inset * 2)), fonts.Caption.Height),
                day.State == WorkState.Working ? palette.Accent : palette.Muted,
                Flat | TextFormatFlags.Right);
        }
    }

    /// <summary>
    /// Кольцо заполнения — той же геометрией, что и в трее, но крупнее и палитрой окон.
    /// </summary>
    private static void DrawRing(
        Graphics graphics,
        Rectangle bounds,
        double progress,
        Color mood,
        Color rail,
        Metrics metrics)
    {
        var thickness = metrics.Exact(5);
        var inset = thickness / 2f;
        var circle = RectangleF.Inflate(bounds, -inset, -inset);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var track = new Pen(rail, thickness))
        {
            graphics.DrawEllipse(track, circle);
        }

        var sweep = (float)(Math.Clamp(progress, 0d, 1d) * 360d);
        if (sweep > 0)
        {
            using var arc = new Pen(mood, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(arc, circle, -90f, sweep);
        }

        graphics.SmoothingMode = SmoothingMode.None;
    }
}
