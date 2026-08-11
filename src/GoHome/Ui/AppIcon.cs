using System.Buffers.Binary;

namespace GoHome.Ui;

/// <summary>
/// Иконка приложения — постоянная картинка для окон и для трея до первой отрисовки кольца.
/// </summary>
/// <remarks>
/// С кольцом из <see cref="TrayRingPainter"/> не пересекается: кольцо меняет заполнение
/// и цвет на каждом тике, а эта иконка неизменна. Файл лежит в сборке ресурсом —
/// рядом с однофайловым exe никаких файлов нет.
/// </remarks>
public static class AppIcon
{
    private const string ResourceName = "GoHome.Resources.gohome.ico";

    private static readonly Lazy<Icon?> AllSizes = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Icon?> TraySize = new(SelectTraySize, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Все размеры сразу — для окон: Windows сама возьмёт подходящий для заголовка
    /// и для переключателя окон. Живёт до конца процесса, освобождать не нужно.
    /// </summary>
    public static Icon? ForWindows => AllSizes.Value;

    /// <summary>Кадр под размер иконки трея. Живёт до конца процесса, освобождать не нужно.</summary>
    public static Icon? ForTray => TraySize.Value;

    private static Icon? Load()
    {
        try
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
            return stream is null ? null : new Icon(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // Без иконки приложение работает — Windows подставит заглушку.
            return null;
        }
    }

    private static Icon? SelectTraySize()
    {
        try
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            var side = FrameFor(stream, TrayRing.IconSize);
            stream.Position = 0;
            return new Icon(stream, side, side);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return AllSizes.Value;
        }
    }

    /// <summary>
    /// Наименьший из нарисованных кадров, который не меньше нужного. На 125% DPI трею
    /// нужны 20 пикселей: система уменьшит кадр 24 чище, чем растянет кадр 16.
    /// Сами кадры не трогаем — мелкие нарисованы вручную.
    /// </summary>
    private static int FrameFor(Stream stream, int wanted)
    {
        Span<byte> header = stackalloc byte[6];
        stream.ReadExactly(header);

        var count = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        var best = 0;

        Span<byte> entry = stackalloc byte[16];
        for (var i = 0; i < count; i++)
        {
            stream.ReadExactly(entry);

            // Нулевая ширина в каталоге .ico означает 256.
            var width = entry[0] == 0 ? 256 : entry[0];
            if (width >= wanted && (best == 0 || width < best))
            {
                best = width;
            }
        }

        return best == 0 ? wanted : best;
    }
}
