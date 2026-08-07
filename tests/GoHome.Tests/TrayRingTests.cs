using System.Drawing;
using GoHome.Ui;

namespace GoHome.Tests;

public class TrayRingTests
{
    [Fact]
    public void Одинаковый_прогресс_даёт_одинаковое_описание_картинки()
    {
        // Иначе кольцо перерисовывается на каждом тике и течёт хендлами GDI.
        var first = RingVisual.From(16, 0.5000, RingMood.Running, darkBackground: true);
        var second = RingVisual.From(16, 0.5001, RingMood.Running, darkBackground: true);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Различимое_изменение_прогресса_меняет_описание()
    {
        var first = RingVisual.From(16, 0.50, RingMood.Running, darkBackground: true);
        var second = RingVisual.From(16, 0.51, RingMood.Running, darkBackground: true);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)] // 125% DPI
    [InlineData(24)]
    public void Размер_иконки_соблюдается(int size)
    {
        using var bitmap = TrayRingPainter.Paint(RingVisual.From(size, 0.5, RingMood.Running, darkBackground: true));

        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
    }

    [Fact]
    public void Смена_темы_и_настроения_меняет_описание()
    {
        var baseline = RingVisual.From(16, 0.5, RingMood.Running, darkBackground: true);

        Assert.NotEqual(baseline, RingVisual.From(16, 0.5, RingMood.Running, darkBackground: false));
        Assert.NotEqual(baseline, RingVisual.From(16, 0.5, RingMood.Paused, darkBackground: true));
        Assert.NotEqual(baseline, RingVisual.From(20, 0.5, RingMood.Running, darkBackground: true));
    }

    [Fact]
    public void Прогресс_ограничен_отрезком()
    {
        Assert.Equal(0, RingVisual.From(16, -1, RingMood.Running, true).Steps);
        Assert.Equal(RingVisual.TotalSteps, RingVisual.From(16, 42, RingMood.Done, true).Steps);
        Assert.Equal(0, RingVisual.From(16, double.NaN, RingMood.Running, true).Steps);
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

    [Fact]
    public void Подложка_видна_на_обеих_темах()
    {
        using var onDark = TrayRingPainter.Paint(RingVisual.From(24, 0d, RingMood.Paused, darkBackground: true));
        using var onLight = TrayRingPainter.Paint(RingVisual.From(24, 0d, RingMood.Paused, darkBackground: false));

        Assert.True(Opaqueness(onDark) > 0);
        Assert.True(Opaqueness(onLight) > 0);
        Assert.NotEqual(TrackPixel(onDark), TrackPixel(onLight));
    }

    /// <summary>Сколько пикселей закрашено цветом кольца, а не подложкой.</summary>
    private static int CountColored(double progress)
    {
        using var bitmap = TrayRingPainter.Paint(RingVisual.From(32, progress, RingMood.Running, darkBackground: true));

        var count = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A > 200 && pixel.B > pixel.R)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int Opaqueness(Bitmap bitmap)
    {
        var count = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).A > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Пиксель на верхушке кольца — там, где всегда лежит подложка при нулевом прогрессе.</summary>
    private static Color TrackPixel(Bitmap bitmap) => bitmap.GetPixel(bitmap.Width / 2, 3);
}