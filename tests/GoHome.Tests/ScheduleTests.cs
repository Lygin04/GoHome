using GoHome.App;
using GoHome.Core;
using GoHome.Storage;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Недельный график, исключения по датам, нерабочие дни и недельный баланс.
/// </summary>
/// <remarks>
/// Все даты здесь рабочие, со сдвигом на <see cref="WorkDay.StartHour"/>.
/// <see cref="TestClock.Today"/> — четверг 6 августа 2026 года.
/// </remarks>
public sealed class ScheduleTests : IDisposable
{
    /// <summary>Воскресенье перед <see cref="TestClock.Today"/>.</summary>
    private static readonly DateOnly Sunday = new(2026, 8, 2);

    /// <summary>Понедельник недели, в которой лежит <see cref="TestClock.Today"/>.</summary>
    private static readonly DateOnly Monday = new(2026, 8, 3);

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

    // ---- нерабочий день -------------------------------------------------------------

    [Fact]
    public void В_нерабочий_день_время_считается_но_нормы_нет()
    {
        var log = Fresh(In(9), Out(12)).By(Goal(null));

        var summary = WorkTimeCalculator.Compute(log, At(13));

        Assert.True(summary.IsDayOff);
        Assert.Equal(Hm(3), summary.Worked);

        // Нулевая цель формально достигнута в момент запуска — а нерабочий день не достигается.
        Assert.False(summary.GoalReached);

        // И на ноль ничего не делится: кольцо показывает нейтральное состояние.
        Assert.Equal(0d, summary.Progress);
        Assert.False(double.IsNaN(summary.Progress));
        Assert.Equal(TimeSpan.Zero, summary.Remaining);
        Assert.Null(summary.ProjectedEnd);
    }

    [Fact]
    public void Нерабочий_день_не_то_же_самое_что_ноль_часов()
    {
        var punches = new[] { In(9), Out(12) };

        var dayOff = WorkTimeCalculator.Compute(Fresh(punches).By(Goal(null)), At(13));
        var zero = WorkTimeCalculator.Compute(Fresh(punches).By(Goal(TimeSpan.Zero)), At(13));

        Assert.False(dayOff.GoalReached);
        Assert.True(zero.GoalReached);
    }

    [Fact]
    public void В_нерабочий_день_уведомление_о_норме_не_приходит()
    {
        var service = TestApp.Service(_root, AppSettings.Default with { Schedule = Flat(null) });

        service.RecordReturn(At(9), "unlock");

        Assert.False(service.TryTakeGoalNotification(At(19)));
        Assert.Equal(Hm(10), service.Summarize(At(19)).Worked);
    }

    [Fact]
    public void Отработанное_в_нерабочий_день_идёт_в_баланс_со_знаком_плюс()
    {
        var settings = AppSettings.Default;   // суббота и воскресенье нерабочие
        var saturday = new DateOnly(2026, 8, 8);
        var afternoon = At(14, 0, 2);         // суббота

        var idle = HistoryCalculator.Week([], afternoon, settings);
        var worked = HistoryCalculator.Week(
            [Log(saturday, In(10), Out(13)).By(Goal(null))],
            afternoon,
            settings);

        // У самого дня баланс — это всё отработанное: цели, из которой вычитать, нет.
        Assert.Equal(Hm(3), worked.Days.Single(day => day.Date == saturday).Balance);

        // Нерабочий день ничего не добавляет к норме, поэтому в недельный баланс
        // отработанное в него попадает целиком и со знаком плюс.
        Assert.Equal(idle.Norm, worked.Norm);
        Assert.Equal(idle.Balance + Hm(3), worked.Balance);
    }

    // ---- исключения по датам --------------------------------------------------------

    [Fact]
    public void Исключение_перекрывает_недельный_график()
    {
        var settings = AppSettings.Default with
        {
            Schedule = Flat(Hm(8)),
            Exceptions = [new DateException { Date = Today, Hours = Hm(7), Note = "предпраздничный" }],
        };

        Assert.Equal(Hm(7), settings.GoalFor(Today));
        Assert.Equal(Hm(8), settings.GoalFor(Today.AddDays(1)));
    }

