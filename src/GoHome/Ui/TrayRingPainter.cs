using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GoHome.Ui;

/// <summary>
/// Рисует кольцо прогресса. Текст в иконку не пишем: на 16 пикселях он нечитаем.
/// </summary>
/// <remarks>
/// Все цвета кольца непрозрачные. Полупрозрачная подложка рассчитывает на то, что сквозь
/// неё просвечивает фон панели задач, а панель бывает выкрашена в акцентный цвет пользователя —
/// предсказать результат смешивания нельзя. Прозрачным остаётся только фон иконки.
/// </remarks>
public static class TrayRingPainter
{
    /// <summary>Цвета одного оформления.</summary>
    /// <param name="Track">Незаполненная часть кольца. Тише дуги, но отчётливо видна на своём фоне.</param>
    private readonly record struct Palette(Color Track, Color Running, Color Paused, Color Done);

    /// <summary>Тёмное оформление. Панель задач Windows 11 — около #191A1C.</summary>
    private static readonly Palette OnDark = new(
        Track: Color.FromArgb(0x5E, 0x64, 0x6C),
        Running: Color.FromArgb(0x4F, 0xA8, 0xFF),
        Paused: Color.FromArgb(0xC3, 0xC9, 0xD1),
        Done: Color.FromArgb(0x4A, 0xD6, 0x83));

    /// <summary>Светлое оформление. Панель задач — около #F3F3F3, поэтому цвета глубже.</summary>
    private static readonly Palette OnLight = new(
        Track: Color.FromArgb(0x9A, 0xA1, 0xAC),
        Running: Color.FromArgb(0x0B, 0x5C, 0xC8),
        Paused: Color.FromArgb(0x4E, 0x57, 0x63),
        Done: Color.FromArgb(0x0E, 0x7A, 0x3C));

    /// <summary>Рисует иконку. Вызывающий владеет результатом.</summary>
    public static Bitmap Paint(RingVisual visual)
    {
        var size = Math.Clamp(visual.Size, 8, 256);
        var palette = PaletteFor(visual.Palette);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var thickness = Math.Max(2f, size / 4.5f);
        var inset = (thickness / 2f) + 0.5f;
        var box = new RectangleF(inset, inset, size - (inset * 2f), size - (inset * 2f));

        using (var track = new Pen(palette.Track, thickness))
        {
            graphics.DrawEllipse(track, box);
        }

        var sweep = (float)(360d * visual.Progress);
        if (sweep > 0f)
        {
            using var pen = new Pen(MoodColor(palette, visual.Mood), thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            // Отсчёт с двенадцати часов по часовой стрелке.
            graphics.DrawArc(pen, box, -90f, Math.Min(sweep, 359.9f));
        }

        return bitmap;
    }

    private static Palette PaletteFor(RingPalette palette) => palette switch
    {
        RingPalette.HighContrast => HighContrast(),
        RingPalette.Light => OnLight,
        _ => OnDark,
    };

    /// <summary>
    /// Высокая контрастность. Своих цветов здесь быть не может: тему выбирает пользователь,
    /// и фон панели задач заранее неизвестен. Системные цвета с ней согласованы —
    /// <c>GrayText</c> на панели читается и при этом тише остальных.
    /// </summary>
    private static Palette HighContrast() => new(
        Track: SystemColors.GrayText,
        Running: SystemColors.Highlight,
        Paused: SystemColors.ActiveCaption,
        Done: SystemColors.ControlText);

    private static Color MoodColor(Palette palette, RingMood mood) => mood switch
    {
        RingMood.Done => palette.Done,
        RingMood.Paused => palette.Paused,
        _ => palette.Running,
    };
}