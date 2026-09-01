using GoHome.Interop;
using GoHome.Ui.Design;

namespace GoHome.Tests;

/// <summary>
/// Проверки попадания курсора в окно, у которого заголовок нарисован приложением.
/// </summary>
/// <remarks>
/// Ошибка здесь не выглядит ошибкой: окно тянется, двигается и закрывается, просто за угол
/// потянуть нельзя. На глаз это ловится случайно, а перебором точек — сразу.
/// </remarks>
public sealed class FrameHitTestTests
{
    private static readonly Size Window = new(800, 600);

    private const int Caption = 38;
    private const int Border = 5;
    private const int Corner = 8;

    /// <summary>
    /// Углы отвечают раньше сторон. Забывают именно про это, и тогда окно перестаёт
    /// тянуться по диагонали.
    /// </summary>
    [Theory]
    [InlineData(0, 0, NativeMethods.HtTopLeft)]
    [InlineData(7, 7, NativeMethods.HtTopLeft)]
    [InlineData(799, 0, NativeMethods.HtTopRight)]
    [InlineData(792, 7, NativeMethods.HtTopRight)]
    [InlineData(0, 599, NativeMethods.HtBottomLeft)]
    [InlineData(7, 592, NativeMethods.HtBottomLeft)]
    [InlineData(799, 599, NativeMethods.HtBottomRight)]
    [InlineData(792, 592, NativeMethods.HtBottomRight)]
    public void CornersAnswerBeforeEdges(int x, int y, int expected)
    {
        Assert.Equal(expected, At(x, y));
    }

    /// <summary>Все четыре стороны тянутся — не только те две, что попадаются первыми.</summary>
    [Theory]
    [InlineData(400, 0, NativeMethods.HtTop)]
    [InlineData(400, 4, NativeMethods.HtTop)]
    [InlineData(400, 599, NativeMethods.HtBottom)]
    [InlineData(400, 595, NativeMethods.HtBottom)]
    [InlineData(0, 300, NativeMethods.HtLeft)]
    [InlineData(4, 300, NativeMethods.HtLeft)]
    [InlineData(799, 300, NativeMethods.HtRight)]
    [InlineData(795, 300, NativeMethods.HtRight)]
    public void EveryEdgeResizes(int x, int y, int expected)
    {
        Assert.Equal(expected, At(x, y));
    }

    /// <summary>
    /// Квадрат угла шире полосы стороны: попасть в угол должно быть легче, чем в сторону.
    /// Точка на пять пикселей вглубь от угла — это ещё угол, а не сторона.
    /// </summary>
    [Fact]
    public void CornerZoneReachesFurtherThanTheEdgeStrip()
    {
        Assert.Equal(NativeMethods.HtTopLeft, At(Corner - 1, Corner - 1));
        Assert.Equal(NativeMethods.HtTop, At(Corner, Border - 1));
        Assert.Equal(NativeMethods.HtLeft, At(Border - 1, Corner));
    }

    /// <summary>Полоса заголовка перетаскивает окно — везде ниже полосы растяжения.</summary>
    [Theory]
    [InlineData(400, 5)]
    [InlineData(400, 20)]
    [InlineData(400, 37)]
    [InlineData(Corner, Corner)]
    public void CaptionDragsTheWindow(int x, int y)
    {
        Assert.Equal(NativeMethods.HtCaption, At(x, y));
    }

    /// <summary>Ниже заголовка начинается содержимое, и окно за него не тянется.</summary>
    [Theory]
    [InlineData(400, 38)]
    [InlineData(400, 300)]
    [InlineData(100, 500)]
    public void ContentIsNotTheFrame(int x, int y)
    {
        Assert.Equal(NativeMethods.HtClient, At(x, y));
    }

    /// <summary>
    /// У развёрнутого окна и у окна с жёсткой рамкой полос растяжения нет вовсе: там,
    /// где иначе был бы угол, остаётся обычный заголовок или содержимое.
    /// </summary>
    [Theory]
    [InlineData(0, 0, NativeMethods.HtCaption)]
    [InlineData(799, 599, NativeMethods.HtClient)]
    [InlineData(400, 0, NativeMethods.HtCaption)]
    [InlineData(0, 300, NativeMethods.HtClient)]
    public void FixedWindowHasNoResizeZones(int x, int y, int expected)
    {
        Assert.Equal(expected, At(x, y, sizable: false));
    }

    /// <summary>
    /// Зоны считаются в пикселях текущего масштаба, а не в единицах дизайна. На 150 %
    /// полоса растяжения тоже в полтора раза шире — иначе попасть в неё станет труднее.
    /// </summary>
    [Fact]
    public void ZonesGrowWithTheScale()
    {
        var large = new Metrics(144);
        var size = new Size(1200, 900);

        // Семь пикселей — это ещё сторона при 150 %, хотя при 100 % было бы уже содержимое.
        Assert.Equal(
            NativeMethods.HtTop,
            FrameHitTest.At(new Point(600, 7), size, large.CaptionHeight, large.ResizeBorder, large.ResizeCorner, true));

        Assert.Equal(
            NativeMethods.HtCaption,
            FrameHitTest.At(new Point(600, 7), size, Caption, Border, Corner, true));
    }

    /// <summary>Ни одна точка окна не остаётся без ответа.</summary>
    [Fact]
    public void EveryPointGetsAnAnswer()
    {
        for (var x = 0; x < Window.Width; x += 7)
        {
            for (var y = 0; y < Window.Height; y += 7)
            {
                Assert.NotEqual(0, At(x, y));
            }
        }
    }

    private static nint At(int x, int y, bool sizable = true) =>
        FrameHitTest.At(new Point(x, y), Window, Caption, Border, Corner, sizable);
}
