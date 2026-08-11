namespace GoHome.Core;

/// <summary>
/// Расчётный слой. Чистые функции: текущее время всегда приходит параметром,
/// внутри нет ни одного обращения к системным часам.
/// </summary>
public static class WorkTimeCalculator
{
    /// <summary>Норма рабочего дня.</summary>
    public static readonly TimeSpan DefaultGoal = TimeSpan.FromHours(8);

    /// <inheritdoc cref="Compute(DayLog, DateTimeOffset, TimeSpan, LunchRules)"/>
    public static DaySummary Compute(DayLog log, DateTimeOffset now) =>
        Compute(log, now, DefaultGoal, LunchRules.Default);

    /// <inheritdoc cref="Compute(DayLog, DateTimeOffset, TimeSpan, LunchRules)"/>
    public static DaySummary Compute(DayLog log, DateTimeOffset now, TimeSpan goal) =>
        Compute(log, now, goal, LunchRules.Default);

    /// <summary>
    /// Пересчитывает день из журнала.
    /// </summary>
    /// <remarks>
    /// Нормализация применяется здесь, а не при записи:
    /// <list type="bullet">
    /// <item>первое событие дня всегда трактуется как приход — утренняя разблокировка приходит как <see cref="PunchKind.BreakEnd"/>;</item>
    /// <item>висящий перерыв в уже закрытом дне — это уход: человек нажал Win+L и пошёл домой;</item>
    /// <item>висящий перерыв в текущем дне остаётся перерывом: человек отошёл.</item>
    /// </list>
    /// Классификация отлучек — оттуда же: в зачёт не идёт только обед, и определяется он
    /// правилами <see cref="LunchRules"/> и поправками из журнала, а не типом отметки.
    /// Дни, начатые до смены правил, считаются по-старому — см. <see cref="RulesVersion"/>.
    /// </remarks>
    public static DaySummary Compute(DayLog log, DateTimeOffset now, TimeSpan goal, LunchRules rules)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(rules);

        var isCurrentDay = WorkDay.DateOf(now) == log.Date;

        // Сортировка устойчивая, поэтому у отметок с одинаковой меткой времени
        // решает порядок в файле: приложение дописывает их хронологически,
        // и «разблокировал и тут же заблокировал» не должно читаться наоборот.
        var punches = log.Punches
            .Where(p => p is not null)
            .OrderBy(p => p.At)
            .ToList();

        var atDesk = TimeSpan.Zero;          // время с открытым интервалом, без отлучек
        var closed = new List<(DateTimeOffset Start, DateTimeOffset End)>();
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
                    if (pausedSince is { } breakFrom)
                    {
                        closed.Add((breakFrom, punch.At));
                    }

                    arrivedAt ??= punch.At;
                    openSince ??= punch.At;
                    pausedSince = null;
                    leftAt = null;
                    break;

                case PunchKind.BreakStart:
                    // Перерыв без открытого интервала (повторная блокировка, пауза после ухода) — шум, пропускаем.
                    if (openSince is { } pauseFrom)
                    {
                        atDesk += NonNegative(punch.At - pauseFrom);
                        openSince = null;
                        pausedSince = punch.At;
                    }

                    break;

                case PunchKind.Out:
                    if (openSince is { } outFrom)
                    {
                        atDesk += NonNegative(punch.At - outFrom);
                    }

