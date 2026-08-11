namespace GoHome.Core;

/// <summary>
/// Когда отлучка может оказаться обедом.
/// </summary>
/// <remarks>
/// Различить обед и переговорку по событиям блокировки невозможно — этой информации в машине
/// нет. Поэтому правила подобраны так, чтобы неоднозначный случай за день остался ровно один:
/// всё короткое и всё вне окна уходит в зачёт молча, без единого вопроса к человеку.
/// </remarks>
/// <param name="WindowStart">Начало обеденного окна.</param>
/// <param name="WindowEnd">Конец обеденного окна.</param>
/// <param name="Minimum">
/// Короче этого отлучка не считается отлучкой вовсе: чай, вопрос коллеге, подпись бумаги.
/// Такие интервалы не участвуют в догадке и не показываются в истории.
/// </param>
/// <param name="GuessMinimum">
/// Короче этого догадка не срабатывает. Пятнадцать минут в час дня — не обед,
/// и уведомлять о них не о чем.
/// </param>
public sealed record LunchRules(
    TimeOnly WindowStart,
    TimeOnly WindowEnd,
    TimeSpan Minimum,
    TimeSpan GuessMinimum)
{
    /// <summary>Правила по умолчанию.</summary>
    public static readonly LunchRules Default = new(
        new TimeOnly(11, 30),
        new TimeOnly(15, 0),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(25));

    /// <summary>Отлучка началась внутри обеденного окна.</summary>
    public bool InWindow(DateTimeOffset start)
    {
        if (WindowEnd <= WindowStart)
        {
            // Пустое или вывернутое окно: обед не угадывается вообще.
            return false;
        }

        var time = TimeOnly.FromDateTime(start.DateTime);
        return time >= WindowStart && time < WindowEnd;
    }

    /// <summary>Отлучка достаточно длинная, чтобы вообще попасть в историю.</summary>
    public bool IsSignificant(TimeSpan duration) => duration >= Minimum;

    /// <summary>Отлучка может оказаться обедом: и по времени, и по длительности.</summary>
    public bool CouldBeLunch(BreakInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return interval.Duration >= GuessMinimum && InWindow(interval.Start);
    }
}
