using WorkClock.Core;

namespace WorkClock.Tests;

public class TimePrecisionTests
{
    [Fact]
    public void Доли_секунды_отбрасываются()
    {
        var value = new DateTimeOffset(2026, 8, 6, 22, 7, 12, 896, TestClock.Offset).AddTicks(9586);

        var snapped = value.ToWholeSecond();

        Assert.Equal(new DateTimeOffset(2026, 8, 6, 22, 7, 12, TestClock.Offset), snapped);
    }

    [Fact]
    public void Целая_секунда_не_меняется()
    {
        var value = new DateTimeOffset(2026, 8, 6, 22, 7, 12, TestClock.Offset);

        Assert.Equal(value, value.ToWholeSecond());
    }

    [Fact]
    public void Смещение_сохраняется()
    {
        var value = new DateTimeOffset(2026, 8, 6, 22, 7, 12, 500, TimeSpan.FromHours(-5));

        Assert.Equal(TimeSpan.FromHours(-5), value.ToWholeSecond().Offset);
    }
}