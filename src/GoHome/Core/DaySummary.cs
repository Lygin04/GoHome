namespace GoHome.Core;

/// <summary>Результат расчёта дня. Считается из журнала целиком, состояние нигде не копится.</summary>
/// <param name="Date">Рабочая дата.</param>
/// <param name="State">Состояние дня на момент расчёта.</param>
/// <param name="Worked">Отработано: всё время присутствия за вычетом неоплачиваемых отлучек.</param>
/// <param name="Breaks">Суммарное время отлучек между приходом и уходом, независимо от зачёта.</param>
/// <param name="ArrivedAt">Приход.</param>
/// <param name="LeftAt">Уход, если день закрыт.</param>
/// <param name="ProjectedEnd">Прогноз окончания. Не определён, если время не идёт.</param>
/// <param name="Goal">Норма за день.</param>
/// <param name="Unpaid">Сколько из <paramref name="Breaks"/> не пошло в зачёт.</param>
/// <param name="Intervals">
/// Отлучки, достаточно длинные, чтобы о них имело смысл говорить. Короткие в список не попадают,
/// чтобы не засорять историю, но в рабочее время они входят наравне с остальными.
/// </param>
/// <param name="RulesVersion">Версия правил, по которым посчитан день.</param>
public sealed record DaySummary(
    DateOnly Date,
    WorkState State,
    TimeSpan Worked,
    TimeSpan Breaks,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? LeftAt,
    DateTimeOffset? ProjectedEnd,
    TimeSpan Goal,
    TimeSpan Unpaid,
    IReadOnlyList<BreakInterval> Intervals,
    int RulesVersion)
{
    /// <summary>Норма достигнута.</summary>
    public bool GoalReached => Worked >= Goal;

    /// <summary>Сколько осталось до нормы.</summary>
    public TimeSpan Remaining => Worked >= Goal ? TimeSpan.Zero : Goal - Worked;

    /// <summary>Доля нормы от 0 до 1.</summary>
    public double Progress => Goal > TimeSpan.Zero
        ? Math.Clamp(Worked.TotalSeconds / Goal.TotalSeconds, 0d, 1d)
        : 1d;

    /// <summary>Отклонение от нормы: положительное — переработка.</summary>
    public TimeSpan Balance => Worked - Goal;

    /// <summary>Счётчик идёт прямо сейчас.</summary>
    public bool IsRunning => State == WorkState.Working;

    /// <summary>День посчитан по правилам, в которых не идёт в зачёт только обед.</summary>
    public bool OnlyLunchUnpaid => Core.RulesVersion.OnlyLunchUnpaid(RulesVersion);

    /// <summary>Отлучка, которую догадка сочла обедом. Её пометку можно снять в один клик.</summary>
    public BreakInterval? GuessedLunch => Intervals.FirstOrDefault(interval => interval.Guessed);

    /// <summary>Отлучки, не пошедшие в зачёт.</summary>
    public IEnumerable<BreakInterval> UnpaidIntervals => Intervals.Where(interval => interval.IsUnpaid);
}