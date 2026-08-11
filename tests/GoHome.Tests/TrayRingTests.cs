using System.Drawing;
using GoHome.Ui;

namespace GoHome.Tests;

public class TrayRingTests
{
    [Fact]
    public void Одинаковый_прогресс_даёт_одинаковое_описание_картинки()
    {
        // Иначе кольцо перерисовывается на каждом тике и течёт хендлами GDI.
        var first = RingVisual.From(16, 0.5000, RingMood.Running, RingPalette.Dark);
        var second = RingVisual.From(16, 0.5001, RingMood.Running, RingPalette.Dark);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Различимое_изменение_прогресса_меняет_описание()
    {
        var first = RingVisual.From(16, 0.50, RingMood.Running, RingPalette.Dark);
        var second = RingVisual.From(16, 0.51, RingMood.Running, RingPalette.Dark);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)] // 125% DPI
    [InlineData(24)]
    public void Размер_иконки_соблюдается(int size)
    {
        using var bitmap = TrayRingPainter.Paint(RingVisual.From(size, 0.5, RingMood.Running, RingPalette.Dark));

        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
    }

    [Fact]
    public void Смена_оформления_и_настроения_меняет_описание()
    {
        var baseline = RingVisual.From(16, 0.5, RingMood.Running, RingPalette.Dark);

        Assert.NotEqual(baseline, RingVisual.From(16, 0.5, RingMood.Running, RingPalette.Light));
        Assert.NotEqual(baseline, RingVisual.From(16, 0.5, RingMood.Running, RingPalette.HighContrast));
        Assert.NotEqual(baseline, RingVisual.From(16, 0.5, RingMood.Paused, RingPalette.Dark));
        Assert.NotEqual(baseline, RingVisual.From(20, 0.5, RingMood.Running, RingPalette.Dark));
    }

    [Fact]
    public void Прогресс_ограничен_отрезком()
    {
        Assert.Equal(0, RingVisual.From(16, -1, RingMood.Running, RingPalette.Dark).Steps);
        Assert.Equal(RingVisual.TotalSteps, RingVisual.From(16, 42, RingMood.Done, RingPalette.Dark).Steps);
        Assert.Equal(0, RingVisual.From(16, double.NaN, RingMood.Running, RingPalette.Dark).Steps);
    }

    [Fact]
    public void Кольцо_заполняется_по_мере_прогресса()
    {
        var empty = CountColored(0d);
        var half = CountColored(0.5d);
        var full = CountColored(1d);

        Assert.Equal(0, empty);
        Assert.True(half > 0);
        Assert.True(full > half);
    }

    [Theory]
    [InlineData(RingPalette.Dark)]
    [InlineData(RingPalette.Light)]
    [InlineData(RingPalette.HighContrast)]
    public void Кольцо_рисуется_непрозрачным(RingPalette palette)
    {
        // Полупрозрачная подложка рассчитывает на просвечивающий фон панели задач,
        // а панель бывает выкрашена в акцентный цвет пользователя.
        using var bitmap = TrayRingPainter.Paint(RingVisual.From(24, 0.5, RingMood.Running, palette));

        Assert.Equal(255, TrackPixel(bitmap).A);
        Assert.Equal(255, ArcPixel(bitmap).A);
    }

    [Theory]
    [InlineData(RingPalette.Dark)]
    [InlineData(RingPalette.Light)]
    [InlineData(RingPalette.HighContrast)]
    public void Подложка_отличается_от_дуги(RingPalette palette)
    {
        foreach (var mood in Enum.GetValues<RingMood>())
        {
            using var bitmap = TrayRingPainter.Paint(RingVisual.From(24, 0.5, mood, palette));

            Assert.True(
                Distance(TrackPixel(bitmap), ArcPixel(bitmap)) > 40,
                $"{palette}/{mood}: подложка {TrackPixel(bitmap)} неотличима от дуги {ArcPixel(bitmap)}");
        }
    }

    [Theory]
    [InlineData(RingPalette.Dark)]
    [InlineData(RingPalette.Light)]
    public void Три_состояния_дуги_различимы(RingPalette palette)
    {
        var running = ArcColor(RingMood.Running, palette);
        var paused = ArcColor(RingMood.Paused, palette);
        var done = ArcColor(RingMood.Done, palette);

        Assert.True(Distance(running, paused) > 90, $"{palette}: работа {running} против паузы {paused}");
        Assert.True(Distance(running, done) > 90, $"{palette}: работа {running} против нормы {done}");
        Assert.True(Distance(paused, done) > 90, $"{palette}: пауза {paused} против нормы {done}");
    }

    [Fact]
    public void Оформление_берётся_из_системы()
    {
        var expected = SystemInformation.HighContrast
            ? RingPalette.HighContrast
            : GoHome.Interop.SystemTheme.IsDarkTaskbar()
                ? RingPalette.Dark
                : RingPalette.Light;

        Assert.Equal(expected, TrayRing.CurrentPalette);
    }

    /// <summary>Сколько пикселей закрашено цветом дуги, а не подложкой.</summary>
    private static int CountColored(double progress)
    {
        // Эталон цвета дуги берётся из заведомо закрашенной точки: сравнивать
        // с константой нельзя — цвета подбираются под оформление.
        using var reference = TrayRingPainter.Paint(RingVisual.From(32, 0.5, RingMood.Running, RingPalette.Dark));
        var arc = ArcPixel(reference);

        using var bitmap = TrayRingPainter.Paint(RingVisual.From(32, progress, RingMood.Running, RingPalette.Dark));

        var count = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A > 200 && Distance(pixel, arc) < 30)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Color ArcColor(RingMood mood, RingPalette palette)
    {
        using var bitmap = TrayRingPainter.Paint(RingVisual.From(24, 0.5, mood, palette));
        return ArcPixel(bitmap);
    }

    /// <summary>Манхэттенское расстояние по каналам — грубая мера «на глаз различимо».</summary>
    private static int Distance(Color first, Color second) =>
        Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);

    /// <summary>Верхушка кольца: при половинном заполнении дуга уже ушла вправо, здесь подложка.</summary>
    private static Color TrackPixel(Bitmap bitmap) => bitmap.GetPixel(3, bitmap.Height / 2);

    /// <summary>Правый бок кольца: при половинном заполнении он всегда закрашен дугой.</summary>
    private static Color ArcPixel(Bitmap bitmap) => bitmap.GetPixel(bitmap.Width - 4, bitmap.Height / 2);
}