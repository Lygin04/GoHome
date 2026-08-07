using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GoHome.Ui;

/// <summary>
/// Рисует кольцо прогресса. Текст в иконку не пишем: на 16 пикселях он нечитаем.
/// </summary>
public static class TrayRingPainter
{
    private static readonly Color Running = Color.FromArgb(0x2F, 0x9B, 0xFF);
    private static readonly Color Paused = Color.FromArgb(0x9A, 0xA0, 0xA6);
    private static readonly Color Done = Color.FromArgb(0x3F, 0xC4, 0x6B);

    /// <summary>Подложка кольца на тёмной панели задач.</summary>
    private static readonly Color TrackOnDark = Color.FromArgb(90, 255, 255, 255);

    /// <summary>
    /// Подложка на светлой панели: полупрозрачный белый там просто невидим.
    /// </summary>
    private static readonly Color TrackOnLight = Color.FromArgb(70, 0, 0, 0);

    /// <summary>Рисует иконку. Вызывающий владеет результатом.</summary>
    public static Bitmap Paint(RingVisual visual)
    {
        var size = Math.Clamp(visual.Size, 8, 256);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var thickness = Math.Max(2f, size / 4.5f);
        var inset = (thickness / 2f) + 0.5f;
        var box = new RectangleF(inset, inset, size - (inset * 2f), size - (inset * 2f));

        using (var track = new Pen(TrackColor(visual.DarkBackground), thickness))
        {
            graphics.DrawEllipse(track, box);
        }

        var sweep = (float)(360d * visual.Progress);
        if (sweep > 0f)
        {
            using var pen = new Pen(MoodColor(visual.Mood), thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            // Отсчёт с двенадцати часов по часовой стрелке.
            graphics.DrawArc(pen, box, -90f, Math.Min(sweep, 359.9f));
        }

        return bitmap;
    }

    private static Color MoodColor(RingMood mood) => mood switch
    {
        RingMood.Done => Done,
        RingMood.Paused => Paused,
        _ => Running,
    };

    private static Color TrackColor(bool darkBackground) => darkBackground ? TrackOnDark : TrackOnLight;
}