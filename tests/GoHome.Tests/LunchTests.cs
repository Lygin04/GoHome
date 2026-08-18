using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Новое правило учёта: в зачёт не идёт только обед, всё остальное — чай, переговорка,
/// разговор в коридоре — рабочее время. Обед определяется по времени и длительности.
/// </summary>
public class LunchTests
{
    [Fact]
    public void Короткая_отлучка_не_останавливает_счётчик()
    {
        var log = Fresh(In(9), BreakStart(13), BreakEnd(13, 5));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
        Assert.Null(summary.GuessedLunch);
    }

    [Fact]
    public void Короткая_отлучка_не_засоряет_историю()
    {
        // Пять минут — не отлучка, а вопрос коллеге. В списке перерывов её быть не должно.
        var log = Fresh(In(9), BreakStart(13), BreakEnd(13, 5));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Empty(summary.Intervals);
    }

    [Fact]
    public void Отлучка_короче_минимума_догадки_обедом_не_становится()
    {
        // Пятнадцать минут в час дня — не обед, и уведомлять о них не о чем.
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(12, 45));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Null(summary.GuessedLunch);
        Assert.Single(summary.Intervals);
        Assert.False(summary.Intervals[0].IsUnpaid);
    }

    [Fact]
    public void Длинная_отлучка_в_обеденном_окне_помечается_обедом()
    {
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(13, 15));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(8, 15), summary.Worked);
        Assert.Equal(Hm(0, 45), summary.Unpaid);
        Assert.NotNull(summary.GuessedLunch);
        Assert.Equal(At(12, 30), summary.GuessedLunch.Start);
    }

    [Fact]
    public void Длинная_отлучка_вне_обеденного_окна_идёт_в_зачёт()
    {
        var log = Fresh(In(9), BreakStart(10), BreakEnd(10, 45));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
        Assert.Null(summary.GuessedLunch);
    }

    [Fact]
    public void Из_двух_отлучек_в_окне_обедом_становится_первая()
    {
        var log = Fresh(In(9), BreakStart(12), BreakEnd(12, 40), BreakStart(14), BreakEnd(14, 40));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(0, 40), summary.Unpaid);
        Assert.Equal(At(12), summary.GuessedLunch?.Start);
        Assert.False(summary.Intervals.Single(i => i.Start == At(14)).IsUnpaid);
    }

    [Fact]
    public void После_отмены_обедом_становится_следующая_отлучка()
    {
        // Встреча в 12:00 не должна съедать единственную попытку: настоящий обед в 14:00
        // обязан снова стать кандидатом.
        var log = Fresh(In(9), BreakStart(12), BreakEnd(12, 40), BreakStart(14), BreakEnd(14, 40))
            .With(Paid(12));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(At(14), summary.GuessedLunch?.Start);
        Assert.False(summary.Intervals.Single(i => i.Start == At(12)).IsUnpaid);
        Assert.Equal(Hm(0, 40), summary.Unpaid);
    }

    [Fact]
    public void Отменённая_отлучка_обедом_повторно_не_становится()
    {
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(13, 15)).With(Paid(12, 30));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Null(summary.GuessedLunch);
        Assert.Equal(Hm(9), summary.Worked);
    }

    [Fact]
    public void После_обеда_длинная_отлучка_идёт_в_зачёт()
    {
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(17));

        var summary = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(Hm(0, 45), summary.Unpaid);
        Assert.Equal(Hm(9, 15), summary.Worked);
        Assert.False(summary.Intervals.Single(i => i.Start == At(16)).IsUnpaid);
    }

    [Fact]
    public void День_без_длинных_отлучек_идёт_в_зачёт_целиком()
    {
        var log = Fresh(In(9), Out(18));

        var summary = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
        Assert.Null(summary.GuessedLunch);
    }

    [Fact]
    public void Последняя_блокировка_дня_в_обеденном_окне_это_уход()
    {
        // Заблокировал в обеденное окно и не вернулся — это уход домой, а не обед.
        var log = Fresh(In(9), BreakStart(13));

        var summary = WorkTimeCalculator.Compute(log, At(10, dayShift: 1));

        Assert.Equal(WorkState.Finished, summary.State);
        Assert.Equal(At(13), summary.LeftAt);
        Assert.Equal(Hm(4), summary.Worked);
        Assert.Null(summary.GuessedLunch);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
    }

    [Fact]
    public void Догадка_срабатывает_на_возвращении_а_не_на_уходе()
    {
        var log = Fresh(In(9), BreakStart(12, 30));

        // Человек ещё не вернулся: чем окажется отлучка, пока неизвестно.
        var away = WorkTimeCalculator.Compute(log, At(13));
        Assert.Null(away.GuessedLunch);
        Assert.Equal(WorkState.OnBreak, away.State);
        Assert.Equal(Hm(3, 30), away.Worked);

        log.Punches.Add(BreakEnd(13, 15));
        var back = WorkTimeCalculator.Compute(log, At(13, 15));

        Assert.NotNull(back.GuessedLunch);
        Assert.Equal(Hm(3, 30), back.Worked);
    }

    [Fact]
    public void Поправка_в_никуда_игнорируется()
    {
        // Файл дня правят руками: поправка вполне может перестать указывать на интервал.
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(13, 15))
            .With(Paid(11), Unpaid(17, 42));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(At(12, 30), summary.GuessedLunch?.Start);
        Assert.Equal(Hm(8, 15), summary.Worked);
    }

    [Fact]
    public void Ручная_пометка_не_ограничена_одним_разом_в_день()
    {
        // Бывает и обед, и поликлиника. Ограничение «раз в день» касается только догадки.
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(17))
            .With(Unpaid(12, 30), Unpaid(16));

        var summary = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(Hm(1, 45), summary.Unpaid);
        Assert.Equal(Hm(8, 15), summary.Worked);
        Assert.All(summary.Intervals, interval => Assert.True(interval.IsUnpaid));
    }

    [Fact]
    public void Ручная_пометка_отменяет_догадку()
    {
        // Человек сам назначил обедом другую отлучку — угадывать больше нечего.
        var log = Fresh(In(9), BreakStart(12), BreakEnd(12, 40), BreakStart(14), BreakEnd(14, 40))
            .With(Unpaid(14));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Null(summary.GuessedLunch);
        Assert.Equal(At(14), summary.UnpaidIntervals.Single().Start);
    }

    [Fact]
    public void Обеденное_окно_берётся_из_правил()
    {
        var rules = LunchRules.Default with { WindowStart = new TimeOnly(9, 30), WindowEnd = new TimeOnly(11, 0) };
        var log = Fresh(In(9), BreakStart(10), BreakEnd(10, 45)).By(DayRules.Default with { Lunch = rules });

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(At(10), summary.GuessedLunch?.Start);
    }

    [Fact]
    public void Догадка_устойчива_к_повторному_расчёту()
    {
        // Окно истории пересчитывает день на каждой активации. Догадка обязана оставаться
        // той же самой и не требовать записи в файл, чтобы пережить перезапуск.
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(16, 45));

        var first = WorkTimeCalculator.Compute(log, At(18));
        var second = WorkTimeCalculator.Compute(log, At(18));
        var later = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(first.GuessedLunch?.Start, second.GuessedLunch?.Start);
        Assert.Equal(first.GuessedLunch?.Start, later.GuessedLunch?.Start);
        Assert.Equal(first.Unpaid, later.Unpaid);
        Assert.Empty(log.Adjustments ?? []);
    }

    [Fact]
    public void Перерывы_считаются_отдельно_от_незачтённого()
    {
        var log = Fresh(In(9), BreakStart(12, 30), BreakEnd(13, 15), BreakStart(16), BreakEnd(16, 30));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(1, 15), summary.Breaks);
        Assert.Equal(Hm(0, 45), summary.Unpaid);
        Assert.Equal(Hm(8, 15), summary.Worked);
    }
}