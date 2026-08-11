namespace GoHome.Core;

/// <summary>Сводка за несколько последних рабочих дней. Тоже чистые функции.</summary>
public static class HistoryCalculator
{
    /// <summary>Последние <paramref name="days"/> рабочих дней, свежие сверху.</summary>
    public static IReadOnlyList<DaySummary> Recent(
        IEnumerable<DayLog> logs,
        DateTimeOffset now,
        int days,
        TimeSpan goal,
        LunchRules? lunch = null)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var rules = lunch ?? LunchRules.Default;
        var today = WorkDay.DateOf(now);
        var from = today.AddDays(-(days - 1));

        return logs
            .Where(log => log.Date >= from && log.Date <= today)
            .Select(log => WorkTimeCalculator.Compute(log, now, goal, rules))
            .OrderByDescending(summary => summary.Date)
            .ToList();
    }

    /// <summary>В выборке есть дни, посчитанные по разным правилам.</summary>
    public static bool HasMixedRules(IEnumerable<DaySummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var counted = summaries.Where(summary => summary.State != WorkState.NotStarted).ToList();
        return counted.Count > 0 && counted.Any(s => s.OnlyLunchUnpaid) && counted.Any(s => !s.OnlyLunchUnpaid);
    }

    /// <summary>Суммарно отработано.</summary>
    public static TimeSpan TotalWorked(IEnumerable<DaySummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return summaries.Aggregate(TimeSpan.Zero, (total, summary) => total + summary.Worked);
    }

    /// <summary>Отклонение от нормы за все дни, где человек появлялся.</summary>
    public static TimeSpan TotalBalance(IEnumerable<DaySummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return summaries
            .Where(summary => summary.State != WorkState.NotStarted)
            .Aggregate(TimeSpan.Zero, (total, summary) => total + summary.Balance);
    }
}