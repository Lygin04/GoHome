using GoHome.Core;
using GoHome.Ui.Design;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Полоса дня — то, во что превращается сводка перед отрисовкой.
/// </summary>
/// <remarks>
/// Здесь вся сложность формы дня: границы диапазона, разрезание отлучками, метки часов
/// и переход через полночь. На картинке это не проверить, на числах — можно.
/// </remarks>
public sealed class DayBandTests
{
    /// <summary>День без прихода полосу не ломает: она просто пустая.</summary>
    [Fact]
    public void DayWithoutArrivalIsEmptyRatherThanBroken()
    {
        var band = DayBand.For(Day(), At(9), tickStep: 2);

        Assert.True(band.IsEmpty);
        Assert.Null(band.Now);
        Assert.Null(band.Goal);

        // Полоса растянута на рабочие сутки целиком: показывать нечего, и сужать не к чему.
        Assert.Equal(WorkDay.StartOf(Today, Offset), band.From);
        Assert.Equal(WorkDay.EndOf(Today, Offset), band.To);

        // Деления на ноль нет и у пустой полосы: доля просто зажимается в свои края.
        Assert.InRange(band.Fraction(At(9)), 0d, 1d);
        Assert.NotEmpty(band.Ticks);
    }

    /// <summary>Работа между отлучками режется на отрезки без дыр.</summary>
    [Fact]
    public void WorkIsCutByBreaksWithoutGaps()
    {
        var day = Day(
            arrived: At(9),
            left: At(18),
            intervals: [Away(At(11), At(11, 15)), Lunch(At(13), At(13, 45))]);

        var band = DayBand.For(day, At(20), tickStep: 2);

        Assert.Equal(
            [BandKind.Work, BandKind.PaidBreak, BandKind.Work, BandKind.UnpaidBreak, BandKind.Work],
            band.Segments.Select(segment => segment.Kind));

        // Соседние отрезки смыкаются: конец одного — начало следующего.
        foreach (var (left, right) in band.Segments.Zip(band.Segments.Skip(1)))
        {
            Assert.Equal(left.End, right.Start);
        }

        Assert.Equal(At(9), band.Segments[0].Start);
        Assert.Equal(At(18), band.Segments[^1].End);
    }

    /// <summary>
    /// Отметка «сейчас» и прогноз ухода есть только у сегодняшнего дня: у закрытого время
    /// больше не идёт, и рисовать там нечего.
    /// </summary>
    [Fact]
    public void PastDayHasNeitherNowNorProjection()
    {
        var yesterday = Today.AddDays(-1);
        var day = Day(
            date: yesterday,
            arrived: At(9, 0, -1),
            left: At(18, 0, -1),
            projected: At(17, 0, -1));

        var band = DayBand.For(day, At(12), tickStep: 2);

        Assert.Null(band.Now);
        Assert.Null(band.Goal);
    }

    /// <summary>У сегодняшнего незакрытого дня они есть, и оба внутри диапазона.</summary>
    [Fact]
    public void TodayCarriesNowAndProjection()
    {
        var day = Day(arrived: At(9), projected: At(17, 30));
        var band = DayBand.For(day, At(15), tickStep: 2);

        Assert.Equal(At(15), band.Now);
        Assert.Equal(At(17, 30), band.Goal);
        Assert.InRange(band.Fraction(At(15)), 0d, 1d);
    }

    /// <summary>У нерабочего дня цели нет — значит, нет и её отметки.</summary>
    [Fact]
    public void DayOffHasNoGoalMark()
    {
        var day = Day(arrived: At(9), projected: At(17), dayOff: true);
        var band = DayBand.For(day, At(12), tickStep: 2);

        Assert.Null(band.Goal);
        Assert.NotNull(band.Now);
    }

    /// <summary>
    /// Рабочие сутки сдвинуты, и вечер с переходом за полночь — обычный непрерывный
    /// отрезок, а не отрезок, идущий назад.
    /// </summary>
    [Fact]
    public void DayCrossingMidnightRunsForward()
    {
        var day = Day(
            arrived: At(21),
            left: At(1, 0, 1),
            intervals: [Away(At(23), At(23, 20))]);

        var band = DayBand.For(day, At(2, 0, 1), tickStep: 2);

        Assert.True(band.To > band.From);
        Assert.All(band.Segments, segment => Assert.True(segment.End > segment.Start));
        Assert.True(band.Fraction(At(1, 0, 1)) > band.Fraction(At(21)));
        Assert.Equal(At(21), band.From);
    }

