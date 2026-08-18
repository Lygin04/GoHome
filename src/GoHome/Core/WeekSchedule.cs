using System.Text.Json.Serialization;

namespace GoHome.Core;

/// <summary>
/// Продолжительность рабочего дня по дням недели. <c>null</c> — нерабочий день.
/// </summary>
/// <remarks>
/// Семь отдельных полей, а не словарь: так файл настроек читается глазами сверху вниз
/// в привычном порядке, и ни один день нельзя пропустить или назвать дважды.
/// </remarks>
public sealed record WeekSchedule
{
    private static readonly TimeSpan Workday = WorkTimeCalculator.DefaultGoal;

    /// <summary>Пятидневка с восьмичасовым днём.</summary>
    public static readonly WeekSchedule Default = new();

    /// <summary>Продолжительность понедельника; <c>null</c> — нерабочий день.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Monday { get; init; } = Workday;

    /// <inheritdoc cref="Monday"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Tuesday { get; init; } = Workday;

    /// <inheritdoc cref="Monday"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Wednesday { get; init; } = Workday;

    /// <inheritdoc cref="Monday"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Thursday { get; init; } = Workday;

    /// <inheritdoc cref="Monday"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Friday { get; init; } = Workday;

    /// <inheritdoc cref="Monday"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Saturday { get; init; }

    /// <inheritdoc cref="Monday"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Sunday { get; init; }

    /// <summary>Продолжительность этого дня недели; <c>null</c> — нерабочий.</summary>
    public TimeSpan? this[DayOfWeek day] => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        _ => Sunday,
    };

    /// <summary>Тот же график с изменённым днём.</summary>
    public WeekSchedule With(DayOfWeek day, TimeSpan? hours) => day switch
    {
        DayOfWeek.Monday => this with { Monday = hours },
        DayOfWeek.Tuesday => this with { Tuesday = hours },
        DayOfWeek.Wednesday => this with { Wednesday = hours },
        DayOfWeek.Thursday => this with { Thursday = hours },
        DayOfWeek.Friday => this with { Friday = hours },
        DayOfWeek.Saturday => this with { Saturday = hours },
        _ => this with { Sunday = hours },
    };

    /// <summary>Дни недели в человеческом порядке — с понедельника.</summary>
    public static IReadOnlyList<DayOfWeek> Days { get; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];
}
