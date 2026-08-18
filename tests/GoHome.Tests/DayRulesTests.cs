using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Накопленные дни не пересчитываются: у каждого дня свой снимок правил, поставленный
/// при создании. Обновление посреди дня не должно посчитать его половины по-разному.
/// </summary>
/// <remarks>
/// Здесь же проверяется, что снимок покрывает старое поле версии целиком: день по старым
/// правилам — это день, в снимке которого выключен зачёт коротких отлучек. Построчное
/// доказательство на наборе журналов — в <see cref="RulesBaselineTests"/>.
/// </remarks>
public class DayRulesTests
{
    [Fact]
    public void День_без_снимка_считается_по_правилам_действовавшим_до_изменения()
    {
        // Так выглядят все файлы, накопленные до появления снимка: любая блокировка вне зачёта.
        var log = Log(In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(16, 30));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Null(log.Rules);
        Assert.Null(log.RulesVersion);
        Assert.False(summary.CountsShortBreaks);
        Assert.Equal(Hm(7, 45), summary.Worked);
        Assert.Equal(Hm(1, 15), summary.Unpaid);
    }

    [Fact]
    public void Нынешние_настройки_не_переписывают_день_без_снимка()
    {
        var log = Log(In(9), BreakStart(12, 30), BreakEnd(13, 15));

        // Даже если сейчас короткие отлучки засчитываются — прожитый день это не меняет.
        var summary = WorkTimeCalculator.Compute(log, At(18), DayRules.Default);

        Assert.False(summary.CountsShortBreaks);
        Assert.Equal(Hm(8, 15), summary.Worked);
    }

    [Fact]
    public void Старое_поле_версии_читается_как_прежде()
    {
        var breaksUnpaid = Log(In(9), BreakStart(13), BreakEnd(13, 5));
        breaksUnpaid.RulesVersion = RulesVersion.BreaksAreUnpaid;

        var onlyLunch = Log(In(9), BreakStart(13), BreakEnd(13, 5));
        onlyLunch.RulesVersion = RulesVersion.OnlyLunchIsUnpaid;

        // Даже пятиминутная отлучка по старым правилам выпадала из зачёта.
        Assert.Equal(Hm(8, 55), WorkTimeCalculator.Compute(breaksUnpaid, At(18)).Worked);
        Assert.Equal(Hm(9), WorkTimeCalculator.Compute(onlyLunch, At(18)).Worked);
    }

    [Fact]
    public void Неизвестная_будущая_версия_считается_новой()
    {
        var log = Log(In(9), BreakStart(13), BreakEnd(13, 5));
        log.RulesVersion = RulesVersion.OnlyLunchIsUnpaid + 5;

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.True(summary.CountsShortBreaks);
        Assert.Equal(Hm(9), summary.Worked);
    }

    [Fact]
    public void Снимок_сильнее_старого_поля_версии()
    {
        var log = Log(In(9), BreakStart(13), BreakEnd(13, 5)).By(DayRules.Default);
        log.RulesVersion = RulesVersion.BreaksAreUnpaid;

        Assert.True(WorkTimeCalculator.Compute(log, At(18)).CountsShortBreaks);
    }

    [Fact]
    public void Выключенный_зачёт_повторяет_старые_правила_до_минуты()
    {
        var punches = new[] { In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(16, 30) };

        var legacy = Log(punches);
        legacy.RulesVersion = RulesVersion.BreaksAreUnpaid;

        var snapshot = Log(punches).By(AllBreaksCut);

        var byVersion = WorkTimeCalculator.Compute(legacy, At(18));
        var bySnapshot = WorkTimeCalculator.Compute(snapshot, At(18));

        Assert.Equal(byVersion.Worked, bySnapshot.Worked);
        Assert.Equal(byVersion.Unpaid, bySnapshot.Unpaid);
        Assert.Equal(byVersion.Breaks, bySnapshot.Breaks);
        Assert.Equal(
            byVersion.Intervals.Select(i => (i.Start, i.End, i.Kind, i.Guessed)),
            bySnapshot.Intervals.Select(i => (i.Start, i.End, i.Kind, i.Guessed)));
    }

    [Fact]
    public void Тот_же_день_с_включённым_зачётом_считается_иначе()
    {
        var punches = new[] { In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(16, 30) };

        var cut = WorkTimeCalculator.Compute(Log(punches).By(AllBreaksCut), At(18));
        var counted = WorkTimeCalculator.Compute(Fresh(punches), At(18));

        Assert.Equal(Hm(7, 45), cut.Worked);
        Assert.Equal(Hm(8, 15), counted.Worked);
    }

    [Fact]
    public void При_выключенном_зачёте_догадка_не_работает()
    {
        var log = Log(In(9), BreakStart(12, 30), BreakEnd(13, 15)).By(AllBreaksCut);

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Null(summary.GuessedLunch);
        Assert.True(summary.Intervals.Single().IsUnpaid);
    }

    [Fact]
    public void Поправка_человека_сильнее_выключенного_зачёта()
    {
        // Осознанная правка уже посчитанного дня — единственное, что его меняет.
        var log = Log(In(9), BreakStart(12, 30), BreakEnd(13, 15)).With(Paid(12, 30)).By(AllBreaksCut);

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
    }

    [Fact]
    public void Разные_правила_в_истории_видны()
    {
        var summaries = HistoryCalculator.Recent(
            [Log(Today.AddDays(-1), In(9), Out(17)), Fresh(In(9), Out(18))],
            At(18),
            7,
            Even());

        Assert.True(HistoryCalculator.HasMixedRules(summaries));
        Assert.Contains(summaries, s => s.CountsShortBreaks);
        Assert.Contains(summaries, s => !s.CountsShortBreaks);
    }

    [Fact]
    public void Однородная_история_смешанной_не_считается()
    {
        var summaries = HistoryCalculator.Recent([Fresh(In(9), Out(17))], At(18), 7, Even());

        Assert.False(HistoryCalculator.HasMixedRules(summaries));
    }
}