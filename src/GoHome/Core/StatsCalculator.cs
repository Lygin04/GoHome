namespace GoHome.Core;

/// <summary>
/// Статистика за период. Только читает: новых сущностей в хранилище не появляется,
/// и ни один день от разглядывания не меняется.
/// </summary>
public static class StatsCalculator
{
    private static readonly long DayTicks = TimeSpan.FromDays(1).Ticks;

    /// <summary>
    /// Считает период целиком.
    /// </summary>
    /// <param name="logs">Журналы. Лишние отбрасываются, недостающие дни считаются пустыми.</param>
    /// <param name="now">Момент расчёта.</param>
    /// <param name="settings">
    /// Что действует сейчас — только для дней без собственного снимка. У прожитого дня
    /// цель своя, сохранённая при его создании, и статистика её не переписывает.
    /// </param>
    /// <param name="range">Период.</param>
    /// <remarks>
    /// Нечитаемый файл дня расчёт не роняет: такой день пропускается и попадает
    /// в <see cref="PeriodStats.Unreadable"/> — чтобы человеку было видно, что числа неполные.
    /// </remarks>
    public static PeriodStats For(
        IEnumerable<DayLog> logs,
        DateTimeOffset now,
        AppSettings settings,
        PeriodRange range)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(settings);

        var byDate = new Dictionary<DateOnly, DayLog>();
        var unreadable = 0;

        foreach (var log in logs)
        {
            if (log is null || !range.Contains(log.Date))
            {
                continue;
            }

            if (log.IsUnreadable)
            {
                unreadable++;
                continue;
            }

            byDate[log.Date] = log;
        }

        var today = WorkDay.DateOf(now);
        var days = new List<DaySummary>(range.Length);

        var worked = TimeSpan.Zero;
        var norm = TimeSpan.Zero;
        var normSoFar = TimeSpan.Zero;
        var workedDays = 0;
        var daysOff = 0;

        var arrivals = new List<TimeSpan>();
        var departures = new List<TimeSpan>();
        var lengths = new List<TimeSpan>();
        var unpaid = new List<TimeSpan>();

        foreach (var date in range.Dates)
        {
            var log = byDate.TryGetValue(date, out var found) ? found : DayLog.Empty(date);
            var summary = WorkTimeCalculator.Compute(log, now, settings.RulesFor(date));
            days.Add(summary);

            // Нерабочий день в норму не входит — его цель нулевая, — а отработанное
            // в нём идёт в общий счёт и всплывает в балансе со знаком плюс.
            worked += summary.Worked;
            norm += summary.Goal;
            if (date <= today)
            {
                normSoFar += summary.Goal;
            }

            if (summary.IsDayOff)
            {
                daysOff++;
            }

            if (summary.Worked > TimeSpan.Zero)
            {
                workedDays++;
                lengths.Add(summary.Worked);
            }

            if (summary.Unpaid > TimeSpan.Zero)
            {
                unpaid.Add(summary.Unpaid);
            }

            if (summary.ArrivedAt is { } arrived)
            {
                arrivals.Add(SinceDayStart(date, arrived));
            }

            if (summary.LeftAt is { } left)
            {
                departures.Add(SinceDayStart(date, left));
            }
        }

        return new PeriodStats(
            range,
            days,
            worked,
            norm,
            normSoFar,
            workedDays,
            daysOff,
            unreadable,
            TimeOfDay(arrivals),
            TimeOfDay(departures),
            Average(lengths),
            Average(unpaid));
    }

    /// <summary>
    /// Сколько прошло от начала рабочего дня.
    /// </summary>
    /// <remarks>
    /// Именно от начала рабочего дня, а не от полуночи: уход в час ночи принадлежит
    /// предыдущей дате, и в среднем времени ухода он должен стоять после полуночи,
    /// а не утаскивать среднее на утро.
    /// </remarks>
    private static TimeSpan SinceDayStart(DateOnly date, DateTimeOffset moment) =>
        moment - WorkDay.StartOf(date, moment.Offset);

    /// <summary>Среднее время суток по смещениям от начала рабочего дня.</summary>
    private static TimeSpan? TimeOfDay(List<TimeSpan> offsets)
    {
        if (Average(offsets) is not { } average)
        {
            return null;
        }

        var ticks = (TimeSpan.FromHours(WorkDay.StartHour) + average).Ticks;
        return TimeSpan.FromTicks(((ticks % DayTicks) + DayTicks) % DayTicks);
    }

    /// <summary>Среднее. У пустого списка среднего нет — делить не на что.</summary>
    private static TimeSpan? Average(List<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var total = 0L;
        foreach (var value in values)
        {
            total += value.Ticks;
        }

        return TimeSpan.FromTicks(total / values.Count);
    }
}
