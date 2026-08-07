using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoHome.Core;

namespace GoHome.Storage;

/// <summary>
/// Файл на день в <c>%APPDATA%\GoHome\days</c>. JSON человекочитаемый: заказчик
/// дописывает забытые отметки руками в блокноте, и пересчёт подхватывает их сам.
/// </summary>
/// <remarks>
/// Запись атомарная (temp + подмена): внезапный ребут не должен оставить обрезанный JSON.
/// Все обращения под локом — события сессии приходят на своём потоке, а таймер на UI-потоке.
/// </remarks>
public sealed class DayLogStore
{
    private const string FileDateFormat = "yyyy-MM-dd";
    private const int WriteAttempts = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Послабления для файла, который правят руками.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    public DayLogStore()
        : this(DefaultRoot)
    {
    }

    public DayLogStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
        Directory.CreateDirectory(root);
    }

    /// <summary>Каталог с файлами дней.</summary>
    public string Root { get; }

    /// <summary>Каталог по умолчанию.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GoHome",
        "days");

    /// <summary>Путь к файлу дня.</summary>
    public string PathFor(DateOnly date) =>
        Path.Combine(Root, date.ToString(FileDateFormat, CultureInfo.InvariantCulture) + ".json");

    /// <summary>Читает журнал. Отсутствующий файл — это пустой день, а не ошибка.</summary>
    public DayLog Load(DateOnly date)
    {
        lock (_gate)
        {
            return LoadUnlocked(date);
        }
    }

    /// <summary>Читает журналы за диапазон рабочих дат включительно.</summary>
    public IReadOnlyList<DayLog> LoadRange(DateOnly from, DateOnly to)
    {
        lock (_gate)
        {
            var logs = new List<DayLog>();
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                logs.Add(LoadUnlocked(date));
            }

            return logs;
        }
    }

    /// <summary>Читает, меняет и сохраняет журнал одной неделимой операцией.</summary>
    public T Update<T>(DateOnly date, Func<DayLog, T> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_gate)
        {
            var log = LoadUnlocked(date);
            var result = mutate(log);
            SaveUnlocked(log);
            return result;
        }
    }

    /// <summary>
    /// То же, но журнал сохраняется, только если изменение состоялось.
    /// Иначе в каталоге заводились бы пустые файлы за дни, в которые никто не приходил.
    /// </summary>
    public bool TryUpdate(DateOnly date, Func<DayLog, bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_gate)
        {
            var log = LoadUnlocked(date);
            if (!mutate(log))
            {
                return false;
            }

            SaveUnlocked(log);
            return true;
        }
    }

    /// <inheritdoc cref="Update{T}"/>
    public void Update(DateOnly date, Action<DayLog> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        Update(date, log =>
        {
            mutate(log);
            return true;
        });
    }

    /// <summary>Сохраняет журнал целиком.</summary>
    public void Save(DayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        lock (_gate)
        {
            SaveUnlocked(log);
        }
    }

    /// <summary>Рабочие даты, для которых есть файлы, от старых к новым.</summary>
    public IReadOnlyList<DateOnly> ListDates()
    {
        lock (_gate)
        {
            if (!Directory.Exists(Root))
            {
                return [];
            }

            var dates = new List<DateOnly>();
            foreach (var file in Directory.EnumerateFiles(Root, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (DateOnly.TryParseExact(name, FileDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    dates.Add(date);
                }
            }

            dates.Sort();
            return dates;
        }
    }

    private DayLog LoadUnlocked(DateOnly date)
    {
        var path = PathFor(date);
        if (!File.Exists(path))
        {
            return DayLog.Empty(date);
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var log = JsonSerializer.Deserialize<DayLog>(stream, JsonOptions) ?? DayLog.Empty(date);
            // Имя файла авторитетнее поля внутри: его человек переименовать не мог, а поле — мог.
            log.Date = date;
            log.Punches ??= [];
            return log;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Скорее всего в файле опечатка после ручной правки. Ничего не теряем и не трогаем.
            return new DayLog { Date = date, IsUnreadable = true };
        }
    }

    private void SaveUnlocked(DayLog log)
    {
        if (log.IsUnreadable)
        {
            // Файл не разобрался — перезапись затёрла бы правку человека.
            return;
        }

        var path = PathFor(log.Date);
        var temp = path + ".tmp";

        Directory.CreateDirectory(Root);

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, log, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        ReplaceWithRetry(temp, path);
    }

    /// <summary>Подменяет файл, переживая мгновенные блокировки от антивируса и индексатора.</summary>
    private static void ReplaceWithRetry(string temp, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < WriteAttempts)
            {
                Thread.Sleep(attempt * 50);
            }
            catch (UnauthorizedAccessException) when (attempt < WriteAttempts)
            {
                Thread.Sleep(attempt * 50);
            }
        }
    }
}