namespace GoHome.Core;

/// <summary>
/// Итог недели. Только показывается: на дневные цели не влияет и между неделями
/// не переносится — перенос это уже учёт, а не трекер.
/// </summary>
/// <param name="Start">Понедельник недели.</param>
/// <param name="Worked">Отработано за неделю.</param>
/// <param name="Norm">Норма недели — сумма целей всех её дней, включая ещё не наступившие.</param>
/// <param name="NormSoFar">Сумма целей дней, которые уже прошли, включая сегодняшний.</param>
/// <param name="Days">Дни недели с понедельника, каждый со своей целью.</param>
public sealed record WeekSummary(
    DateOnly Start,
    TimeSpan Worked,
    TimeSpan Norm,
    TimeSpan NormSoFar,
    IReadOnlyList<DaySummary> Days)
{
    /// <summary>Воскресенье недели.</summary>
    public DateOnly End => Start.AddDays(6);

    /// <summary>
    /// Накопленная разница по уже прожитым дням. Считать её от полной недельной нормы
    /// нельзя: в понедельник утром это всегда «минус целая неделя», и смотреть на такое
    /// бессмысленно.
    /// </summary>
    public TimeSpan Balance => Worked - NormSoFar;

    /// <summary>Сколько в неделе рабочих дней.</summary>
    public int WorkingDays => Days.Count(day => !day.IsDayOff);
}