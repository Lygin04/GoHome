using System.Globalization;

namespace GoHome.Core;

/// <summary>Период, за который смотрят статистику.</summary>
public enum StatsPeriod
{
    /// <summary>Календарная неделя с понедельника.</summary>
    Week,

    /// <summary>Календарный месяц.</summary>
    Month,

    /// <summary>Календарный год.</summary>
    Year,
}

/// <summary>
/// Отрезок рабочих дат, за который считается статистика.
/// </summary>
/// <remarks>
/// Даты рабочие, со сдвигом <see cref="WorkDay.StartHour"/>: работа с воскресенья 22:00
/// до понедельника 01:00 относится к воскресенью — и попадает в ту неделю, которая
/// воскресеньем заканчивается, а не в следующую.
/// </remarks>
/// <param name="Period">Какого рода период.</param>
/// <param name="Start">Первая дата, включительно.</param>
/// <param name="End">Последняя дата, включительно.</param>
public readonly record struct PeriodRange(StatsPeriod Period, DateOnly Start, DateOnly End)
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Период, которому принадлежит рабочая дата.</summary>
    public static PeriodRange Of(StatsPeriod period, DateOnly date) => period switch
    {
        StatsPeriod.Month => new PeriodRange(
            period,
            new DateOnly(date.Year, date.Month, 1),
            new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))),
        StatsPeriod.Year => new PeriodRange(period, new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31)),
        _ => OfWeek(date),
    };

    /// <summary>Сколько дней в периоде.</summary>
    public int Length => End.DayNumber - Start.DayNumber + 1;

    /// <summary>Все даты периода по порядку.</summary>
    public IEnumerable<DateOnly> Dates
    {
        get
        {
            for (var date = Start; date <= End; date = date.AddDays(1))
            {
                yield return date;
            }
        }
    }

    /// <summary>Название периода — заголовку окна и выгрузке.</summary>
    public string Title => Period switch
    {
        StatsPeriod.Month => Capitalize(Start.ToString("MMMM yyyy", Russian)),
        StatsPeriod.Year => Start.Year.ToString(CultureInfo.InvariantCulture) + " год",
        _ => WeekTitle(),
    };

    /// <summary>Дата принадлежит периоду.</summary>
    public bool Contains(DateOnly date) => date >= Start && date <= End;

    /// <summary>Соседний период: <c>-1</c> — предыдущий, <c>+1</c> — следующий.</summary>
    /// <remarks>
    /// Считается от начала периода, а не прибавлением его длины: месяцы разной длины,
    /// и «плюс тридцать дней» от тридцать первого января приводит в март.
    /// </remarks>
    public PeriodRange Shift(int steps) => Period switch
    {
        StatsPeriod.Month => Of(Period, Start.AddMonths(steps)),
        StatsPeriod.Year => Of(Period, Start.AddYears(steps)),
        _ => Of(Period, Start.AddDays(7 * steps)),
    };

    private static PeriodRange OfWeek(DateOnly date)
    {
        var start = HistoryCalculator.WeekStart(date);
        return new PeriodRange(StatsPeriod.Week, start, start.AddDays(6));
    }

    /// <summary>Неделя может лежать на границе месяцев и даже лет — тогда подпись длиннее.</summary>
    private string WeekTitle()
    {
        if (Start.Year != End.Year)
        {
            return $"{Start.ToString("d MMMM yyyy", Russian)} — {End.ToString("d MMMM yyyy", Russian)}";
        }

        return Start.Month == End.Month
            ? $"{Start.Day}–{End.ToString("d MMMM yyyy", Russian)}"
            : $"{Start.ToString("d MMMM", Russian)} — {End.ToString("d MMMM yyyy", Russian)}";
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Russian) + text[1..];
}
