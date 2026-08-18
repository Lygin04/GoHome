using GoHome.Core;
using GoHome.Diagnostics;
using GoHome.Storage;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Файл настроек и проверка значений. Тот же класс бед, что у файла дня: его правят
/// руками, держат открытым и портят опечаткой — и ни одно из этого не должно мешать
/// приложению считать время.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "gohome-tests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_root, "settings.json");

    private SettingsStore Store => new(SettingsPath) { WriteBackoff = [], ReadBackoff = [] };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---- файл -----------------------------------------------------------------------

    [Fact]
    public void Отсутствующий_файл_даёт_значения_по_умолчанию()
    {
        var store = Store;

        Assert.Equal(AppSettings.Default, store.Current);
        Assert.False(store.IsUnreadable);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Настройки_переживают_запись_и_чтение()
    {
        var settings = AppSettings.Default with
        {
            CountShortBreaks = false,
            Theme = AppTheme.Dark,
            Schedule = Flat(Hm(7)) with { Sunday = null },
            Exceptions = [new DateException { Date = Today, Hours = null, Note = "отпуск" }],
        };

        Assert.True(Store.Save(settings));

        var reloaded = Store.Current;
        Assert.False(reloaded.CountShortBreaks);
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.Equal(Hm(7), reloaded.Schedule.Monday);
        Assert.Null(reloaded.Schedule.Sunday);
        Assert.Equal("отпуск", reloaded.Exceptions.Single().Note);
        Assert.True(reloaded.Exceptions.Single().IsDayOff);
    }

    [Fact]
    public void Нерабочий_день_переживает_запись()
    {
        // Пустая цель обязана дойти до файла: иначе нерабочий день не отличить
        // от файла, в котором поля просто нет.
        Store.Save(AppSettings.Default with { Schedule = Flat(null) });

        var text = File.ReadAllText(SettingsPath);
        Assert.Contains("\"monday\": null", text, StringComparison.Ordinal);
        Assert.All(WeekSchedule.Days, day => Assert.Null(Store.Current.Schedule[day]));
    }

    [Fact]
    public void Файл_остаётся_человекочитаемым()
    {
        Store.Save(AppSettings.Default with { Theme = AppTheme.Light });

        var text = File.ReadAllText(SettingsPath);

        Assert.Contains("\"theme\": \"Light\"", text, StringComparison.Ordinal);   // не число
        Assert.Contains("\"countShortBreaks\": true", text, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine, text);                                 // с отступами
    }

    [Fact]
    public void Испорченный_файл_даёт_умолчания_и_не_затирается()
    {
        Directory.CreateDirectory(_root);
        const string broken = "{ \"countShortBreaks\": fa";
        File.WriteAllText(SettingsPath, broken);

        var store = Store;

        Assert.True(store.IsUnreadable);
        Assert.Equal(AppSettings.Default, store.Current);

        // Приложение работает дальше, а правку человека не трогает.
        Assert.False(store.Save(AppSettings.Default with { Theme = AppTheme.Dark }));
        Assert.Equal(broken, File.ReadAllText(SettingsPath));

        // Но в памяти новое значение уже действует: занятый файл не повод запретить настройку.
        Assert.Equal(AppTheme.Dark, store.Current.Theme);
    }

    [Fact]
    public void Про_испорченный_файл_говорится_один_раз()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, "{ \"schedule\": ");

        var store = Store;
        var alert = store.TryTakeAlert();

        Assert.Equal(StorageAlertKind.FileUnreadable, alert?.Kind);
        Assert.Equal(SettingsPath, alert?.Path);
        Assert.Null(store.TryTakeAlert());
    }

    [Fact]
    public void Файл_открытый_на_чтение_читается()
    {
        Store.Save(AppSettings.Default with { Theme = AppTheme.Dark });

        using var reader = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.Equal(AppTheme.Dark, Store.Current.Theme);
    }

    [Fact]
    public void Правки_руками_переживают_вольности_json()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, """
            {
              // выключил зачёт коротких отлучек, хочу как раньше
              "countShortBreaks": false,
              "schedule": { "monday": "07:00:00", },
            }
            """);

        var settings = Store.Current;

        Assert.False(settings.CountShortBreaks);
        Assert.Equal(Hm(7), settings.Schedule.Monday);
    }

    [Fact]
    public void Смена_настроек_объявляется_сразу()
    {
        var store = Store;
        var heard = 0;
        store.Changed += (_, _) => heard++;

        store.Save(AppSettings.Default with { Theme = AppTheme.Dark });

        Assert.Equal(1, heard);
        Assert.Equal(AppTheme.Dark, store.Current.Theme);
    }

    // ---- проверка значений ----------------------------------------------------------

    [Fact]
    public void Порог_отлучки_больше_минимума_обеда_отклоняется()
    {
        var settings = AppSettings.Default with
        {
            Lunch = LunchRules.Default with { Minimum = Hm(0, 40), GuessMinimum = Hm(0, 25) },
        };

        var problems = SettingsCheck.Validate(settings);

        // Иначе обед не определится никогда, а по одному полю этого не увидеть.
        Assert.Contains(problems, problem => problem.Message.Contains("не определится никогда", StringComparison.Ordinal));
    }

    [Fact]
    public void Вывернутое_обеденное_окно_отклоняется()
    {
        var settings = AppSettings.Default with
        {
            Lunch = LunchRules.Default with { WindowStart = new TimeOnly(15, 0), WindowEnd = new TimeOnly(11, 30) },
        };

        Assert.Contains(SettingsCheck.Validate(settings), problem => problem.Field == "учёт времени");
    }

    [Fact]
    public void Слишком_узкое_окно_отклоняется()
    {
        var settings = AppSettings.Default with
        {
            Lunch = LunchRules.Default with { WindowEnd = new TimeOnly(11, 40) },   // окно в 10 минут
        };

        Assert.Contains(SettingsCheck.Validate(settings), problem => problem.Field == "учёт времени");
    }

    [Fact]
    public void Нулевая_и_запредельная_продолжительность_отклоняются()
    {
        Assert.NotEmpty(SettingsCheck.Validate(AppSettings.Default with { Schedule = Flat(TimeSpan.Zero) }));
        Assert.NotEmpty(SettingsCheck.Validate(AppSettings.Default with { Schedule = Flat(Hm(25)) }));
        Assert.Empty(SettingsCheck.Validate(AppSettings.Default with { Schedule = Flat(null) }));
    }

    [Fact]
    public void Настройки_обеда_при_выключенном_зачёте_не_проверяются()
    {
        var broken = LunchRules.Default with { Minimum = Hm(3), GuessMinimum = Hm(0, 1) };

        // Они не влияют ни на что, и запрещать из-за них сохранение незачем.
        Assert.NotEmpty(SettingsCheck.Validate(AppSettings.Default with { Lunch = broken }));
        Assert.Empty(SettingsCheck.Validate(AppSettings.Default with { Lunch = broken, CountShortBreaks = false }));
    }

    [Fact]
    public void Повтор_даты_в_исключениях_отклоняется()
    {
        var settings = AppSettings.Default with
        {
            Exceptions =
            [
                new DateException { Date = Today, Hours = Hm(7) },
                new DateException { Date = Today, Hours = null },
            ],
        };

        Assert.Contains(SettingsCheck.Validate(settings), problem => problem.Field == "исключения");
    }

    [Fact]
    public void Негодное_значение_чинится_по_месту_а_остальное_принимается()
    {
        var log = Path.Combine(_root, "errors.log");
        Directory.CreateDirectory(_root);

        // Залётный минус в продолжительности и порог отлучки больше минимума обеда:
        // разобрать такое можно, а работать по нему нельзя.
        File.WriteAllText(SettingsPath, """
            {
              "theme": "Dark",
              "schedule": { "monday": "-08:00:00", "tuesday": "07:00:00" },
              "lunch": { "windowStart": "11:30:00", "windowEnd": "15:00:00",
                         "minimum": "00:40:00", "guessMinimum": "00:25:00" }
            }
            """);

        var store = new SettingsStore(SettingsPath, new ErrorLog(log)) { WriteBackoff = [], ReadBackoff = [] };
        var settings = store.Current;

        // Негодное заменено умолчанием...
        Assert.Equal(WorkTimeCalculator.DefaultGoal, settings.Schedule.Monday);
        Assert.Equal(LunchRules.Default.Minimum, settings.Lunch.Minimum);

        // ...а годное принято.
        Assert.Equal(Hm(7), settings.Schedule.Tuesday);
        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.False(store.IsUnreadable);

        // И файл при этом не тронут, а замены записаны.
        Assert.Contains("\"monday\": \"-08:00:00\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.Contains("заменен", File.ReadAllText(log), StringComparison.Ordinal);
    }

    [Fact]
    public void Неразбираемое_значение_делает_файл_испорченным_целиком()
    {
        Directory.CreateDirectory(_root);

        // «99:00:00» — не длительность вовсе, и разбор обрывается на ней. Починить
        // по месту тут нечего: до остальных полей чтение просто не доходит.
        const string text = """{ "schedule": { "monday": "99:00:00" }, "theme": "Dark" }""";
        File.WriteAllText(SettingsPath, text);

        var store = Store;

        Assert.True(store.IsUnreadable);
        Assert.Equal(AppSettings.Default, store.Current);
        Assert.Equal(text, File.ReadAllText(SettingsPath));
    }
}