using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Статистика за период. Считается из тех же журналов, что и история, поэтому проверяется
/// на них же: важны не столько числа, сколько то, что период не разваливается на краях —
/// пустой, с нерабочими днями, с испорченным файлом.
/// </summary>
public sealed class StatsTests : IDisposable
{
    /// <summary>Понедельник недели, которой принадлежит <see cref="TestClock.Today"/>.</summary>
    private static readonly DateOnly Monday = HistoryCalculator.WeekStart(Today);

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
    public void Пустой_период_считается_и_средних_не_придумывает()
    {
        var stats = Week([]);

        Assert.Equal(7, stats.Days.Count);
        Assert.True(stats.IsEmpty);
        Assert.Equal(TimeSpan.Zero, stats.Worked);
        Assert.Null(stats.Arrival);
        Assert.Null(stats.Departure);
        Assert.Null(stats.DayLength);
        Assert.Null(stats.Unpaid);
    }

    [Fact]
    public void Испорченный_файл_пропускается_а_остальные_дни_считаются()
    {
        var stats = Week([
            Day(0, In(9), Out(17)),
            new DayLog { Date = Monday.AddDays(1), IsUnreadable = true },
            Day(2, In(9), Out(15)),
        ]);

        Assert.Equal(1, stats.Unreadable);
        Assert.True(stats.HasGaps);
        Assert.Equal(Hm(14), stats.Worked);
        Assert.Equal(2, stats.WorkedDays);
    }

    [Fact]
    public void Норма_периода_это_сумма_целей_дней_а_не_произведение()
    {
        // Вторник сокращённый, у среды свой снимок на шесть часов: ни то, ни другое
        // умножением числа дней на цель не получается.
        var settings = Even(Hm(8)).WithException(
            Monday.AddDays(1),
            new DateException { Date = Monday.AddDays(1), Hours = Hm(4) });

        var stats = StatsCalculator.For(
            [Day(2, In(9), Out(15)).By(Goal(Hm(6)))],
            At(20),
            settings,
            PeriodRange.Of(StatsPeriod.Week, Today));

        Assert.Equal(Hm(8 + 4 + 6 + 8 + 8 + 8 + 8), stats.Norm);
    }

    [Fact]
    public void Нерабочий_день_в_норму_не_входит_а_отработанное_в_нём_входит_в_баланс()
    {
        // Понедельник помечен нерабочим в самом файле дня, и три часа в нём отработаны.
        var stats = StatsCalculator.For(
            [Day(0, In(10), Out(13)).By(Goal(null))],
            At(20),
            Even(Hm(8)),
            PeriodRange.Of(StatsPeriod.Week, Today));

        Assert.Equal(Hm(6 * 8), stats.Norm);
        Assert.Equal(Hm(3), stats.Worked);
        Assert.Equal(1, stats.DaysOff);

        // Прожиты понедельник–четверг, из них рабочих три: норма к этому моменту — 24 часа.
        Assert.Equal(Hm(3) - Hm(3 * 8), stats.Balance);
    }

    [Fact]
    public void Средние_считаются_по_дням_с_данными()
    {
        var stats = Week([
            Day(0, In(9), Out(17)),
            Day(1, In(10), Out(18)),
        ]);

        Assert.Equal(Hm(9, 30), stats.Arrival);
        Assert.Equal(Hm(17, 30), stats.Departure);
        Assert.Equal(Hm(8), stats.DayLength);
    }

    [Fact]
    public void Обед_попадает_в_среднее_только_из_дней_с_обедом()
    {
        var stats = Week([
            Day(0, In(9), BreakStart(13), BreakEnd(14), Out(18)),
            Day(1, In(9), Out(17)),
        ]);

        Assert.Equal(Hm(1), stats.Unpaid);
    }

    [Fact]
    public void Ушедший_за_полночь_день_не_утаскивает_средний_уход_на_утро()
    {
        var stats = Week([Day(0, In(16), Out(1, dayShift: 1))]);

        Assert.Equal(Hm(1), stats.Departure);
    }

