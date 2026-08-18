using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoHome.Core;
using GoHome.Diagnostics;

namespace GoHome.Storage;

/// <summary>
/// Файл на день в <c>%APPDATA%\GoHome\days</c>. JSON человекочитаемый: заказчик
/// дописывает забытые отметки руками в блокноте, и пересчёт подхватывает их сам.
/// </summary>
/// <remarks>
/// <para>
/// Запись атомарная (temp + подмена): внезапный ребут не должен оставить обрезанный JSON.
/// Все обращения под локом — события сессии приходят на своём потоке, а таймер на UI-потоке.
/// </para>
/// <para>
/// Ни одна операция здесь не выпускает наружу исключений ввода-вывода. Файл дня может быть
/// занят антивирусом, проводником или блокнотом заказчика, и это нормальный ход событий,
/// а не повод останавливать учёт времени: истина на время сессии живёт в памяти, файл —
/// всего лишь её снимок. Неудачная запись означает «снимок устарел»: день остаётся
/// в <see cref="_pending"/> и целиком уезжает на диск следующей удачной записью.
/// </para>
/// </remarks>
public sealed class DayLogStore
{
    private const string FileDateFormat = "yyyy-MM-dd";
    private const string DayExtension = ".json";

    /// <summary>После скольких неудач подряд стоит потревожить человека.</summary>
    private const int SaveFailuresBeforeAlert = 3;

    /// <summary>Как читается и пишется файл дня. Те же послабления и в файле настроек.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
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
    private readonly ErrorLog _errors;

    /// <summary>Дни, накопленное состояние которых ещё не легло на диск.</summary>
    private readonly Dictionary<DateOnly, DayLog> _pending = [];

    /// <summary>Дни, о нечитаемости которых человеку уже сказали.</summary>
    private readonly HashSet<DateOnly> _announcedUnreadable = [];

    private DateOnly? _unreadable;
    private string? _lastFailedPath;
    private int _saveFailures;
    private bool _saveAlertTaken;

    public DayLogStore()
        : this(DefaultRoot)
    {
    }

    public DayLogStore(string root, ErrorLog? errors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
        _errors = errors ?? ErrorLog.Default;

        if (TryCreateRoot())
        {
            // Обрывки прошлых сбоев в каталоге не копятся и в историю не попадают.
            RemoveStaleTemporaries();
        }
    }

    /// <summary>Каталог с файлами дней.</summary>
    public string Root { get; }

