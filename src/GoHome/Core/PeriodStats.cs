namespace GoHome.Core;

/// <summary>
/// Итог периода: то, ради чего статистику вообще открывают.
/// </summary>
/// <remarks>
/// Считается только для показа и выгрузки: ни на цели дней, ни на уведомления не влияет.
/// </remarks>
/// <param name="Range">Период.</param>
/// <param name="Days">Все дни периода по порядку, включая те, в которые никто не приходил.</param>
/// <param name="Worked">Отработано за период, включая нерабочие дни.</param>
/// <param name="Norm">Норма периода — сумма целей его дней, включая ещё не наступившие.</param>
/// <param name="NormSoFar">Сумма целей дней, которые уже прошли, включая сегодняшний.</param>
/// <param name="WorkedDays">В скольких днях есть отработанное время.</param>
/// <param name="DaysOff">Сколько в периоде нерабочих дней.</param>
/// <param name="Unreadable">Сколько файлов дней не прочиталось — данные за период неполные.</param>
/// <param name="Arrival">Обычное время прихода — среднее по дням, в которые приходили.</param>
/// <param name="Departure">Обычное время ухода — среднее по закрытым дням.</param>
/// <param name="DayLength">Средняя продолжительность дня по дням, в которые работали.</param>
/// <param name="Unpaid">Средняя длительность обеда — того, что не пошло в зачёт.</param>
public sealed record PeriodStats(
    PeriodRange Range,
    IReadOnlyList<DaySummary> Days,
    TimeSpan Worked,
    TimeSpan Norm,
    TimeSpan NormSoFar,
    int WorkedDays,
    int DaysOff,
    int Unreadable,
    TimeSpan? Arrival,
    TimeSpan? Departure,
    TimeSpan? DayLength,
    TimeSpan? Unpaid)
{
    /// <summary>Пустой период: считать нечего, но показать его всё равно надо.</summary>
    public static PeriodStats Empty(PeriodRange range) =>
        new(range, [], TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, null, null, null, null);

    /// <summary>
    /// Накопленная разница по уже прожитым дням.
    /// </summary>
    /// <remarks>
    /// Считать её от полной нормы периода нельзя: первого числа это всегда «минус целый
    /// месяц», и смотреть на такое бессмысленно. То же решение, что и в недельном балансе.
    /// </remarks>
    public TimeSpan Balance => Worked - NormSoFar;

    /// <summary>В периоде не работали ни дня.</summary>
    public bool IsEmpty => WorkedDays == 0;

    /// <summary>Часть файлов не прочиталась, и числа неполные.</summary>
    public bool HasGaps => Unreadable > 0;

    /// <summary>Самая высокая величина периода — и отработанное, и цели: по ней масштабируются столбцы.</summary>
    public TimeSpan Peak
    {
        get
        {
            var peak = TimeSpan.Zero;
            foreach (var day in Days)
            {
                if (day.Worked > peak)
                {
                    peak = day.Worked;
                }

                if (day.Goal > peak)
                {
                    peak = day.Goal;
                }
            }

            return peak;
        }
    }
}