    [Fact]
    public void Границы_периодов_считаются_по_календарю()
    {
        var week = PeriodRange.Of(StatsPeriod.Week, Today);
        Assert.Equal(Monday, week.Start);
        Assert.Equal(Monday.AddDays(6), week.End);
        Assert.Equal(7, week.Length);

        var month = PeriodRange.Of(StatsPeriod.Month, Today);
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1), month.Start);
        Assert.Equal(DateTime.DaysInMonth(Today.Year, Today.Month), month.Length);

        var year = PeriodRange.Of(StatsPeriod.Year, Today);
        Assert.Equal(new DateOnly(Today.Year, 1, 1), year.Start);
        Assert.Equal(new DateOnly(Today.Year, 12, 31), year.End);
    }

    [Fact]
    public void Соседний_период_отсчитывается_от_начала_а_не_прибавлением_длины()
    {
        // Тридцать первое января плюс тридцать дней — это март, а нужен февраль.
        var january = PeriodRange.Of(StatsPeriod.Month, new DateOnly(2026, 1, 31));

        var february = january.Shift(1);

        Assert.Equal(new DateOnly(2026, 2, 1), february.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), february.End);
        Assert.Equal(january, february.Shift(-1));
    }

    [Fact]
    public void Название_периода_называет_период()
    {
        Assert.Contains("2026", PeriodRange.Of(StatsPeriod.Year, Today).Title, StringComparison.Ordinal);
        Assert.Contains("2026", PeriodRange.Of(StatsPeriod.Month, Today).Title, StringComparison.Ordinal);
        Assert.Contains("2026", PeriodRange.Of(StatsPeriod.Week, Today).Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Нечитаемый_файл_на_диске_статистику_не_роняет()
    {
        var service = TestApp.Service(_root, Even(Hm(8)));
        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(17), TimeSpan.Zero, "lock");

        // Так выглядит файл, который человек правил руками и ошибся в JSON.
        File.WriteAllText(Path.Combine(_root, $"{Monday:yyyy-MM-dd}.json"), "{ это не json");

        var stats = service.Stats(PeriodRange.Of(StatsPeriod.Week, Today), At(20));

        Assert.Equal(1, stats.Unreadable);
        Assert.Equal(Hm(8), stats.Worked);
    }

    [Fact]
    public void Выгрузка_читается_экселем_с_русскими_настройками()
    {
        var csv = CsvExport.Build(Week([Day(0, In(9), Out(16, 45))]));
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Дата;День недели;", lines[0], StringComparison.Ordinal);

        // Часы — дробью через запятую: с точкой Excel показал бы текст вместо числа.
        Assert.Contains(";7:45;7,75;", lines[1], StringComparison.Ordinal);
        Assert.Contains("понедельник", lines[1], StringComparison.Ordinal);
        Assert.Equal(8, lines.Length);
    }

    [Fact]
    public void В_выгрузке_нерабочий_день_назван_нерабочим()
    {
        var csv = CsvExport.Build(StatsCalculator.For(
            [Day(0, In(10), Out(13)).By(Goal(null))],
            At(20),
            Even(Hm(8)),
            PeriodRange.Of(StatsPeriod.Week, Today)));

        Assert.Contains("нерабочий", csv, StringComparison.Ordinal);
    }

    /// <summary>Неделя <see cref="TestClock.Today"/> по ровному восьмичасовому графику.</summary>
    private static PeriodStats Week(IEnumerable<DayLog> logs) =>
        StatsCalculator.For(logs, At(20), Even(Hm(8)), PeriodRange.Of(StatsPeriod.Week, Today));

    /// <summary>День недели по номеру от понедельника, со снимком нынешних правил.</summary>
    private static DayLog Day(int index, params Punch[] punches)
    {
        var date = Monday.AddDays(index);
        var shift = date.DayNumber - Today.DayNumber;

        var moved = punches
            .Select(punch => punch with { At = punch.At.AddDays(shift) })
            .ToArray();

        return Log(date, moved).By(DayRules.Default);
    }
}
