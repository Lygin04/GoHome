using GoHome.Ui;

namespace GoHome.Tests;

/// <summary>
/// Иконка приложения лежит в сборке ресурсом. Опечатка в имени ресурса или потерянный
/// <c>EmbeddedResource</c> проявились бы только на живом запуске — здесь они видны сразу.
/// </summary>
public class AppIconTests
{
    [Fact]
    public void Иконка_окна_загружается()
    {
        Assert.NotNull(AppIcon.ForWindows);
    }

    [Fact]
    public void Иконка_трея_загружается()
    {
        Assert.NotNull(AppIcon.ForTray);
    }

    [Fact]
    public void Иконка_трея_не_мельче_нужного_размера()
    {
        // Мелкие кадры нарисованы вручную: система уменьшит крупный кадр чище,
        // чем растянет мелкий. Растягивания быть не должно.
        var icon = AppIcon.ForTray;

        Assert.NotNull(icon);
        Assert.True(
            icon.Width >= TrayRing.IconSize,
            $"трею нужно {TrayRing.IconSize}, а кадр {icon.Width}: система будет его растягивать");
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 24)] // 125% DPI: берётся кадр крупнее, а не растягивается мелкий
    [InlineData(24, 24)]
    [InlineData(32, 32)]
    public void Кадр_подбирается_из_нарисованных(int wanted, int expected)
    {
        var frame = SelectFrame(wanted);

        Assert.Equal(expected, frame);
    }

    /// <summary>Повторяет выбор кадра по каталогу .ico — тем же правилом, что и в <see cref="AppIcon"/>.</summary>
    private static int SelectFrame(int wanted)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("GoHome.Resources.gohome.ico");
        Assert.NotNull(stream);

        var header = new byte[6];
        stream.ReadExactly(header);
        var count = BitConverter.ToUInt16(header, 4);

        var best = 0;
        var entry = new byte[16];
        for (var i = 0; i < count; i++)
        {
            stream.ReadExactly(entry);
            var width = entry[0] == 0 ? 256 : entry[0];
            if (width >= wanted && (best == 0 || width < best))
            {
                best = width;
            }
        }

        return best;
    }
}