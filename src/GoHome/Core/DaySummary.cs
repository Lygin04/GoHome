namespace GoHome.Core;

/// <summary>Результат расчёта дня. Считается из журнала целиком, состояние нигде не копится.</summary>
/// <param name="Date">Рабочая дата.</param>
/// <param name="State">Состояние дня на момент расчёта.</param>
/// <param name="Worked">Отработано: всё время присутствия за вычетом неоплачиваемых отлучек.</param>
/// <param name="Breaks">Суммарное время отлучек между приходом и уходом, независимо от зачёта.</param>
/// <param name="ArrivedAt">Приход.</param>
/// <param name="LeftAt">Уход, если день закрыт.</param>
/// <param name="ProjectedEnd">Прогноз окончания. Не определён, если время не идёт или день нерабочий.</param>
/// <param name="Unpaid">Сколько из <paramref name="Breaks"/> не пошло в зачёт.</param>
/// <param name="Intervals">
/// Отлучки, достаточно длинные, чтобы о них имело смысл говорить. Короткие в список не попадают,
/// чтобы не засорять историю, но в рабочее время они входят наравне с остальными.
/// </param>
/// <param name="Rules">Правила и цель, по которым посчитан день.</param>
public sealed record DaySummary(
    DateOnly Date,
    WorkState State,
    TimeSpan Worked,
    TimeSpan Breaks,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? LeftAt,
    DateTimeOffset? ProjectedEnd,
    TimeSpan Unpaid,
    IReadOnlyList<BreakInterval> Intervals,
    DayRules Rules)
{
    /// <summary>Норма за день. У нерабочего дня нулевая — но см. <see cref="IsDayOff"/>.</summary>
    public TimeSpan Goal => Rules.GoalOrZero;

    /// <summary>Нерабочий день: цели нет, и это не то же самое, что цель в ноль часов.</summary>
    public bool IsDayOff => Rules.IsDayOff;

    /// <summary>
    /// Норма достигнута. В нерабочий день — никогда: достигать нечего, и уведомлять не о чем.
    /// </summary>
    public bool GoalReached => !IsDayOff && Worked >= Goal;

    /// <summary>Сколько осталось до нормы.</summary>
    public TimeSpan Remaining => GoalReached || IsDayOff ? TimeSpan.Zero : Goal - Worked;

    /// <summary>
    /// Доля нормы от 0 до 1. В нерабочий день — ноль: делить не на что, а кольцо
    /// в этот день показывает нейтральное состояние, а не заполнение.
    /// </summary>
    public double Progress => IsDayOff || Goal <= TimeSpan.Zero
        ? 0d
        : Math.Clamp(Worked.TotalSeconds / Goal.TotalSeconds, 0d, 1d);

    /// <summary>
    /// Отклонение от нормы: положительное — переработка. В нерабочий день это всё
    /// отработанное, оно и уходит в недельный баланс со знаком плюс.
    /// </summary>
    public TimeSpan Balance => Worked - Goal;

    /// <summary>Счётчик идёт прямо сейчас.</summary>
    public bool IsRunning => State == WorkState.Working;

    /// <summary>День посчитан по правилам, в которых в зачёт идут короткие отлучки.</summary>
    public bool CountsShortBreaks => Rules.CountShortBreaks;

    /// <summary>Отлучка, которую догадка сочла обедом. Её пометку можно снять в один клик.</summary>
    public BreakInterval? GuessedLunch => Intervals.FirstOrDefault(interval => interval.Guessed);

    /// <summary>Отлучки, не пошедшие в зачёт.</summary>
    public IEnumerable<BreakInterval> UnpaidIntervals => Intervals.Where(interval => interval.IsUnpaid);
}