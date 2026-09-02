namespace GoHome.Core;

/// <summary>
/// Правка отлучки в журнале: сдвинуть границы или удалить целиком.
/// </summary>
/// <remarks>
/// В отличие от смены зачёта, это правка самого журнала: <see cref="BreakAdjustment"/>
/// ложится рядом с отметками и ничего в них не меняет, а здесь двигаются сами отметки.
/// Поэтому изменённые отметки помечаются источником — иначе позже нельзя будет понять,
/// откуда взялось время, не совпадающее с тем, что происходило на самом деле.
/// <para>
/// Проверка отделена от применения намеренно: форме нужно объяснить, почему значение
/// не годится, ещё до попытки сохранить, и объяснение должно быть тем же самым.
/// </para>
/// </remarks>
public static class BreakEdit
{
    /// <summary>Источник отметки, поставленной человеком через форму дня.</summary>
    public const string ManualSource = "manual";

    /// <summary>
    /// Что мешает задать отлучке такие границы. <c>null</c> — ничего не мешает.
    /// </summary>
    /// <param name="log">Журнал дня.</param>
    /// <param name="breakAt">Начало правимой отлучки — по нему она и находится.</param>
    /// <param name="start">Новое начало.</param>
    /// <param name="end">Новый конец.</param>
    public static string? Reject(DayLog log, DateTimeOffset breakAt, DateTimeOffset start, DateTimeOffset end)
    {
        ArgumentNullException.ThrowIfNull(log);

        var (opening, closing) = Pair(log, breakAt);
        if (opening is null || closing is null)
        {
            return "Этот перерыв не найден или ещё не закончился.";
        }

        if (end <= start)
        {
            return "Конец перерыва раньше начала.";
        }

        var dayStart = WorkDay.StartOf(log.Date, start.Offset);
        var dayEnd = WorkDay.EndOf(log.Date, start.Offset);

        if (start < dayStart || end > dayEnd)
        {
            return "Время уходит в соседний день.";
        }

        var arrived = log.Punches.FirstOrDefault(punch => punch.Kind == PunchKind.In);
        if (arrived is not null && start < arrived.At)
        {
            return "Перерыв не может начаться раньше прихода.";
        }

        var left = log.Punches.FirstOrDefault(punch => punch.Kind == PunchKind.Out);
        if (left is not null && end > left.At)
        {
            return "Перерыв не может закончиться после ухода.";
        }

        foreach (var (otherStart, otherEnd) in Others(log, breakAt))
        {
            if (start < otherEnd && otherStart < end)
            {
                return $"Перерыв наезжает на соседний — {WorkTimeFormat.Clock(otherStart)}–{WorkTimeFormat.Clock(otherEnd)}.";
            }
        }

        return null;
    }

    /// <summary>
    /// Сдвигает границы отлучки.
    /// </summary>
    /// <returns><c>true</c>, если журнал изменился.</returns>
    /// <remarks>Ничего не проверяет — проверку делает <see cref="Reject"/> до вызова.</remarks>
    public static bool Move(DayLog log, DateTimeOffset breakAt, DateTimeOffset start, DateTimeOffset end)
    {
        ArgumentNullException.ThrowIfNull(log);

        var (opening, closing) = Pair(log, breakAt);
        if (opening is null || closing is null)
        {
            return false;
        }

        Replace(log, opening, opening with { At = start.ToWholeSecond(), Source = ManualSource });
        Replace(log, closing, closing with { At = end.ToWholeSecond(), Source = ManualSource });

        // Поправка привязана к началу отлучки. Сдвинули начало — переносим и её,
        // иначе снятая пометка обеда молча вернётся на следующем расчёте.
        var adjustments = log.Adjustments;
        for (var index = 0; index < adjustments?.Count; index++)
        {
            if (adjustments[index] is { } adjustment && adjustment.BreakAt.UtcTicks == breakAt.UtcTicks)
            {
                adjustments[index] = adjustment with { BreakAt = start.ToWholeSecond() };
                break;
            }
        }

        log.Punches.Sort((one, other) => one.At.CompareTo(other.At));
        return true;
    }