    /// <summary>Ранний приход не обрезается: полоса начинается с его часа.</summary>
    [Fact]
    public void EarlyArrivalFitsOnTheLeft()
    {
        var day = Day(arrived: At(7, 12), left: At(15));
        var band = DayBand.For(day, At(16), tickStep: 2);

        Assert.Equal(At(7), band.From);
        Assert.True(band.From <= day.ArrivedAt);
    }

    /// <summary>Поздний уход не обрезается справа и не выходит за рабочие сутки.</summary>
    [Fact]
    public void LateDepartureFitsOnTheRight()
    {
        var day = Day(arrived: At(9), left: At(23, 40));
        var band = DayBand.For(day, At(23, 50), tickStep: 2);

        Assert.True(band.To >= day.LeftAt);
        Assert.True(band.To <= WorkDay.EndOf(day.Date, At(9).Offset));
    }

    /// <summary>
    /// Совсем короткий день не растягивается на всю ширину: иначе десять минут работы
    /// выглядят как полный день.
    /// </summary>
    [Fact]
    public void ShortDayKeepsAReadableScale()
    {
        var day = Day(arrived: At(9), left: At(9, 10));
        var band = DayBand.For(day, At(10), tickStep: 2);

        Assert.True(band.Span >= TimeSpan.FromHours(4));
        Assert.True(band.Fraction(At(9, 10)) < 0.1d);
    }

    /// <summary>Метки часов идут через заданный шаг и лежат внутри полосы.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TicksFollowTheStep(int step)
    {
        var day = Day(arrived: At(9), left: At(19));
        var band = DayBand.For(day, At(20), tickStep: step);

        Assert.NotEmpty(band.Ticks);
        Assert.All(band.Ticks, tick => Assert.Equal(0, tick.Hour % step));
        Assert.All(band.Ticks, tick => Assert.InRange(tick, band.From, band.To));
    }

    /// <summary>
    /// Незакрытый прошлый день тянется до последнего известного события, а не до сейчас:
    /// иначе полоса нарисовала бы работу, которой не было.
    /// </summary>
    [Fact]
    public void UnclosedPastDayStopsAtTheLastEvent()
    {
        var yesterday = Today.AddDays(-1);
        var day = Day(
            date: yesterday,
            arrived: At(9, 0, -1),
            intervals: [Away(At(16, 0, -1), At(16, 30, -1))]);

        var band = DayBand.For(day, At(12), tickStep: 2);

        Assert.Equal(At(16, 30, -1), band.Segments[^1].End);
    }

    /// <summary>Отлучка, уехавшая за пределы дня после правки файла руками, не роняет полосу.</summary>
    [Fact]
    public void BreaksOutsideTheDayAreIgnored()
    {
        var day = Day(
            arrived: At(9),
            left: At(18),
            intervals: [Away(At(5), At(6)), Away(At(20), At(21)), Lunch(At(13), At(13, 30))]);

        var band = DayBand.For(day, At(19), tickStep: 2);

        Assert.All(band.Segments, segment => Assert.InRange(segment.Start, At(9), At(18)));
        Assert.All(band.Segments, segment => Assert.InRange(segment.End, At(9), At(18)));
        Assert.Contains(band.Segments, segment => segment.Kind == BandKind.UnpaidBreak);
    }

    /// <summary>Доля не выходит за края, что бы ни спросили.</summary>
    [Fact]
    public void FractionStaysBetweenTheEdges()
    {
        var day = Day(arrived: At(9), left: At(18));
        var band = DayBand.For(day, At(19), tickStep: 2);

        Assert.Equal(0d, band.Fraction(At(1)));
        Assert.Equal(1d, band.Fraction(At(23, 59)));
    }

    private static BreakInterval Away(DateTimeOffset from, DateTimeOffset to) =>
        new(from, to, BreakKind.Paid, Guessed: false);

    private static BreakInterval Lunch(DateTimeOffset from, DateTimeOffset to) =>
        new(from, to, BreakKind.Unpaid, Guessed: false);

    /// <summary>Сводка дня ровно с теми полями, которые читает полоса.</summary>
    private static DaySummary Day(
        DateOnly? date = null,
        DateTimeOffset? arrived = null,
        DateTimeOffset? left = null,
        DateTimeOffset? projected = null,
        bool dayOff = false,
        IReadOnlyList<BreakInterval>? intervals = null) =>
        new(
            date ?? Today,
            arrived is null ? WorkState.NotStarted : left is null ? WorkState.Working : WorkState.NotStarted,
            Worked: TimeSpan.FromHours(7),
            Breaks: TimeSpan.Zero,
            ArrivedAt: arrived,
            LeftAt: left,
            ProjectedEnd: projected,
            Unpaid: TimeSpan.Zero,
            Intervals: intervals ?? [],
            Rules: TestClock.Goal(dayOff ? null : Hm(8)));
}
