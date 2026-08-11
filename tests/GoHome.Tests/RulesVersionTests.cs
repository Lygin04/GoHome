using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Накопленные дни не пересчитываются: у каждого дня своя версия правил, проставленная
/// при создании. Обновление посреди дня не должно посчитать его половины по-разному.
/// </summary>
public class RulesVersionTests
{
    [Fact]
    public void День_без_поля_версии_считается_по_старым_правилам()
    {
        // Так выглядят все файлы, накопленные до смены правил: любая блокировка вне зачёта.
        var log = Log(In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(16, 30));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Null(log.RulesVersion);
        Assert.Equal(RulesVersion.BreaksAreUnpaid, summary.RulesVersion);
        Assert.False(summary.OnlyLunchUnpaid);
        Assert.Equal(Hm(7, 45), summary.Worked);
        Assert.Equal(Hm(1, 15), summary.Unpaid);
    }

    [Fact]
    public void День_со_старой_версией_считается_по_старым_правилам()
    {
        var log = Log(In(9), BreakStart(13), BreakEnd(13, 5));
        log.RulesVersion = RulesVersion.BreaksAreUnpaid;

        var summary = WorkTimeCalculator.Compute(log, At(18));

        // Даже пятиминутная отлучка по старым правилам выпадала из зачёта.
        Assert.Equal(Hm(8, 55), summary.Worked);
        Assert.Equal(Hm(0, 5), summary.Unpaid);
    }

    [Fact]
    public void Тот_же_день_по_новым_правилам_считается_иначе()
    {
        var punches = new[] { In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(16, 30) };

        var old = WorkTimeCalculator.Compute(Log(punches), At(18));
        var fresh = WorkTimeCalculator.Compute(Fresh(punches), At(18));

        Assert.Equal(Hm(7, 45), old.Worked);
        Assert.Equal(Hm(8, 15), fresh.Worked);
        Assert.True(fresh.Worked > old.Worked);
    }

    [Fact]
    public void По_старым_правилам_догадка_не_работает()
    {
        var log = Log(In(9), BreakStart(12, 30), BreakEnd(13, 15));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Null(summary.GuessedLunch);
        Assert.True(summary.Intervals.Single().IsUnpaid);
    }

    [Fact]
    public void Поправка_человека_сильнее_старых_правил()
    {
        // Осознанная правка уже посчитанного дня — единственное, что его меняет.
        var log = Log(In(9), BreakStart(12, 30), BreakEnd(13, 15)).With(Paid(12, 30));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
    }

    [Fact]
    public void Неизвестная_будущая_версия_считается_новой()
    {
        var log = Fresh(In(9), BreakStart(13), BreakEnd(13, 5));
        log.RulesVersion = RulesVersion.Current + 5;

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.True(summary.OnlyLunchUnpaid);
        Assert.Equal(Hm(9), summary.Worked);
    }

    [Fact]
    public void Разные_версии_в_истории_видны()
    {
        var now = At(18);
        var summaries = HistoryCalculator.Recent(
            [Log(Today.AddDays(-1), In(9), Out(17)), Fresh(In(9), Out(18))],
            now,
            7,
            WorkTimeCalculator.DefaultGoal);

        Assert.True(HistoryCalculator.HasMixedRules(summaries));
        Assert.Contains(summaries, s => s.OnlyLunchUnpaid);
        Assert.Contains(summaries, s => !s.OnlyLunchUnpaid);
    }

    [Fact]
    public void Однородная_история_смешанной_не_считается()
    {
        var summaries = HistoryCalculator.Recent(
            [Fresh(In(9), Out(17))],
            At(18),
            7,
            WorkTimeCalculator.DefaultGoal);

        Assert.False(HistoryCalculator.HasMixedRules(summaries));
    }
}