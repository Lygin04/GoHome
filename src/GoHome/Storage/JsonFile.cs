using System.Text.Json;

namespace GoHome.Storage;

/// <summary>Чем закончилось чтение файла.</summary>
internal enum JsonFileStatus
{
    /// <summary>Прочитан и разобран.</summary>
    Loaded,

    /// <summary>Файла нет — это не ошибка.</summary>
    Missing,

    /// <summary>Есть, но не читается или не разбирается. Трогать его нельзя.</summary>
    Unreadable,
}

/// <summary>Результат чтения.</summary>
internal readonly record struct JsonRead<T>(T? Value, JsonFileStatus Status, Exception? Failure)
    where T : class;

/// <summary>
/// Чтение и запись json-файла, переживающие занятость файла.
/// </summary>
/// <remarks>
/// Общее место для файла дня и файла настроек: у них один и тот же набор бед. Оба лежат
/// в каталоге, который человек открывает проводником и правит блокнотом, оба читает
/// антивирус, и ни один не имеет права уронить приложение своей недоступностью.
/// </remarks>
internal static class JsonFile
{
    /// <summary>
    /// Паузы между попытками подменить файл — около пяти секунд в сумме. Столько занятость
    /// от антивируса или обновляющегося проводника обычно и живёт. Дольше ждать нельзя:
    /// запись идёт с UI-потока, и это время значок в трее не отвечает.
    /// </summary>
    public static readonly TimeSpan[] DefaultWriteBackoff =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(2000),
    ];

    /// <summary>
    /// Чтение отступает всего дважды: файл открывается с разрешением на совместный доступ,
    /// поэтому мешать может только очень короткая эксклюзивная блокировка.
    /// </summary>
    public static readonly TimeSpan[] DefaultReadBackoff =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
    ];

    /// <summary>Расширение обрывков записи.</summary>
    public const string TempExtension = ".tmp";

    /// <summary>Читает файл. Ничего не бросает: неудача — это часть результата.</summary>
    public static JsonRead<T> Read<T>(string path, JsonSerializerOptions options, IReadOnlyList<TimeSpan> backoff)
        where T : class
    {
        if (!File.Exists(path))
        {
            return new JsonRead<T>(null, JsonFileStatus.Missing, null);
        }

        Exception? failure;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Совместный доступ: файл человек открывает в блокноте прямо на ходу.
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                var value = JsonSerializer.Deserialize<T>(stream, options);
                return value is null
                    ? new JsonRead<T>(null, JsonFileStatus.Missing, null)
                    : new JsonRead<T>(value, JsonFileStatus.Loaded, null);
            }
            catch (JsonException ex)
            {
                // Опечатка после ручной правки. Повторы ничего не изменят, ждать нечего.
                failure = ex;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = ex;
                if (attempt >= backoff.Count)
                {
                    break;
                }

                Thread.Sleep(backoff[attempt]);
            }
        }

        return new JsonRead<T>(null, JsonFileStatus.Unreadable, failure);
    }

    /// <summary>
    /// Пишет через временный файл с подменой: внезапный ребут не должен оставить
    /// обрезанный json. Ничего не бросает.
    /// </summary>
    /// <param name="failure">Что помешало, если не вышло.</param>
    public static bool Write<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        IReadOnlyList<TimeSpan> backoff,
        out Exception? failure)
    {
        failure = null;

        // Имя уникально на попытку: с общим именем два одновременных сохранения
        // затирали бы друг другу промежуточный результат.
        var temp = $"{path}.{Guid.NewGuid():N}{TempExtension}";

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true);
            }

            if (Replace(temp, path, backoff, out failure))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            failure = ex;
        }

        Delete(temp);
        return false;
    }

    /// <summary>Подменяет файл, переживая блокировки от антивируса, индексатора и проводника.</summary>
    private static bool Replace(string temp, string path, IReadOnlyList<TimeSpan> backoff, out Exception? failure)
    {
        failure = null;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = ex;
                if (attempt >= backoff.Count)
                {
                    return false;
                }

                Thread.Sleep(backoff[attempt]);
            }
        }
    }

    /// <summary>Удаляет файл, если получится. Не получилось — не беда.</summary>
    public static bool Delete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}