    /// <summary>Каталог по умолчанию.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GoHome",
        "days");

    /// <summary>Есть ли дни, состояние которых пока живёт только в памяти.</summary>
    public bool HasPendingWrites
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    /// <summary>Сколько записей подряд не удалось довести до диска.</summary>
    public int SaveFailures
    {
        get
        {
            lock (_gate)
            {
                return _saveFailures;
            }
        }
    }

    /// <summary>Паузы между попытками подменить занятый файл. Тест укорачивает их, чтобы не ждать.</summary>
    internal IReadOnlyList<TimeSpan> WriteBackoff { get; set; } = JsonFile.DefaultWriteBackoff;

    /// <inheritdoc cref="WriteBackoff"/>
    internal IReadOnlyList<TimeSpan> ReadBackoff { get; set; } = JsonFile.DefaultReadBackoff;

    /// <summary>Путь к файлу дня.</summary>
    public string PathFor(DateOnly date) =>
        Path.Combine(Root, date.ToString(FileDateFormat, CultureInfo.InvariantCulture) + DayExtension);

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
    /// <returns>
    /// <c>true</c>, если изменение состоялось. Именно изменение, а не запись на диск:
    /// не легший на диск день не потерян, он уедет туда следующей удачной записью.
    /// </returns>
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
    /// <returns><c>true</c>, если журнал лёг на диск.</returns>
    public bool Save(DayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        lock (_gate)
        {
            return SaveUnlocked(log);
        }
    }

    /// <summary>
    /// Досылает на диск всё, что накопилось в памяти после неудачных записей.
    /// Зовётся там, где второго шанса может не быть: пауза, завершение сессии, выход.
    /// </summary>
    /// <returns><c>true</c>, если досылать было нечего или всё удалось.</returns>
    public bool Flush()
    {
        lock (_gate)
        {
            var everything = true;
            foreach (var log in _pending.Values.ToList())
            {
                everything &= SaveUnlocked(log);
            }

            return everything;
        }
    }

    /// <summary>Рабочие даты, для которых есть данные, от старых к новым.</summary>
    public IReadOnlyList<DateOnly> ListDates()
    {
        lock (_gate)
        {
            // Дни, ещё не легшие на диск, — такая же часть истории, как и файлы.
            var dates = new SortedSet<DateOnly>(_pending.Keys);

            try
            {
                if (Directory.Exists(Root))
                {
                    foreach (var file in Directory.EnumerateFiles(Root))
                    {
                        if (DateOf(file) is { } date)
                        {
                            dates.Add(date);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _errors.Write($"не удалось перечислить каталог дней {Root}", ex);
            }

            return [.. dates];
        }
    }

    /// <summary>
    /// Забирает право один раз пожаловаться на проблему с файлами. Пока проблема держится,
    /// повторно она не возвращается: человеку хватит одной подсказки, а не одной на каждую минуту.
    /// </summary>
    public StorageAlert? TryTakeAlert()
    {
        lock (_gate)
        {
            if (_unreadable is { } date)
            {
                _unreadable = null;
                return new StorageAlert(StorageAlertKind.FileUnreadable, PathFor(date));
            }

            if (_saveFailures >= SaveFailuresBeforeAlert && !_saveAlertTaken)
            {
                _saveAlertTaken = true;
                return new StorageAlert(StorageAlertKind.WriteFailing, _lastFailedPath ?? Root);
            }

            return null;
        }
    }

    private DayLog LoadUnlocked(DateOnly date)
    {
        if (_pending.TryGetValue(date, out var pending))
        {
            // Состояние в памяти новее того, что успело лечь на диск.
            return pending;
        }

        var path = PathFor(date);
        var read = JsonFile.Read<DayLog>(path, JsonOptions, ReadBackoff);

        if (read.Status == JsonFileStatus.Unreadable)
        {
            _errors.Write($"не удалось прочитать файл дня {path}", read.Failure);
            MarkUnreadable(date);

            // Ничего не теряем и не трогаем: пусть человек починит файл руками.
            return new DayLog { Date = date, IsUnreadable = true };
        }

        MarkReadable(date);
        if (read.Value is not { } log)
        {
            return DayLog.Empty(date);
        }

        // Имя файла авторитетнее поля внутри: его человек переименовать не мог, а поле — мог.
        log.Date = date;
        log.Punches ??= [];
        return log;
    }

    /// <returns><c>true</c>, если журнал лёг на диск.</returns>
    private bool SaveUnlocked(DayLog log)
    {
        if (log.IsUnreadable)
        {
            // Файл не разобрался — перезапись затёрла бы правку человека. И в память
            // такой день не берём: иначе следующая удачная запись затрёт его же.
            return false;
        }

        var path = PathFor(log.Date);

        if (JsonFile.Write(path, log, JsonOptions, WriteBackoff, out var failure))
        {
            _pending.Remove(log.Date);
            _saveFailures = 0;
            _saveAlertTaken = false;
            return true;
        }

        // Снимок устарел, данные целы: день ждёт в памяти следующей удачной записи.
        _pending[log.Date] = log;
        _lastFailedPath = path;
        _saveFailures++;
        _errors.Write($"не удалось сохранить файл дня {path} (неудач подряд: {_saveFailures})", failure);
        return false;
    }

    private bool TryCreateRoot()
    {
        try
        {
            Directory.CreateDirectory(Root);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Каталога нет и не будет — но приложение всё равно должно подняться и считать время.
            _errors.Write($"не удалось создать каталог дней {Root}", ex);
            return false;
        }
    }

    private void RemoveStaleTemporaries()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*" + JsonFile.TempExtension).Where(IsTemporary))
            {
                if (!JsonFile.Delete(file))
                {
                    _errors.Write($"не удалось удалить временный файл {file}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Write($"не удалось убрать временные файлы в {Root}", ex);
        }
    }

    private void MarkUnreadable(DateOnly date)
    {
        if (_announcedUnreadable.Add(date))
        {
            _unreadable = date;
        }
    }

    private void MarkReadable(DateOnly date)
    {
        // Файл починили — про следующую поломку сказать можно снова.
        _announcedUnreadable.Remove(date);
        if (_unreadable == date)
        {
            _unreadable = null;
        }
    }

    /// <summary>
    /// Рабочая дата файла дня, либо <c>null</c> для всего постороннего. В каталоге данных
    /// живут не только файлы дней: заметки, обрывки записей, случайно скопированное.
    /// </summary>
    private static DateOnly? DateOf(string file)
    {
        if (!string.Equals(Path.GetExtension(file), DayExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(file);
        return DateOnly.TryParseExact(name, FileDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Наш ли это обрывок записи. Чужой <c>.tmp</c> в каталоге — не наше дело.</summary>
    private static bool IsTemporary(string file)
    {
        if (!string.Equals(Path.GetExtension(file), JsonFile.TempExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // «2026-08-14.json.<уникальное>.tmp». Однократное отсечение узнаёт ещё и обрывки
        // прошлых версий, у которых имя было просто «2026-08-14.json.tmp».
        var stripped = Path.GetFileNameWithoutExtension(file);
        return DateOf(stripped) is not null || DateOf(Path.GetFileNameWithoutExtension(stripped)) is not null;
    }
}