using System.Text.Json;
using GoHome.Core;
using GoHome.Diagnostics;

namespace GoHome.Storage;

/// <summary>
/// Файл настроек <c>%APPDATA%\GoHome\settings.json</c> — рядом с каталогом дней.
/// Человекочитаемый и рассчитан на правку руками, как и файлы дней.
/// </summary>
/// <remarks>
/// Беды у него ровно те же, что у файла дня, и обходятся так же: чтение с разрешением
/// на совместный доступ, повторы при занятости, а при неудаче разбора — работа
/// на значениях по умолчанию без единой попытки перезаписать файл. Испорченные настройки
/// человек чинит сам; затирать их своими умолчаниями значит потерять всё, что он настроил.
/// <para>
/// Отсутствие файла ошибкой не является: действуют значения по умолчанию, а файл заводится
/// при первом сохранении.
/// </para>
/// </remarks>
public sealed class SettingsStore
{
    private readonly object _gate = new();
    private readonly ErrorLog _errors;

    private AppSettings _current = AppSettings.Default;
    private bool _unreadable;
    private bool _unreadableAnnounced;
    private string? _saveFailedPath;
    private bool _saveAlertTaken;

    public SettingsStore(string? path = null, ErrorLog? errors = null)
    {
        Path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        _errors = errors ?? ErrorLog.Default;
        Reload();
    }

    /// <summary>Файл настроек изменился — значение уже новое.</summary>
    /// <remarks>Приходит на том потоке, который менял настройки. Форма подписывается на UI-потоке.</remarks>
    public event EventHandler? Changed;

    /// <summary>Путь к файлу настроек.</summary>
    public string Path { get; }

    /// <summary>Путь к файлу настроек по умолчанию.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GoHome",
        "settings.json");

    /// <summary>Действующие настройки. Никогда не <c>null</c>: испорченный файл даёт умолчания.</summary>
    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Файл настроек есть, но не разбирается.</summary>
    public bool IsUnreadable
    {
        get
        {
            lock (_gate)
            {
                return _unreadable;
            }
        }
    }

    /// <summary>Паузы между попытками. Тест укорачивает их, чтобы не ждать.</summary>
    internal IReadOnlyList<TimeSpan> WriteBackoff { get; set; } = JsonFile.DefaultWriteBackoff;

    /// <inheritdoc cref="WriteBackoff"/>
    internal IReadOnlyList<TimeSpan> ReadBackoff { get; set; } = JsonFile.DefaultReadBackoff;

    /// <summary>
    /// Перечитывает файл. Недопустимые значения заменяются умолчаниями, остальные принимаются;
    /// сам файл при этом не трогается.
    /// </summary>
    public AppSettings Reload()
    {
        AppSettings loaded;

        lock (_gate)
        {
            var read = JsonFile.Read<AppSettings>(Path, JsonOptions, ReadBackoff);

            if (read.Status == JsonFileStatus.Unreadable)
            {
                _errors.Write($"не удалось прочитать файл настроек {Path}, работаю на значениях по умолчанию", read.Failure);
                if (!_unreadable)
                {
                    _unreadableAnnounced = false;
                }

                _unreadable = true;
                _current = AppSettings.Default;
                loaded = _current;
            }
            else
            {
                _unreadable = false;
                _unreadableAnnounced = false;

                // Отсутствие файла — не ошибка. Значения по умолчанию, файл заведётся при сохранении.
                loaded = SettingsCheck.Sanitize(read.Value ?? AppSettings.Default, out var replaced);
                foreach (var note in replaced)
                {
                    _errors.Write($"настройки {Path}: {note}");
                }

                _current = loaded;
            }
        }

        return Announce(loaded);
    }

    /// <summary>Сохраняет настройки и объявляет их действующими.</summary>
    /// <remarks>
    /// Новые значения вступают в силу даже если запись не удалась: файл — снимок, истина
    /// на время сессии в памяти. Иначе занятый файл настроек означал бы, что человек
    /// не может ничего изменить.
    /// </remarks>
    /// <returns><c>true</c>, если настройки легли на диск.</returns>
    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool saved;

        lock (_gate)
        {
            _current = settings;

            if (_unreadable)
            {
                // Файл не разбирается: перезапись затёрла бы правку человека, и до тех пор,
                // пока он её не починит, настройки живут только в памяти.
                _errors.Write($"настройки не записаны: файл {Path} не разбирается и не перезаписывается");
                saved = false;
            }
            else if (JsonFile.Write(Path, settings, JsonOptions, WriteBackoff, out var failure))
            {
                _saveFailedPath = null;
                _saveAlertTaken = false;
                saved = true;
            }
            else
            {
                _saveFailedPath = Path;
                _errors.Write($"не удалось сохранить файл настроек {Path}", failure);
                saved = false;
            }
        }

        Announce(settings);
        return saved;
    }

    /// <summary>Забирает право один раз сказать человеку о проблеме с файлом настроек.</summary>
    public StorageAlert? TryTakeAlert()
    {
        lock (_gate)
        {
            if (_unreadable && !_unreadableAnnounced)
            {
                _unreadableAnnounced = true;
                return new StorageAlert(StorageAlertKind.FileUnreadable, Path);
            }

            if (_saveFailedPath is { } path && !_saveAlertTaken)
            {
                _saveAlertTaken = true;
                return new StorageAlert(StorageAlertKind.WriteFailing, path);
            }

            return null;
        }
    }

    /// <summary>Те же послабления, что и у файла дня: комментарии, висящие запятые, любой регистр.</summary>
    private static JsonSerializerOptions JsonOptions => DayLogStore.JsonOptions;

    /// <summary>Событие поднимается вне лока: подписчик волен звать хранилище в ответ.</summary>
    private AppSettings Announce(AppSettings settings)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return settings;
    }
}