                    // Отлучка, оборвавшаяся уходом, в зачёт не идёт: человек не вернулся.
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
                atDesk += NonNegative(now - running);
                state = WorkState.Working;
            }
            else
            {
                // День остался незакрытым: машину выключили с открытым интервалом.
                // Закрываем по последней активности пользователя, а не по «сейчас».
                var closedAt = ClosingTime(log, running);
                atDesk += NonNegative(closedAt - running);
                leftAt = closedAt;
                state = WorkState.Finished;
            }
        }
        else if (pausedSince is { } paused)
        {
            if (isCurrentDay)
            {
                // Открытая отлучка в зачёт пока не идёт: чем она окажется, станет ясно
                // на возвращении. Так счётчик никогда не приходится уменьшать задним числом.
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

        var version = RulesVersion.Of(log);
        var intervals = Classify(closed, log.Adjustments, rules, version);

        var unpaid = TimeSpan.Zero;
        var paidBreaks = TimeSpan.Zero;
        foreach (var interval in intervals)
        {
            if (interval.IsUnpaid)
            {
                unpaid += interval.Duration;
            }
            else
            {
                paidBreaks += interval.Duration;
            }
        }

        var away = arrivedAt is { } present
            ? NonNegative(NonNegative((leftAt ?? now) - present) - atDesk)
            : TimeSpan.Zero;

        // Считаются только закрытые отлучки. Открытая ещё не известна чем окажется,
        // а оборвавшаяся уходом — это уже дорога домой; ни ту, ни другую в зачёт не берём.
        var worked = atDesk + paidBreaks;

        var projectedEnd = state == WorkState.Working
            ? now + (worked >= goal ? TimeSpan.Zero : goal - worked)
            : (DateTimeOffset?)null;

        return new DaySummary(
            log.Date,
            state,
            worked,
            away,
            arrivedAt,
            leftAt,
            projectedEnd,
            goal,
            unpaid,
            Significant(intervals, rules),
            version);
    }

    /// <summary>
    /// Раздаёт отлучкам зачёт. Порядок такой: сперва поправка человека, если она есть,
    /// затем правила версии, и только потом догадка.
    /// </summary>
    private static List<BreakInterval> Classify(
        List<(DateTimeOffset Start, DateTimeOffset End)> closed,
        List<BreakAdjustment>? adjustments,
        LunchRules rules,
        int version)
    {
        var onlyLunchUnpaid = RulesVersion.OnlyLunchUnpaid(version);
        var intervals = new List<BreakInterval>(closed.Count);

        foreach (var (start, end) in closed)
        {
            var fixedKind = AdjustmentFor(adjustments, start);

            // По старым правилам в зачёт не шла ни одна отлучка. Явная поправка человека
            // сильнее и там: он поправляет уже посчитанный день осознанно.
            var kind = fixedKind ?? (onlyLunchUnpaid ? BreakKind.Paid : BreakKind.Unpaid);
            intervals.Add(new BreakInterval(start, end, kind, Guessed: false));
        }

        if (!onlyLunchUnpaid)
        {
            return intervals;
        }

        // Обед бывает не больше одного раза в день: как только он определён — хоть догадкой,
        // хоть рукой, — остальные длинные отлучки идут в зачёт без вопросов.
        if (intervals.Any(interval => interval.IsUnpaid))
        {
            return intervals;
        }

        for (var i = 0; i < intervals.Count; i++)
        {
            // Отлучку, о которой человек уже высказался, догадка не трогает: иначе
            // отменённая пометка возвращалась бы на то же место.
            if (AdjustmentFor(adjustments, intervals[i].Start) is not null)
            {
                continue;
            }

            if (rules.CouldBeLunch(intervals[i]))
            {
                intervals[i] = intervals[i] with { Kind = BreakKind.Unpaid, Guessed = true };
                break;
            }
        }

        return intervals;
    }

    /// <summary>
    /// Поправка, привязанная к началу интервала. Поправка, которая ни на что не указывает
    /// (файл правили руками и время сдвинулось), просто не находится.
    /// </summary>
    private static BreakKind? AdjustmentFor(List<BreakAdjustment>? adjustments, DateTimeOffset start)
    {
        if (adjustments is null)
        {
            return null;
        }

        BreakKind? found = null;
        foreach (var adjustment in adjustments)
        {
            // Последняя поправка на тот же момент побеждает: так проще дописывать руками.
            if (adjustment is not null && adjustment.BreakAt.UtcTicks == start.UtcTicks)
            {
                found = adjustment.Kind;
            }
        }

        return found;
    }

    /// <summary>Отлучки, о которых имеет смысл говорить вслух. Короткие в историю не попадают.</summary>
    private static IReadOnlyList<BreakInterval> Significant(List<BreakInterval> intervals, LunchRules rules) =>
        intervals.Where(interval => rules.IsSignificant(interval.Duration)).ToList();

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