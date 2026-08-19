namespace GoHome.Core;

/// <summary>Форматирование для тултипа, меню и окна истории.</summary>
public static class WorkTimeFormat
{
    /// <summary>Длительность как <c>6:24</c>; отрицательная — со знаком.</summary>
    public static string Duration(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var abs = value.Duration();
        return $"{sign}{(int)abs.TotalHours}:{abs.Minutes:00}";
    }

    /// <summary>То же, но с явным плюсом у переработки.</summary>
    public static string SignedDuration(TimeSpan value) =>
        value > TimeSpan.Zero ? "+" + Duration(value) : Duration(value);

    /// <summary>Время суток как <c>09:12</c> — из длительности от полуночи.</summary>
    public static string TimeOfDay(TimeSpan value)
    {
        var abs = value.Duration();
        return $"{(int)abs.TotalHours % 24:00}:{abs.Minutes:00}";
    }

    /// <summary>Время суток как <c>09:12</c>, либо прочерк.</summary>
    public static string Clock(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("HH:mm") : "—";

    /// <summary>Длительность словами: <c>45 мин</c>, <c>1 ч 05 мин</c>.</summary>
    public static string Minutes(TimeSpan value)
    {
        var abs = value.Duration();
        var hours = (int)abs.TotalHours;
        return hours > 0 ? $"{hours} ч {abs.Minutes:00} мин" : $"{abs.Minutes} мин";
    }

    /// <summary>Отлучка как <c>12:40–13:25 (45 мин)</c>.</summary>
    public static string Interval(BreakInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return $"{Clock(interval.Start)}–{Clock(interval.End)} ({Minutes(interval.Duration)})";
    }
}