    [Fact]
    public void Исключение_делает_дату_нерабочей()
    {
        var settings = AppSettings.Default with
        {
            Schedule = Flat(Hm(8)),
            Exceptions = [new DateException { Date = Today, Hours = null, Note = "отпуск" }],
        };

        var rules = settings.RulesFor(Today);
        Assert.True(rules.IsDayOff);

        var summary = WorkTimeCalculator.Compute(Log(Today, In(9), Out(12)), At(13), rules);
        Assert.True(summary.IsDayOff);
        Assert.False(summary.GoalReached);
        Assert.Equal(Hm(3), summary.Worked);
    }

    [Fact]
    public void Нерабочий_день_переживает_запись_в_файл_дня()
    {
        var store = new DayLogStore(_root);
        store.Save(Log(Today, In(9), Out(12)).By(Goal(null)));

        // Пустая цель обязана дойти до файла явно: без неё снимок читался бы обратно
        // как обычный восьмичасовой день, и нерабочий день молча стал бы рабочим.
        Assert.Contains("\"goal\": null", File.ReadAllText(store.PathFor(Today)), StringComparison.Ordinal);

        var reloaded = new DayLogStore(_root).Load(Today);
        Assert.True(reloaded.Rules?.IsDayOff);
        Assert.True(WorkTimeCalculator.Compute(reloaded, At(13)).IsDayOff);
    }

    [Fact]
    public void Пометка_даты_обновляет_снимок_уже_созданного_дня()
    {
        var service = TestApp.Service(_root, Even());
        service.RecordReturn(At(9), "unlock");
        Assert.Equal(Hm(8), service.Summarize(At(13)).Goal);

        service.SaveSettings(
            service.Settings.WithException(Today, new DateException { Date = Today, Hours = null }),
            At(13));

        var summary = service.Summarize(At(13));
        Assert.True(summary.IsDayOff);
        Assert.False(summary.GoalReached);
    }

    // ---- график ---------------------------------------------------------------------

