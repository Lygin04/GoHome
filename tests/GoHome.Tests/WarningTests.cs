using GoHome.Core;
using GoHome.Storage;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Предупреждение «скоро норма». Проверяется на живом каталоге вместе со службой:
/// признак живёт в файле дня, и вся суть — в том, когда он ставится и когда снимается.
/// </summary>
public sealed class WarningTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "gohome-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void По_умолчанию_предупреждение_включено_и_приходит_за_четверть_часа()
    {
        // Заодно ловушка порядка статических полей: объявленное ниже Default значение
        // умолчания дало бы здесь ноль, то есть выключенное предупреждение.
        Assert.Equal(TimeSpan.FromMinutes(15), AppSettings.Default.WarnBefore);
        Assert.True(AppSettings.Default.WarnsBeforeGoal);
    }

    [Fact]
    public void Порог_переживает_запись_и_чтение_файла_настроек()
    {
        var store = TestApp.Settings(_root, Even(Hm(8)) with { WarnBefore = Hm(0, 20) });

        Assert.Equal(Hm(0, 20), store.Reload().WarnBefore);
    }

    [Fact]
    public void Настройки_без_поля_получают_предупреждение_по_умолчанию()
    {
        // Так выглядит файл, созданный версией, в которой предупреждения ещё не было.
        var path = Path.Combine(_root, "settings.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ \"countShortBreaks\": true }");

        var store = TestApp.Settings(_root);

        Assert.Equal(AppSettings.DefaultWarnBefore, store.Current.WarnBefore);
    }

    [Fact]
    public void Порог_длиннее_самого_короткого_дня_отклоняется()
    {
        var settings = Even(Hm(8)) with { WarnBefore = Hm(9) };

        var problems = SettingsCheck.Validate(settings);

        Assert.Contains(problems, problem => problem.Field == "уведомления");
    }

    [Fact]
    public void Порог_короче_дня_принимается()
    {
        var settings = Even(Hm(8)) with { WarnBefore = Hm(0, 15) };

        Assert.Empty(SettingsCheck.Validate(settings));
    }

    [Fact]
    public void Выключенное_предупреждение_проверку_проходит()
    {
        var settings = Even(Hm(1)) with { WarnBefore = TimeSpan.Zero };

        Assert.Empty(SettingsCheck.Validate(settings));
    }

    [Fact]
    public void Порог_длиннее_дня_в_файле_заменяется_умолчанием()
    {
        var settings = Even(Hm(8)) with { WarnBefore = Hm(9) };

        var fixedUp = SettingsCheck.Sanitize(settings, out var replaced);

        Assert.Equal(AppSettings.DefaultWarnBefore, fixedUp.WarnBefore);
        Assert.Contains(replaced, note => note.StartsWith("уведомления", StringComparison.Ordinal));
    }

    [Fact]
    public void Порог_длиннее_дня_при_коротком_дне_выключается()
    {
        // Умолчание в такой день тоже не влезает — предупреждать нечем.
        var settings = Even(Hm(0, 10)) with { WarnBefore = Hm(2) };

        var fixedUp = SettingsCheck.Sanitize(settings, out _);

        Assert.Equal(TimeSpan.Zero, fixedUp.WarnBefore);
    }

    [Fact]
    public void Выключенное_предупреждение_не_приходит()
    {
        var service = TestApp.Service(_root, Even(Hm(8)) with { WarnBefore = TimeSpan.Zero });
        service.RecordReturn(At(9), "unlock");

        Assert.Null(service.TryTakeWarningNotification(At(16, 45)));

        // Уведомление о норме при этом работает как работало.
        Assert.True(service.TryTakeGoalNotification(At(17)));
    }

    [Fact]
    public void До_порога_предупреждение_молчит()
    {
        var service = TestApp.Service(_root, Even(Hm(8)) with { WarnBefore = Hm(0, 15) });
        service.RecordReturn(At(9), "unlock");

        Assert.Null(service.TryTakeWarningNotification(At(16, 44)));
    }

    [Fact]
    public void На_пороге_предупреждение_приходит_один_раз()
    {
        var service = TestApp.Service(_root, Even(Hm(8)) with { WarnBefore = Hm(0, 15) });
        service.RecordReturn(At(9), "unlock");

        Assert.Equal(Hm(0, 15), service.TryTakeWarningNotification(At(16, 45)));
        Assert.Null(service.TryTakeWarningNotification(At(16, 50)));
    }

    [Fact]
    public void После_предупреждения_приходит_уведомление_о_норме()
    {
        var service = TestApp.Service(_root, Even(Hm(8)) with { WarnBefore = Hm(0, 15) });
        service.RecordReturn(At(9), "unlock");

        Assert.NotNull(service.TryTakeWarningNotification(At(16, 45)));

        Assert.Null(service.TryTakeWarningNotification(At(17)));
        Assert.True(service.TryTakeGoalNotification(At(17)));
    }

    [Fact]
    public void Пересчёт_через_оба_порога_даёт_только_уведомление_о_норме()
    {
        var service = TestApp.Service(_root, Even(Hm(8)) with { WarnBefore = Hm(0, 15) });

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(14), "unlock");

        // Час обеда вне зачёта: до нормы ещё далеко, предупреждать не о чем.
        Assert.Null(service.TryTakeWarningNotification(At(17, 10)));

        // Пометку сняли — счётчик прыгнул сразу через оба порога.
        service.Reclassify(Today, At(13), BreakKind.Paid, "test");

        Assert.Null(service.TryTakeWarningNotification(At(17, 10)));
        Assert.True(service.TryTakeGoalNotification(At(17, 10)));
    }

    [Fact]
    public void В_нерабочий_день_не_приходит_ни_одно()
    {
        var service = TestApp.Service(_root, AppSettings.Default with { Schedule = Flat(null) });
        service.RecordReturn(At(9), "unlock");

        Assert.Null(service.TryTakeWarningNotification(At(18)));
        Assert.False(service.TryTakeGoalNotification(At(18)));
    }

    [Fact]
    public void В_паузе_предупреждение_не_возникает()
    {
        // Правила, в которых счёт останавливает любая блокировка: только так пауза
        // и видна как пауза. При зачёте коротких отлучек вернувшийся человек получает
        // это время обратно, и «в паузе» проверять становится нечего.
        var settings = Even(Hm(8)) with { CountShortBreaks = false, WarnBefore = Hm(0, 15) };
        var service = TestApp.Service(_root, settings);

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(15), TimeSpan.Zero, "lock");

        // Два часа простоя не капают: до нормы по-прежнему два часа.
        Assert.Null(service.TryTakeWarningNotification(At(17)));

        service.RecordReturn(At(17), "unlock");
        Assert.Equal(Hm(0, 15), service.TryTakeWarningNotification(At(18, 45)));
    }

    [Fact]
    public void Повышенная_цель_сбрасывает_оба_признака()
    {
        var service = TestApp.Service(_root, Even(Hm(8)) with { WarnBefore = Hm(0, 15) });
        service.RecordReturn(At(9), "unlock");

        Assert.NotNull(service.TryTakeWarningNotification(At(16, 45)));
        Assert.True(service.TryTakeGoalNotification(At(17)));

        service.SaveSettings(Even(Hm(9)) with { WarnBefore = Hm(0, 15) }, At(17, 5));

        // Признаки снимаются на ближайшем опросе, а не в момент правки настроек:
        // тот же механизм, что и у уведомления о норме.
        Assert.Null(service.TryTakeWarningNotification(At(17, 5)));
        Assert.False(service.TryTakeGoalNotification(At(17, 5)));

        var log = new DayLogStore(_root).Load(Today);
        Assert.False(log.WarningNotified);
        Assert.False(log.GoalNotified);

        // И оба приходят заново, уже по новой цели.
        Assert.NotNull(service.TryTakeWarningNotification(At(17, 45)));
        Assert.True(service.TryTakeGoalNotification(At(18)));
    }
}
