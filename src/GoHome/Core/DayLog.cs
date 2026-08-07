using System.Text.Json.Serialization;

namespace GoHome.Core;

/// <summary>
/// Журнал одного рабочего дня — ровно то, что лежит в файле дня.
/// Хранится сырым: нормализация применяется на чтении, в расчёте.
/// Порядок свойств задаёт порядок полей в файле: служебные метки сверху,
/// длинный список отметок снизу, чтобы дописывать забытое руками было удобно.
/// </summary>
public sealed class DayLog
{
    /// <summary>Рабочая дата (со сдвигом <see cref="WorkDay.StartHour"/>).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Время последнего тика приложения.</summary>
    public DateTimeOffset? LastHeartbeat { get; set; }

    /// <summary>
    /// Время последней активности пользователя. Именно по нему закрывается день,
    /// если машина умерла с открытым интервалом: heartbeat и «сейчас» запишут уход в 03:00.
    /// </summary>
    public DateTimeOffset? LastUserActivity { get; set; }

    /// <summary>Уведомление о норме за этот день уже показано.</summary>
    public bool GoalNotified { get; set; }

    /// <summary>Отметки. Порядок не гарантируется — расчёт сортирует сам.</summary>
    public List<Punch> Punches { get; set; } = [];

    /// <summary>
    /// Файл не разобрался (человек правил руками и ошибся в JSON).
    /// Такой журнал нельзя перезаписывать — правка пользователя ценнее пустого файла.
    /// </summary>
    [JsonIgnore]
    public bool IsUnreadable { get; set; }

    /// <summary>Пустой журнал за дату.</summary>
    public static DayLog Empty(DateOnly date) => new() { Date = date };
}