using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Модель учёта — та самая галочка «засчитывать короткие отлучки».
/// </summary>
/// <remarks>
/// Выключенная галочка означает, что счёт останавливает любая блокировка экрана,
/// и настройки обеда при этом не влияют ни на что: режется всё.
/// </remarks>
public class AccountingModelTests
{
    [Fact]
    public void Выключено_короткая_отлучка_не_засчитана()
    {
        var log = Log(In(9), BreakStart(10), BreakEnd(10, 5), Out(18)).By(AllBreaksCut);

        var summary = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(Hm(8, 55), summary.Worked);
        Assert.Equal(Hm(0, 5), summary.Unpaid);
    }

    [Fact]
    public void Выключено_отлучка_в_обед_не_засчитана_и_обедом_не_помечена()
    {
        var log = Log(In(9), BreakStart(13), BreakEnd(13, 40), Out(18)).By(AllBreaksCut);

        var summary = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(Hm(8, 20), summary.Worked);
        Assert.Equal(Hm(0, 40), summary.Unpaid);

        // Догадка не работает вовсе: помечать обедом нечего, когда режется всё.
        Assert.Null(summary.GuessedLunch);
        Assert.All(summary.Intervals, interval => Assert.False(interval.Guessed));
    }

    [Fact]
    public void Включено_короткая_отлучка_засчитана()
    {
        var log = Fresh(In(9), BreakStart(10), BreakEnd(10, 5), Out(18));

        var summary = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Equal(TimeSpan.Zero, summary.Unpaid);
    }

    [Fact]
    public void Выключено_обеденные_настройки_ничего_не_меняют()
    {
        var punches = new[] { In(9), BreakStart(13), BreakEnd(13, 40), Out(18) };

        var usual = Log(punches).By(AllBreaksCut);
        var strange = Log(punches).By(AllBreaksCut with
        {
            Lunch = new LunchRules(new TimeOnly(3, 0), new TimeOnly(4, 0), Hm(0, 1), Hm(0, 2)),
        });

        // Окно сдвинуто на ночь, пороги вывернуты — и ни на чём это не сказывается.
        Assert.Equal(
            WorkTimeCalculator.Compute(usual, At(19)).Worked,
            WorkTimeCalculator.Compute(strange, At(19)).Worked);
    }
}