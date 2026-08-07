using GoHome.Interop;

namespace GoHome.Tests;

public class UserActivityTests
{
    [Fact]
    public void Простой_считается_разностью_тиков()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(600), UserActivity.IdleFromTicks(1_000, 400));
    }

    [Fact]
    public void Нулевой_простой_даёт_ноль()
    {
        Assert.Equal(TimeSpan.Zero, UserActivity.IdleFromTicks(123_456, 123_456));
    }

    [Fact]
    public void Переполнение_счётчика_тиков_не_ломает_простой()
    {
        // 49 суток аптайма: счётчик перевалил через uint и пошёл с нуля.
        // Наивная арифметика выдала бы здесь простой длиной в полтора месяца.
        const uint lastInput = uint.MaxValue - 899;
        const uint now = 100;

        Assert.Equal(TimeSpan.FromSeconds(1), UserActivity.IdleFromTicks(now, lastInput));
    }

    [Fact]
    public void Максимальный_простой_не_превышает_период_переполнения()
    {
        var idle = UserActivity.IdleFromTicks(0, 1);

        Assert.Equal(TimeSpan.FromMilliseconds(uint.MaxValue), idle);
        Assert.True(idle < TimeSpan.FromDays(50));
    }

    [Fact]
    public void Системный_простой_неотрицателен()
    {
        var idle = UserActivity.GetIdleTime();

        Assert.True(idle >= TimeSpan.Zero);
        Assert.True(idle <= TimeSpan.FromMilliseconds(uint.MaxValue));
    }
}