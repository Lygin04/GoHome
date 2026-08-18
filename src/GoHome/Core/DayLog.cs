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

    /// <summary>
    /// Правила и цель, по которым считается день. Проставляются при создании дня и дальше
    /// не меняются — кроме текущего дня, который обновляется при смене настроек: иначе
    /// нельзя было бы поправить сегодняшнюю цель.
    /// </summary>
    public DayRules? Rules { get; set; }

    /// <summary>
    /// Устаревшее поле версии правил. Читается только ради файлов, созданных до появления
    /// снимка настроек, и никогда не проставляется заново — см. <see cref="DayRules.Of"/>.
    /// </summary>
    public int? RulesVersion { get; set; }

    /// <summary>Начало отлучки, о которой уже сообщили как об угаданном обеде.</summary>
    public DateTimeOffset? LunchNotifiedAt { get; set; }

    /// <summary>
    /// Поправки к классификации отлучек. Отдельно от отметок: в момент блокировки тип
    /// отлучки неизвестен, поэтому журнал остаётся сырым, а поправки применяются на чтении.
    /// Пусто, пока никто ничего не поправил — в файле дня лишнего поля тогда не появляется.
    /// </summary>
    public List<BreakAdjustment>? Adjustments { get; set; }

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