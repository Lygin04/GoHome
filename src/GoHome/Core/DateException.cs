using System.Text.Json.Serialization;

namespace GoHome.Core;

/// <summary>
/// Исключение по дате: отпуск, праздник, предпраздничный день сокращённой продолжительности.
/// Перекрывает недельный график.
/// </summary>
/// <remarks>
/// Производственный календарь не загружается: сеть из проекта убрана намеренно, а полтора
/// десятка дат в году вводятся руками за пару минут.
/// </remarks>
public sealed record DateException
{
    /// <summary>Рабочая дата, к которой относится исключение.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Продолжительность дня; <c>null</c> — нерабочий день.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Hours { get; init; }

    /// <summary>Пометка для человека: «Новый год», «отпуск». На расчёт не влияет.</summary>
    public string? Note { get; init; }

    /// <summary>Нерабочий день.</summary>
    [JsonIgnore]
    public bool IsDayOff => Hours is null;
}
