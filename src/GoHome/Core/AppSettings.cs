using System.Text.Json.Serialization;

namespace GoHome.Core;

/// <summary>
/// Настройки приложения целиком — ровно то, что лежит в файле настроек.
/// </summary>
/// <remarks>
/// Настройки двух разных сортов, и разница принципиальная.
/// <list type="bullet">
/// <item>
/// <b>Правила расчёта</b> (<see cref="CountShortBreaks"/>, <see cref="Lunch"/>,
/// <see cref="Schedule"/>, <see cref="Exceptions"/>) определяют, как минуты превращаются
/// в отработанное время. Их изменение задним числом меняет смысл накопленных дней, поэтому
/// они ложатся снимком в файл дня при его создании — см. <see cref="DayRules"/>.
/// </item>
/// <item>
/// <b>Предпочтения</b> (<see cref="Theme"/>, <see cref="WarnBefore"/>) определяют,
/// что показывается. Историю не затрагивают и применяются немедленно.
/// </item>
/// </list>
/// </remarks>
public sealed record AppSettings
{
    /// <summary>
    /// За сколько до нормы предупреждать по умолчанию. Четверти часа хватает, чтобы
    /// свернуть дела, и не настолько много, чтобы предупреждение забылось.
    /// </summary>
    /// <remarks>Объявлено выше <see cref="Default"/>: статические поля считаются по порядку.</remarks>
    public static readonly TimeSpan DefaultWarnBefore = TimeSpan.FromMinutes(15);

    /// <summary>Значения по умолчанию.</summary>
    public static readonly AppSettings Default = new();

    /// <inheritdoc cref="DayRules.CountShortBreaks"/>
    public bool CountShortBreaks { get; init; } = true;

    /// <summary>Когда отлучка может оказаться обедом. При выключенном зачёте не влияет ни на что.</summary>
    public LunchRules Lunch { get; init; } = LunchRules.Default;

    /// <summary>Продолжительность рабочего дня по дням недели.</summary>
    public WeekSchedule Schedule { get; init; } = WeekSchedule.Default;

    /// <summary>Даты, перекрывающие недельный график. Отпуск, праздники, сокращённые дни.</summary>
    public IReadOnlyList<DateException> Exceptions { get; init; } = [];

    /// <summary>Оформление окон. На кольцо в трее не влияет.</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>
    /// За сколько до нормы предупредить. Ноль — не предупреждать.
    /// </summary>
    /// <remarks>
    /// Предпочтение, а не правило: в снимок дня не ложится и действует немедленно —
    /// от него зависит только момент уведомления, но не отработанное время.
    /// </remarks>
    public TimeSpan WarnBefore { get; init; } = DefaultWarnBefore;

    /// <summary>Предупреждение включено.</summary>
    [JsonIgnore]
    public bool WarnsBeforeGoal => WarnBefore > TimeSpan.Zero;

    /// <summary>
    /// Сравнение по значению, включая список исключений.
    /// </summary>
    /// <remarks>
    /// Запись сравнивала бы список по ссылке, и два одинаковых набора настроек оказывались бы
    /// разными — а на этом сравнении держится вопрос «изменилось ли что-нибудь».
    /// </remarks>
    public bool Equals(AppSettings? other) =>
        other is not null
        && CountShortBreaks == other.CountShortBreaks
        && Lunch == other.Lunch
        && Schedule == other.Schedule
        && Theme == other.Theme
        && WarnBefore == other.WarnBefore
        && Exceptions.SequenceEqual(other.Exceptions);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CountShortBreaks);
        hash.Add(Lunch);
        hash.Add(Schedule);
        hash.Add(Theme);
        hash.Add(WarnBefore);
        foreach (var exception in Exceptions)
        {
            hash.Add(exception);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Цель на рабочую дату: исключение важнее графика.
    /// </summary>
    /// <remarks>
    /// Дата здесь — рабочая, со сдвигом на <see cref="WorkDay.StartHour"/>. Работа
    /// с пятницы 22:00 до субботы 01:00 — пятница со своей целью, потому что рабочая
    /// дата у обоих моментов пятничная.
    /// </remarks>
    public TimeSpan? GoalFor(DateOnly date)
    {
        // Последнее исключение на дату побеждает: так проще дописывать список руками.
        TimeSpan? goal = null;
        var found = false;

        foreach (var exception in Exceptions)
        {
            if (exception is not null && exception.Date == date)
            {
                goal = exception.Hours;
                found = true;
            }
        }

        return found ? goal : Schedule[date.DayOfWeek];
    }

    /// <summary>Исключение, заданное на эту дату, если оно есть.</summary>
    public DateException? ExceptionFor(DateOnly date)
    {
        DateException? found = null;
        foreach (var exception in Exceptions)
        {
            if (exception is not null && exception.Date == date)
            {
                found = exception;
            }
        }

        return found;
    }

    /// <summary>Правила и цель, действующие для этой рабочей даты прямо сейчас.</summary>
    public DayRules RulesFor(DateOnly date) => new()
    {
        Goal = GoalFor(date),
        CountShortBreaks = CountShortBreaks,
        Lunch = Lunch,
    };

    /// <summary>Тот же набор с заданным или снятым исключением на дату.</summary>
    /// <param name="date">Рабочая дата.</param>
    /// <param name="exception">Новое исключение либо <c>null</c>, чтобы вернуть день графику.</param>
    public AppSettings WithException(DateOnly date, DateException? exception)
    {
        var kept = Exceptions.Where(e => e is not null && e.Date != date).ToList();
        if (exception is not null)
        {
            kept.Add(exception with { Date = date });
        }

        kept.Sort((left, right) => left.Date.CompareTo(right.Date));
        return this with { Exceptions = kept };
    }
}