namespace GoHome.Core;

/// <summary>Точность меток в журнале.</summary>
public static class TimePrecision
{
    /// <summary>
    /// Обрезает метку до целой секунды. Расчёт всё равно оперирует минутами,
    /// а файл, который правят руками, не должен пестрить семизначными долями секунды.
    /// </summary>
    public static DateTimeOffset ToWholeSecond(this DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
}