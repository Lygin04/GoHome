using GoHome.Core;

namespace GoHome.Ui.Design;

/// <summary>Чем занят отрезок полосы дня.</summary>
internal enum BandKind
{
    /// <summary>Работа.</summary>
    Work,

    /// <summary>Отлучка, идущая в рабочее время.</summary>
    PaidBreak,

    /// <summary>Отлучка, не идущая в рабочее время: обед или отрезанная блокировка.</summary>
    UnpaidBreak,
}

/// <summary>Отрезок полосы дня.</summary>
/// <param name="Start">Начало.</param>
/// <param name="End">Конец.</param>
/// <param name="Kind">Чем занят.</param>
internal readonly record struct BandSegment(DateTimeOffset Start, DateTimeOffset End, BandKind Kind)
{
    /// <summary>Длительность отрезка.</summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}

/// <summary>
/// Полоса дня: во что превращается сводка, прежде чем её нарисовать.
/// </summary>
/// <remarks>
/// Отдельно от рисования, потому что вся сложность здесь, а не в заливке прямоугольников:
/// границы диапазона, разрезание дня отлучками, метки часов и переход через полночь.
/// Проверять это на картинке нельзя, а на числах — можно.
/// <para>
/// Рабочие сутки сдвинуты (<see cref="WorkDay.StartHour"/>), поэтому работа до часу ночи
/// принадлежит вчерашней дате. В абсолютном времени такой день — обычный непрерывный
/// отрезок, и полоса рисует его слева направо без разрывов; календарная полночь внутри
/// него ничем не примечательна.
/// </para>
/// </remarks>
/// <param name="From">Левый край полосы.</param>
/// <param name="To">Правый край полосы.</param>
/// <param name="Segments">Отрезки по порядку, без пропусков между приходом и концом.</param>
/// <param name="Now">Отметка текущего момента. Есть только у сегодняшнего дня.</param>
/// <param name="Goal">Момент, когда норма будет закрыта. Нет у нерабочего и у закрытого дня.</param>
/// <param name="Ticks">Метки часов на оси.</param>
internal sealed record DayBand(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<BandSegment> Segments,
    DateTimeOffset? Now,
    DateTimeOffset? Goal,
    IReadOnlyList<DateTimeOffset> Ticks)
{
    /// <summary>
    /// Наименьшая ширина полосы во времени.
    /// </summary>
    /// <remarks>
    /// Полчаса работы, растянутые на всю ширину окна, читаются как полный день — это враньё.
    /// Четыре часа дают привычный масштаб, в котором получасовой день выглядит получасовым.
    /// </remarks>
    private static readonly TimeSpan MinimumSpan = TimeSpan.FromHours(4);

    /// <summary>Полоса пустая: показывать нечего.</summary>
    public bool IsEmpty => Segments.Count == 0;

    /// <summary>Ширина полосы во времени.</summary>
    public TimeSpan Span => To > From ? To - From : TimeSpan.Zero;

    /// <summary>
    /// Строит полосу по сводке дня.
    /// </summary>
    /// <param name="day">Сводка.</param>
    /// <param name="now">Текущий момент.</param>
    /// <param name="tickStep">Через сколько часов ставить метки: 2 на широком окне, 3 на узком.</param>
    public static DayBand For(DaySummary day, DateTimeOffset now, int tickStep)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentOutOfRangeException.ThrowIfLessThan(tickStep, 1);

        var offset = now.Offset;
        var dayStart = WorkDay.StartOf(day.Date, offset);
        var dayEnd = WorkDay.EndOf(day.Date, offset);
        var isToday = WorkDay.DateOf(now) == day.Date;

        if (day.ArrivedAt is not { } arrived)
        {
            // День без прихода: утро до первого события, выходной, день без данных. Полоса
            // растягивается на рабочие сутки целиком — показывать нечего, и сужать диапазон
            // не к чему; выдумывать «обычные рабочие часы» тем более незачем.
            return new DayBand(dayStart, dayEnd, [], null, null, TicksBetween(dayStart, dayEnd, tickStep));
        }

        var last = day.LeftAt ?? (isToday ? now : LastKnownMoment(day, arrived));

        // Прогноз ухода и отметка «сейчас» имеют смысл только сегодня: у закрытого дня
        // ни того ни другого быть не должно — время в нём больше не идёт.
        var goal = isToday && !day.IsDayOff && day.LeftAt is null ? day.ProjectedEnd : null;
        var marker = isToday && day.LeftAt is null ? now : (DateTimeOffset?)null;

        var right = Later(last, goal ?? last);
        var from = FloorToHour(arrived);
        var to = CeilingToHour(right);

        if (to - from < MinimumSpan)
        {
            to = from + MinimumSpan;
        }

        // За пределы рабочих суток полоса не выходит: день начинается в четыре утра
        // и заканчивается в четыре утра следующих.
        from = from < dayStart ? dayStart : from;
        to = to > dayEnd ? dayEnd : to;

        return new DayBand(
            from,
            to,
            Cut(arrived, last, day.Intervals),
            Between(marker, from, to),
            Between(goal, from, to),
            TicksBetween(from, to, tickStep));
    }

    /// <summary>Доля от левого края: 0 — начало полосы, 1 — конец.</summary>
    public double Fraction(DateTimeOffset at)
    {
        var span = Span;
        return span <= TimeSpan.Zero ? 0d : Math.Clamp((at - From) / span, 0d, 1d);
    }

    /// <summary>Разрезает день от прихода до конца отлучками, не оставляя дыр.</summary>
    private static List<BandSegment> Cut(
        DateTimeOffset arrived,
        DateTimeOffset last,
        IReadOnlyList<BreakInterval> intervals)
    {
        var segments = new List<BandSegment>();
        var cursor = arrived;

        foreach (var interval in intervals.OrderBy(interval => interval.Start))
        {
            // Отлучка целиком за пределами дня — след правки файла руками. Пропускаем:
            // рисовать её негде, а ронять из-за неё показ дня незачем.
            if (interval.End <= cursor || interval.Start >= last)
            {
                continue;
            }

            var start = Later(interval.Start, cursor);
            var end = Earlier(interval.End, last);

            if (start > cursor)
            {
                segments.Add(new BandSegment(cursor, start, BandKind.Work));
            }

            if (end > start)
            {
                segments.Add(new BandSegment(
                    start,
                    end,
                    interval.IsUnpaid ? BandKind.UnpaidBreak : BandKind.PaidBreak));
            }

            cursor = Later(cursor, end);
        }

        if (last > cursor)
        {
            segments.Add(new BandSegment(cursor, last, BandKind.Work));
        }

        return segments;
    }

    /// <summary>Метки часов внутри диапазона, кратные шагу.</summary>
    private static List<DateTimeOffset> TicksBetween(DateTimeOffset from, DateTimeOffset to, int step)
    {
        var ticks = new List<DateTimeOffset>();
        var first = CeilingToHour(from);

        for (var at = first; at <= to; at = at.AddHours(1))
        {
            if (at.Hour % step == 0)
            {
                ticks.Add(at);
            }
        }

        return ticks;
    }

    /// <summary>
    /// Последний известный момент закрытого дня без отметки ухода.
    /// </summary>
    /// <remarks>
    /// Такой день бывает после падения или выключения питания: приход записан, уход нет.
    /// Тянуть полосу до текущего момента нельзя — это нарисовало бы работу, которой не было.
    /// </remarks>
    private static DateTimeOffset LastKnownMoment(DaySummary day, DateTimeOffset arrived)
    {
        var last = arrived;
        foreach (var interval in day.Intervals)
        {
            last = Later(last, interval.End);
        }

        return last;
    }

    private static DateTimeOffset? Between(DateTimeOffset? at, DateTimeOffset from, DateTimeOffset to) =>
        at is { } moment && moment >= from && moment <= to ? moment : null;

    private static DateTimeOffset FloorToHour(DateTimeOffset at) =>
        new(at.Year, at.Month, at.Day, at.Hour, 0, 0, at.Offset);

    private static DateTimeOffset CeilingToHour(DateTimeOffset at)
    {
        var floor = FloorToHour(at);
        return floor == at ? floor : floor.AddHours(1);
    }

    private static DateTimeOffset Later(DateTimeOffset one, DateTimeOffset other) =>
        one > other ? one : other;

    private static DateTimeOffset Earlier(DateTimeOffset one, DateTimeOffset other) =>
        one < other ? one : other;
}
