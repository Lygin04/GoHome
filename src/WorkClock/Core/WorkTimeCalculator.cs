namespace WorkClock.Core;

/// <summary>
/// Расчётный слой. Чистые функции: текущее время всегда приходит параметром,
/// внутри нет ни одного обращения к системным часам.
/// </summary>
public static class WorkTimeCalculator
{
    /// <summary>Норма рабочего дня.</summary>
    public static readonly TimeSpan DefaultGoal = TimeSpan.FromHours(8);

    /// <inheritdoc cref="Compute(DayLog, DateTimeOffset, TimeSpan)"/>
    public static DaySummary Compute(DayLog log, DateTimeOffset now) => Compute(log, now, DefaultGoal);

    /// <summary>
    /// Пересчитывает день из журнала.
    /// </summary>
    /// <remarks>
    /// Нормализация применяется здесь, а не при записи:
    /// <list type="bullet">
    /// <item>первое событие дня всегда трактуется как приход — утренняя разблокировка приходит как <see cref="PunchKind.BreakEnd"/>;</item>
    /// <item>висящий перерыв в уже закрытом дне — это уход: человек нажал Win+L и пошёл домой;</item>
    /// <item>висящий перерыв в текущем дне остаётся перерывом: человек на обеде.</item>
    /// </list>
    /// </remarks>
    public static DaySummary Compute(DayLog log, DateTimeOffset now, TimeSpan goal)
    {
        ArgumentNullException.ThrowIfNull(log);

        var isCurrentDay = WorkDay.DateOf(now) == log.Date;

        // Сортировка устойчивая, поэтому у отметок с одинаковой меткой времени
        // решает порядок в файле: приложение дописывает их хронологически,
        // и «разблокировал и тут же заблокировал» не должно читаться наоборот.
        var punches = log.Punches
            .Where(p => p is not null)
            .OrderBy(p => p.At)
            .ToList();

        var worked = TimeSpan.Zero;
        DateTimeOffset? openSince = null;    // интервал работы открыт с этого момента
        DateTimeOffset? arrivedAt = null;
        DateTimeOffset? leftAt = null;
        DateTimeOffset? pausedSince = null;  // висящий перерыв

        foreach (var punch in punches)
        {
            // Первое событие дня — всегда приход, каким бы типом оно ни записалось.
            var kind = arrivedAt is null ? PunchKind.In : punch.Kind;

            switch (kind)
            {
                case PunchKind.In:
                case PunchKind.BreakEnd:
                    arrivedAt ??= punch.At;
                    openSince ??= punch.At;
                    pausedSince = null;
                    leftAt = null;
                    break;

                case PunchKind.BreakStart:
                    // Перерыв без открытого интервала (повторная блокировка, пауза после ухода) — шум, пропускаем.
                    if (openSince is { } pauseFrom)
                    {
                        worked += NonNegative(punch.At - pauseFrom);
                        openSince = null;
                        pausedSince = punch.At;
                    }

                    break;

                case PunchKind.Out:
                    if (openSince is { } outFrom)
                    {
                        worked += NonNegative(punch.At - outFrom);
                    }

                    openSince = null;
                    pausedSince = null;
                    leftAt = punch.At;
                    break;
            }
        }

        WorkState state;

        if (arrivedAt is null)
        {
            state = WorkState.NotStarted;
        }
        else if (openSince is { } running)
        {
            if (isCurrentDay)
            {
                worked += NonNegative(now - running);
                state = WorkState.Working;
            }
            else
            {
                // День остался незакрытым: машину выключили с открытым интервалом.
                // Закрываем по последней активности пользователя, а не по «сейчас».
                var closedAt = ClosingTime(log, running);
                worked += NonNegative(closedAt - running);
                leftAt = closedAt;
                state = WorkState.Finished;
            }
        }
        else if (pausedSince is { } paused)
        {
            if (isCurrentDay)
            {
                state = WorkState.OnBreak;
            }
            else
            {
                state = WorkState.Finished;
                leftAt = paused;
            }
        }
        else
        {
            state = WorkState.Finished;
        }

        var breaks = arrivedAt is { } from
            ? NonNegative(NonNegative((leftAt ?? now) - from) - worked)
            : TimeSpan.Zero;

        var projectedEnd = state == WorkState.Working
            ? now + (worked >= goal ? TimeSpan.Zero : goal - worked)
            : (DateTimeOffset?)null;

        return new DaySummary(log.Date, state, worked, breaks, arrivedAt, leftAt, projectedEnd, goal);
    }

    /// <summary>
    /// Чем закрыть незакрытый прошлый день. Порядок важен: последняя активность,
    /// затем последний heartbeat, затем начало интервала (то есть ноль сверху).
    /// </summary>
    private static DateTimeOffset ClosingTime(DayLog log, DateTimeOffset openSince)
    {
        var candidate = log.LastUserActivity ?? log.LastHeartbeat ?? openSince;
        var dayEnd = WorkDay.EndOf(log.Date, openSince.Offset);
        if (candidate < openSince)
        {
            return openSince;
        }

        return candidate > dayEnd ? dayEnd : candidate;
    }

    private static TimeSpan NonNegative(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;
}