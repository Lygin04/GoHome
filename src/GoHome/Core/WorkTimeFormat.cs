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

    /// <summary>Время суток как <c>09:12</c>, либо прочерк.</summary>
    public static string Clock(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("HH:mm") : "—";
}