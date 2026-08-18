using System.Globalization;
using System.Text;

namespace GoHome.Diagnostics;

/// <summary>
/// Текстовый журнал ошибок рядом с данными: <c>%APPDATA%\GoHome\errors.log</c>.
/// </summary>
/// <remarks>
/// Сам не бросает исключений ни при каких обстоятельствах. Последний рубеж обороны,
/// который валит приложение при неудачной записи, — это не рубеж, а вторая дыра:
/// журнал зовут как раз оттуда, где исключение уже некуда отдавать.
/// </remarks>
public sealed class ErrorLog
{
    /// <summary>С какого размера файл уезжает в резервную копию.</summary>
    private const long RotateAtBytes = 256 * 1024;

    private readonly object _gate = new();
    private readonly string? _path;

    /// <param name="path">Куда писать. <c>null</c> отключает журнал — так удобно в тестах.</param>
    public ErrorLog(string? path) => _path = string.IsNullOrWhiteSpace(path) ? null : path;

    /// <summary>Общий журнал приложения.</summary>
    public static ErrorLog Default { get; } = new(DefaultPath);

    /// <summary>Путь к журналу по умолчанию.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GoHome",
        "errors.log");

    /// <summary>Куда пишет этот экземпляр, либо <c>null</c>, если никуда.</summary>
    public string? FilePath => _path;

    /// <summary>Записывает строку с отметкой времени. Молча ничего не делает, если не вышло.</summary>
    /// <param name="message">Что произошло — своими словами, с именем файла.</param>
    /// <param name="error">Исключение, если оно было.</param>
    public void Write(string message, Exception? error = null)
    {
        if (_path is null)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append("  ")
            .Append(message);

        if (error is not null)
        {
            // Тип и текст — в строке, стек — следующим абзацем: глазами читается сверху вниз.
            line.Append("  [").Append(error.GetType().Name).Append(": ").Append(error.Message).Append(']');
            if (error.StackTrace is { Length: > 0 } stack)
            {
                line.AppendLine().Append(stack);
            }
        }

        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfLarge();
                File.AppendAllText(_path, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Записать не вышло — писать об этом некуда, а падать тем более незачем.
        }
    }

    /// <summary>Разросшийся журнал уезжает в одну резервную копию: место не течёт, вчера читаемо.</summary>
    private void RotateIfLarge()
    {
        if (_path is null || !File.Exists(_path) || new FileInfo(_path).Length < RotateAtBytes)
        {
            return;
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }
}