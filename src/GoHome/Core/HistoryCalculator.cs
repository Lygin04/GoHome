namespace GoHome.Core;

/// <summary>Сводка за несколько последних рабочих дней. Тоже чистые функции.</summary>
public static class HistoryCalculator
{
    /// <summary>Последние <paramref name="days"/> рабочих дней, свежие сверху.</summary>
    /// <param name="logs">Журналы. Дни без файлов тоже нужны — они попадают в сводку пустыми.</param>
    /// <param name="now">Момент расчёта.</param>
    /// <param name="days">Сколько дней показать.</param>
    /// <param name="settings">
    /// Что действует сейчас. Используется только для дней без собственного снимка:
    /// изменение графика в среду не переписывает понедельник.
    /// </param>
    public static IReadOnlyList<DaySummary> Recent(
        IEnumerable<DayLog> logs,
        DateTimeOffset now,
        int days,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var today = WorkDay.DateOf(now);
        var from = today.AddDays(-(days - 1));

        return logs
            .Where(log => log.Date >= from && log.Date <= today)
            .Select(log => WorkTimeCalculator.Compute(log, now, settings.RulesFor(log.Date)))
            .OrderByDescending(summary => summary.Date)
            .ToList();
    }

    /// <summary>
    /// Понедельник недели, которой принадлежит рабочая дата.
    /// </summary>
    /// <remarks>
    /// Границы недели считаются по рабочим датам, а не по календарным: работа
    /// с воскресенья 22:00 до понедельника 01:00 относится к воскресенью, потому что
    /// рабочий день сдвинут на <see cref="WorkDay.StartHour"/> — и попадает в ту неделю,
    /// которая воскресеньем заканчивается.
    /// </remarks>
    public static DateOnly WeekStart(DateOnly date)
    {
        var shift = ((int)date.DayOfWeek + 6) % 7;   // понедельник — ноль
        return date.AddDays(-shift);
    }

    /// <summary>
    /// Недельный баланс: сколько отработано относительно нормы и накопленная разница.
    /// </summary>
    /// <remarks>
    /// Только показывается и на дневные цели не влияет. Соблазн уменьшить пятничную цель
    /// на переработанное в понедельник есть, но тогда кольцо начинает прыгать, а уведомление
    /// приходит в непредсказуемый момент — объяснить цифру становится невозможно.
    /// </remarks>
    /// <param name="logs">Журналы за неделю; дни без файлов тоже.</param>
    /// <param name="now">Момент расчёта.</param>
    /// <param name="settings">Что действует сейчас — для дней, у которых снимка ещё нет.</param>
    public static WeekSummary Week(IEnumerable<DayLog> logs, DateTimeOffset now, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(settings);

        var today = WorkDay.DateOf(now);
        var start = WeekStart(today);
        var byDate = logs.Where(log => log is not null).ToDictionary(log => log.Date, log => log);

        var summaries = new List<DaySummary>(7);
        var worked = TimeSpan.Zero;
        var norm = TimeSpan.Zero;
        var normSoFar = TimeSpan.Zero;

        for (var date = start; date < start.AddDays(7); date = date.AddDays(1))
        {
            var log = byDate.TryGetValue(date, out var found) ? found : DayLog.Empty(date);

            // У прожитого дня цель своя, сохранённая при его создании; у будущего — нынешняя.
            var summary = WorkTimeCalculator.Compute(log, now, settings.RulesFor(date));
            summaries.Add(summary);

            worked += summary.Worked;
            norm += summary.Goal;
            if (date <= today)
            {
                normSoFar += summary.Goal;
            }
        }

        return new WeekSummary(start, worked, norm, normSoFar, summaries);
    }

    /// <summary>В выборке есть дни, посчитанные по разным правилам.</summary>
    public static bool HasMixedRules(IEnumerable<DaySummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var counted = summaries.Where(summary => summary.State != WorkState.NotStarted).ToList();
        return counted.Count > 0
            && counted.Any(s => s.CountsShortBreaks)
            && counted.Any(s => !s.CountsShortBreaks);
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