namespace WorkClock.Core;

/// <summary>
/// Границы рабочего дня. День сдвинут вперёд на несколько часов: работа до часу ночи
/// относится к предыдущей дате, иначе вечерняя переработка разъезжается на два файла.
/// </summary>
public static class WorkDay
{
    /// <summary>Час, с которого начинается новый рабочий день.</summary>
    public const int StartHour = 4;

    /// <summary>Рабочая дата, которой принадлежит момент времени.</summary>
    public static DateOnly DateOf(DateTimeOffset at) =>
        DateOnly.FromDateTime(at.AddHours(-StartHour).DateTime);

    /// <summary>Начало рабочего дня в указанном смещении.</summary>
    public static DateTimeOffset StartOf(DateOnly date, TimeSpan offset) =>
        new(date.ToDateTime(new TimeOnly(StartHour, 0)), offset);

    /// <summary>Конец рабочего дня, он же начало следующего.</summary>
    public static DateTimeOffset EndOf(DateOnly date, TimeSpan offset) =>
        StartOf(date.AddDays(1), offset);
}