using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

public class WorkDayTests
{
    [Theory]
    [InlineData(4, 0, 0)]    // граница дня
    [InlineData(9, 0, 0)]
    [InlineData(23, 59, 0)]
    [InlineData(0, 30, 1)]   // ночь после вечерней работы — всё ещё вчерашний день
    [InlineData(3, 59, 1)]
    public void Момент_относится_к_рабочей_дате(int hour, int minute, int dayShift)
    {
        Assert.Equal(Today, WorkDay.DateOf(At(hour, minute, dayShift)));
    }

    [Fact]
    public void Четыре_утра_открывают_новый_день()
    {
        Assert.Equal(Today.AddDays(1), WorkDay.DateOf(At(4, 0, 1)));
    }

    [Fact]
    public void Три_ночи_относятся_к_предыдущей_дате()
    {
        Assert.Equal(Today, WorkDay.DateOf(At(3, 0, 1)));
    }

    [Fact]
    public void Границы_дня_смежные()
    {
        Assert.Equal(WorkDay.StartOf(Today.AddDays(1), Offset), WorkDay.EndOf(Today, Offset));
    }

    [Fact]
    public void Начало_дня_попадает_в_свой_же_день()
    {
        var start = WorkDay.StartOf(Today, Offset);

        Assert.Equal(Today, WorkDay.DateOf(start));
        Assert.Equal(WorkDay.StartHour, start.Hour);
    }
}