    /// <summary>
    /// Убирает отлучку из журнала целиком: время до и после неё смыкается в работу.
    /// </summary>
    /// <returns>
    /// Убранное — отметки и поправка — или <c>null</c>, если убирать было нечего.
    /// По этому же значению удаление и отменяется.
    /// </returns>
    public static RemovedBreak? Remove(DayLog log, DateTimeOffset breakAt)
    {
        ArgumentNullException.ThrowIfNull(log);

        var (opening, closing) = Pair(log, breakAt);
        if (opening is null || closing is null)
        {
            return null;
        }

        log.Punches.Remove(opening);
        log.Punches.Remove(closing);

        // Поправка к удалённой отлучке указывает в пустоту. Расчёт её и так игнорирует,
        // но копить мусор в файле, который человек читает глазами, незачем.
        var adjustment = log.Adjustments?
            .FirstOrDefault(item => item is not null && item.BreakAt.UtcTicks == breakAt.UtcTicks);

        log.Adjustments?.RemoveAll(
            item => item is null || item.BreakAt.UtcTicks == breakAt.UtcTicks);

        return new RemovedBreak(opening, closing, adjustment);
    }

    /// <summary>
    /// Возвращает в журнал ровно то, что убрало <see cref="Remove"/>.
    /// </summary>
    /// <remarks>
    /// Это откат конкретного удаления, а не создание отлучки: вернуть можно только те
    /// отметки, которые сами же и убрали. Общей операции «добавить перерыв» здесь нет
    /// намеренно — ею можно было бы нарисовать перерыв, которого не было.
    /// </remarks>
    /// <returns><c>true</c>, если журнал изменился.</returns>
    public static bool Restore(DayLog log, RemovedBreak removed)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(removed);

        // Место могли занять, пока отлучки не было: служба дописывает в тот же файл,
        // да и руками его правят. Тогда возвращать некуда.
        if (Overlaps(log, removed))
        {
            return false;
        }

        log.Punches.Add(removed.Opening);
        log.Punches.Add(removed.Closing);
        log.Punches.Sort((one, other) => one.At.CompareTo(other.At));

        if (removed.Adjustment is { } adjustment)
        {
            log.Adjustments ??= [];
            log.Adjustments.Add(adjustment);
        }

        return true;
    }

    /// <summary>Занято ли место, на которое возвращается отлучка.</summary>
    private static bool Overlaps(DayLog log, RemovedBreak removed)
    {
        foreach (var (start, end) in Others(log, removed.Opening.At))
        {
            if (removed.Opening.At < end && start < removed.Closing.At)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Отметки начала и конца отлучки. Незакрытая отлучка даёт конец <c>null</c>.</summary>
    private static (Punch? Start, Punch? End) Pair(DayLog log, DateTimeOffset breakAt)
    {
        var ordered = log.Punches.OrderBy(punch => punch.At).ToList();
        var index = ordered.FindIndex(
            punch => punch.Kind == PunchKind.BreakStart && punch.At.UtcTicks == breakAt.UtcTicks);

        if (index < 0)
        {
            return (null, null);
        }

        var start = ordered[index];
        var end = ordered.Skip(index + 1).FirstOrDefault(punch => punch.Kind == PunchKind.BreakEnd);

        return (start, end);
    }

    /// <summary>Границы остальных закрытых отлучек дня.</summary>
    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Others(
        DayLog log,
        DateTimeOffset breakAt)
    {
        var ordered = log.Punches.OrderBy(punch => punch.At).ToList();
        Punch? open = null;

        foreach (var punch in ordered)
        {
            if (punch.Kind == PunchKind.BreakStart)
            {
                open = punch;
            }
            else if (punch.Kind == PunchKind.BreakEnd && open is not null)
            {
                if (open.At.UtcTicks != breakAt.UtcTicks)
                {
                    yield return (open.At, punch.At);
                }

                open = null;
            }
        }
    }

    private static void Replace(DayLog log, Punch old, Punch fresh)
    {
        var index = log.Punches.IndexOf(old);
        if (index >= 0)
        {
            log.Punches[index] = fresh;
        }
    }
}
