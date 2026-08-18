using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

public class HistoryCalculatorTests
{
    private static DayLog ClosedDay(int dayShift, int fromHour, int toHour)
    {
        var date = Today.AddDays(dayShift);
        return new DayLog
        {
            Date = date,
            Punches =
            [
                new Punch(At(fromHour, 0, dayShift), PunchKind.In),
                new Punch(At(toHour, 0, dayShift), PunchKind.Out),
            ],
        };
    }

    [Fact]
    public void Берутся_только_дни_из_окна_и_свежие_сверху()
    {
        DayLog[] logs =
        [
            ClosedDay(0, 9, 17),
            ClosedDay(-1, 9, 18),
            ClosedDay(-6, 9, 16),
            ClosedDay(-7, 9, 20), // за пределами недели
        ];

        var recent = HistoryCalculator.Recent(logs, At(19), 7, Even());

        Assert.Equal(3, recent.Count);
        Assert.Equal([Today, Today.AddDays(-1), Today.AddDays(-6)], recent.Select(s => s.Date));
    }

    [Fact]
    public void Итоги_недели_суммируются()
    {
        DayLog[] logs = [ClosedDay(0, 9, 17), ClosedDay(-1, 9, 18)];

        var recent = HistoryCalculator.Recent(logs, At(19), 7, Even());

        Assert.Equal(Hm(17), HistoryCalculator.TotalWorked(recent));
        Assert.Equal(Hm(1), HistoryCalculator.TotalBalance(recent));
    }

    [Fact]
    public void Дни_без_прихода_не_портят_баланс()
    {
        DayLog[] logs = [ClosedDay(0, 9, 17), new DayLog { Date = Today.AddDays(-1) }];

        var recent = HistoryCalculator.Recent(logs, At(19), 7, Even());

        Assert.Equal(2, recent.Count);
        Assert.Equal(TimeSpan.Zero, HistoryCalculator.TotalBalance(recent));
    }
}