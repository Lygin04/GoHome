using GoHome.App;
using GoHome.Core;
using GoHome.Storage;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Обед со стороны приложения: версия правил в новом дне, уведомление, отмена догадки
/// и флаг уведомления о норме, который перестал быть односторонним.
/// </summary>
public sealed class LunchServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "gohome-tests",
        Guid.NewGuid().ToString("N"));

    private readonly DayLogStore _store;
    private readonly SettingsStore _settings;
    private readonly GoHomeService _service;

    public LunchServiceTests()
    {
        _store = new DayLogStore(_root);
        _settings = TestApp.Settings(_root);
        _service = new GoHomeService(_store, _settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Новый_день_получает_снимок_действующих_настроек()
    {
        _service.RecordReturn(At(9), "unlock");

        var rules = _store.Load(Today).Rules;
        Assert.NotNull(rules);
        Assert.True(rules.CountShortBreaks);
        Assert.Equal(WorkTimeCalculator.DefaultGoal, rules.Goal);
        Assert.Equal(LunchRules.Default, rules.Lunch);
    }

    [Fact]
    public void Правила_дня_не_меняются_после_создания()
    {
        // Обновление приехало в середине дня: день обязан досчитаться по своим правилам.
        _store.Save(Log(In(9)).By(AllBreaksCut));

        _service.RecordPause(At(12, 30), TimeSpan.Zero, "lock");
        _service.RecordReturn(At(13, 15), "unlock");

        Assert.False(_store.Load(Today).Rules?.CountShortBreaks);
        Assert.Equal(Hm(3, 30), _service.Summarize(At(13, 15)).Worked);
    }

    [Fact]
    public void Смена_настроек_посреди_дня_не_трогает_правила_расчёта()
    {
        _service.RecordReturn(At(9), "unlock");

        // Пятиминутная отлучка вне обеденного окна: при включённом зачёте идёт
        // в рабочее время, при выключенном — режется. На ней разница и видна.
        _service.RecordPause(At(10), TimeSpan.Zero, "lock");
        _service.RecordReturn(At(10, 5), "unlock");
        Assert.Equal(Hm(4), _service.Summarize(At(13)).Worked);

        _service.SaveSettings(_service.Settings with { CountShortBreaks = false }, At(13));

        // Первая половина дня не должна оказаться посчитана иначе, чем вторая.
        Assert.True(_store.Load(Today).Rules?.CountShortBreaks);
        Assert.Equal(Hm(4), _service.Summarize(At(13)).Worked);
    }

    [Fact]
    public void Смена_цели_посреди_дня_доходит_до_сегодняшнего_дня()
    {
        _service.RecordReturn(At(9), "unlock");

        // Цель — не правило: она только сравнивается с итогом, и поправить её
        // сегодняшним числом безопасно.
        _service.SaveSettings(_service.Settings with { Schedule = Flat(Hm(6)) }, At(13));

        Assert.Equal(Hm(6), _store.Load(Today).Rules?.Goal);
        Assert.Equal(Hm(6), _service.Summarize(At(13)).Goal);
    }

    [Fact]
    public void Про_угаданный_обед_сообщается_один_раз()
    {
        Lunch();

        var first = _service.TryTakeLunchNotification(At(13, 15));
        var second = _service.TryTakeLunchNotification(At(13, 16));

        Assert.NotNull(first);
        Assert.Equal(At(12, 30), first.Start);
        Assert.Null(second);
    }

    [Fact]
    public void Отмена_возвращает_обед_в_зачёт()
    {
        Lunch();
        Assert.Equal(Hm(3, 30), _service.Summarize(At(13, 15)).Worked);

        Assert.True(_service.CancelGuessedLunch(At(13, 15)));

        var summary = _service.Summarize(At(13, 15));
        Assert.Equal(Hm(4, 15), summary.Worked);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
        Assert.Null(summary.GuessedLunch);
    }

    [Fact]
    public void Отменять_нечего_если_догадки_не_было()
    {
        _service.RecordReturn(At(9), "unlock");

        Assert.False(_service.CancelGuessedLunch(At(13)));
    }

    [Fact]
    public void После_отмены_сообщается_о_следующей_отлучке()
    {
        Lunch();
        Assert.NotNull(_service.TryTakeLunchNotification(At(13, 15)));
        _service.CancelGuessedLunch(At(13, 16));

        _service.RecordPause(At(14), TimeSpan.Zero, "lock");
        _service.RecordReturn(At(14, 40), "unlock");

        var again = _service.TryTakeLunchNotification(At(14, 40));

        Assert.NotNull(again);
        Assert.Equal(At(14), again.Start);
    }

    [Fact]
    public void Отмена_доступна_и_через_час()
    {
        // Пункт меню живёт до конца дня, а не пока висит подсказка.
        Lunch();
        _service.TryTakeLunchNotification(At(13, 15));

        Assert.True(_service.CancelGuessedLunch(At(14, 30)));
        Assert.Equal(TimeSpan.Zero, _service.Summarize(At(14, 30)).Unpaid);
    }

    [Fact]
    public void Пометка_обеда_сбрасывает_флаг_уведомления_о_норме()
    {
        // Отлучка вне обеденного окна: сама обедом не станет, пометим её руками.
        // Норма набрана и о ней сообщено; пометка уводит счётчик обратно под порог.
        _service.RecordReturn(At(9), "unlock");
        _service.RecordPause(At(16), TimeSpan.Zero, "lock");
        _service.RecordReturn(At(16, 40), "unlock");

        Assert.True(_service.TryTakeGoalNotification(At(17, 20)));
        Assert.True(_store.Load(Today).GoalNotified);

        _service.Reclassify(Today, At(16), BreakKind.Unpaid, "history");

        Assert.False(_service.Summarize(At(17, 20)).GoalReached);
        Assert.False(_service.TryTakeGoalNotification(At(17, 20)));
        Assert.False(_store.Load(Today).GoalNotified);
    }

    [Fact]
    public void После_сброса_уведомление_о_норме_приходит_заново()
    {
        _service.RecordReturn(At(9), "unlock");
        _service.RecordPause(At(16), TimeSpan.Zero, "lock");
        _service.RecordReturn(At(16, 40), "unlock");

        Assert.True(_service.TryTakeGoalNotification(At(17, 20)));
        _service.Reclassify(Today, At(16), BreakKind.Unpaid, "history");
        _service.TryTakeGoalNotification(At(17, 20));

        // Досидел недостающие сорок минут — норма снова взята.
        Assert.True(_service.TryTakeGoalNotification(At(18)));
    }

    [Fact]
    public void Обед_помеченный_до_нормы_уведомление_не_ломает()
    {
        // Обратный порядок: сперва классификация, потом достижение нормы.
        _service.RecordReturn(At(9), "unlock");
        _service.RecordPause(At(12, 30), TimeSpan.Zero, "lock");
        _service.RecordReturn(At(13, 15), "unlock");

        Assert.False(_service.TryTakeGoalNotification(At(17)));
        Assert.True(_service.TryTakeGoalNotification(At(17, 45)));
        Assert.False(_service.TryTakeGoalNotification(At(18)));
    }

    [Fact]
    public void Переклассификация_из_истории_переживает_перечитывание()
    {
        Lunch();

        _service.Reclassify(Today, At(12, 30), BreakKind.Paid, "history");

        var reloaded = TestApp.Service(_root);
        Assert.Equal(Hm(4, 15), reloaded.Summarize(At(13, 15)).Worked);
    }

    [Fact]
    public void Повторная_поправка_заменяет_прежнюю()
    {
        Lunch();

        _service.Reclassify(Today, At(12, 30), BreakKind.Paid, "history");
        _service.Reclassify(Today, At(12, 30), BreakKind.Unpaid, "history");

        Assert.Single(_store.Load(Today).Adjustments);
        Assert.Equal(Hm(3, 30), _service.Summarize(At(13, 15)).Worked);
    }

    /// <summary>Приход в девять и отлучка 12:30–13:15 — та самая неоднозначная.</summary>
    private void Lunch()
    {
        _service.RecordReturn(At(9), "unlock");
        _service.RecordPause(At(12, 30), TimeSpan.Zero, "lock");
        _service.RecordReturn(At(13, 15), "unlock");
    }
}