    [Fact]
    public void Изменение_графика_в_среду_не_меняет_понедельник_и_вторник()
    {
        var store = new DayLogStore(_root);
        var service = new GoHomeService(store, TestApp.Settings(_root, Even()));

        // Понедельник и вторник прожиты по восьмичасовой цели.
        foreach (var offset in new[] { 0, 1 })
        {
            var date = Monday.AddDays(offset);
            store.Save(new DayLog
            {
                Date = date,
                Punches = [new Punch(new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), Offset), PunchKind.In)],
                Rules = Goal(Hm(8)),
            });
        }

        // В среду график сменился на шестичасовой.
        var wednesday = Monday.AddDays(2);
        service.SaveSettings(service.Settings with { Schedule = Flat(Hm(6)) }, At(12, 0, -1));

        var week = service.Week(At(12, 0, -1));
        Assert.Equal(Hm(8), week.Days.Single(day => day.Date == Monday).Goal);
        Assert.Equal(Hm(8), week.Days.Single(day => day.Date == Monday.AddDays(1)).Goal);
        Assert.Equal(Hm(6), week.Days.Single(day => day.Date == wednesday).Goal);
    }

    // ---- недельный баланс -----------------------------------------------------------

    [Fact]
    public void Норма_недели_это_сумма_целей_её_дней()
    {
        var settings = AppSettings.Default;   // пятидневка по восемь часов

        var week = HistoryCalculator.Week([], At(13), settings);

        // Сумма, а не произведение и не «восемь на семь».
        Assert.Equal(Hm(40), week.Norm);
        Assert.Equal(5, week.WorkingDays);
        Assert.Equal(7, week.Days.Count);
    }

    [Fact]
    public void Норма_недели_учитывает_исключения()
    {
        var settings = AppSettings.Default with
        {
            Exceptions =
            [
                new DateException { Date = Monday, Hours = null },              // отгул
                new DateException { Date = Monday.AddDays(4), Hours = Hm(7) },  // предпраздничная пятница
            ],
        };

        Assert.Equal(Hm(31), HistoryCalculator.Week([], At(13), settings).Norm);
    }

    [Fact]
    public void Недельный_баланс_считается_по_прожитым_дням()
    {
        var settings = AppSettings.Default;

        // Понедельник и вторник по девять часов, сегодня четверг и день ещё идёт.
        DayLog[] logs =
        [
            Log(Monday, In(9), Out(18)).By(Goal(Hm(8))),
            Log(Monday.AddDays(1), In(9), Out(18)).By(Goal(Hm(8))),
            Log(Monday.AddDays(2), In(9), Out(17)).By(Goal(Hm(8))),
        ];

        var week = HistoryCalculator.Week(logs, At(13), settings);

        Assert.Equal(Hm(26), week.Worked);
        Assert.Equal(Hm(40), week.Norm);

        // Прожито четыре дня из пяти рабочих: норма к этому моменту — 32 часа.
        Assert.Equal(Hm(32), week.NormSoFar);
        Assert.Equal(-Hm(6), week.Balance);
    }

    [Fact]
    public void Недельный_баланс_не_меняет_дневные_цели()
    {
        var service = TestApp.Service(_root, Even());

        service.RecordReturn(At(9), "unlock");
        var before = service.Summarize(At(13)).Goal;

        _ = service.Week(At(13));

        Assert.Equal(before, service.Summarize(At(13)).Goal);
        Assert.Equal(Hm(8), service.Summarize(At(13)).Goal);
    }

    // ---- границы недели -------------------------------------------------------------

    [Fact]
    public void Ночь_с_воскресенья_на_понедельник_относится_к_воскресенью()
    {
        var lateSunday = new DateTimeOffset(Sunday.ToDateTime(new TimeOnly(22, 0)), Offset);
        var earlyMonday = new DateTimeOffset(Monday.ToDateTime(new TimeOnly(1, 0)), Offset);

        // Рабочий день сдвинут на четыре утра, поэтому оба момента — воскресенье.
        Assert.Equal(Sunday, WorkDay.DateOf(lateSunday));
        Assert.Equal(Sunday, WorkDay.DateOf(earlyMonday));

        // А воскресенье закрывает предыдущую неделю, а не открывает эту.
        Assert.Equal(Monday.AddDays(-7), HistoryCalculator.WeekStart(Sunday));
        Assert.Equal(Monday, HistoryCalculator.WeekStart(Today));
        Assert.NotEqual(HistoryCalculator.WeekStart(Sunday), HistoryCalculator.WeekStart(Monday));
    }

    [Fact]
    public void Работа_в_ночь_на_понедельник_попадает_в_прошлую_неделю()
    {
        var log = Log(
            Sunday,
            new Punch(new DateTimeOffset(Sunday.ToDateTime(new TimeOnly(22, 0)), Offset), PunchKind.In),
            new Punch(new DateTimeOffset(Monday.ToDateTime(new TimeOnly(1, 0)), Offset), PunchKind.Out));

        var week = HistoryCalculator.Week(
            [log],
            new DateTimeOffset(Sunday.ToDateTime(new TimeOnly(23, 0)), Offset),
            AppSettings.Default);

        Assert.Equal(Monday.AddDays(-7), week.Start);
        Assert.Equal(Sunday, week.End);
        Assert.Equal(Hm(3), week.Worked);
    }

    // ---- уведомление о норме --------------------------------------------------------

    [Fact]
    public void Повышение_цели_сбрасывает_признак_уведомления()
    {
        var service = TestApp.Service(_root, Even());
        service.RecordReturn(At(9), "unlock");

        // Восемь часов отработаны, уведомление пришло.
        Assert.True(service.TryTakeGoalNotification(At(17)));
        Assert.False(service.TryTakeGoalNotification(At(17, 30)));

        // Цель подняли до десяти часов — норма снова недостижима.
        service.SaveSettings(service.Settings with { Schedule = Flat(Hm(10)) }, At(18));
        Assert.False(service.TryTakeGoalNotification(At(18)));

        // И приходит заново, когда достигнута уже новая.
        Assert.True(service.TryTakeGoalNotification(At(19)));
    }

    [Fact]
    public void Понижение_цели_не_повторяет_уведомление()
    {
        var service = TestApp.Service(_root, Even());
        service.RecordReturn(At(9), "unlock");

        Assert.True(service.TryTakeGoalNotification(At(17)));

        service.SaveSettings(service.Settings with { Schedule = Flat(Hm(6)) }, At(17, 30));

        Assert.False(service.TryTakeGoalNotification(At(17, 30)));
    }
}