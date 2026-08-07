namespace GoHome.Core;

/// <summary>Результат расчёта дня. Считается из журнала целиком, состояние нигде не копится.</summary>
/// <param name="Date">Рабочая дата.</param>
/// <param name="State">Состояние дня на момент расчёта.</param>
/// <param name="Worked">Отработано.</param>
/// <param name="Breaks">Суммарные перерывы между приходом и уходом.</param>
/// <param name="ArrivedAt">Приход.</param>
/// <param name="LeftAt">Уход, если день закрыт.</param>
/// <param name="ProjectedEnd">Прогноз окончания. Не определён, если время не идёт.</param>
/// <param name="Goal">Норма за день.</param>
public sealed record DaySummary(
    DateOnly Date,
    WorkState State,
    TimeSpan Worked,
    TimeSpan Breaks,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? LeftAt,
    DateTimeOffset? ProjectedEnd,
    TimeSpan Goal)
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
}