using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Правка отлучки в журнале.
/// </summary>
/// <remarks>
/// В отличие от смены зачёта, здесь двигаются сами отметки, и недопустимое значение
/// нельзя записать «почти»: журнал либо остаётся прежним, либо становится осмысленным.
/// </remarks>
public sealed class BreakEditTests
{
    /// <summary>Конец раньше начала не сохраняется и объясняет причину.</summary>
    [Fact]
    public void EndBeforeStartIsRefused()
    {
        var log = Day();

        var refusal = BreakEdit.Reject(log, At(13), At(13), At(12, 40));

        Assert.NotNull(refusal);
        Assert.Contains("раньше начала", refusal, StringComparison.Ordinal);
    }

    /// <summary>Нулевая длительность — тоже не интервал.</summary>
    [Fact]
    public void ZeroLengthIsRefused()
    {
        Assert.NotNull(BreakEdit.Reject(Day(), At(13), At(13), At(13)));
    }

    /// <summary>Наезд на соседний перерыв не сохраняется и называет соседа.</summary>
    [Fact]
    public void OverlapWithNeighbourIsRefused()
    {
        var log = Day();

        var refusal = BreakEdit.Reject(log, At(13), At(10, 30), At(13, 45));

        Assert.NotNull(refusal);
        Assert.Contains("наезжает", refusal, StringComparison.Ordinal);
    }

    /// <summary>Касание соседа краем наездом не считается: конец одного — начало другого.</summary>
    [Fact]
    public void TouchingANeighbourIsAllowed()
    {
        Assert.Null(BreakEdit.Reject(Day(), At(13), At(11, 15), At(13, 45)));
    }

    /// <summary>За пределы рабочих суток время не уезжает.</summary>
    [Fact]
    public void TimeOutsideTheWorkDayIsRefused()
    {
        var refusal = BreakEdit.Reject(Day(), At(13), At(13), At(5, 0, 1));

        Assert.NotNull(refusal);
        Assert.Contains("соседний день", refusal, StringComparison.Ordinal);
    }

    /// <summary>Перерыв не начинается раньше прихода и не кончается после ухода.</summary>
    [Theory]
    [InlineData(8, 0, 8, 30, "раньше прихода")]
    [InlineData(17, 0, 19, 0, "после ухода")]
    public void BreakStaysInsideTheDay(int fromHour, int fromMinute, int toHour, int toMinute, string because)
    {
        var log = Day(closed: true);

        var refusal = BreakEdit.Reject(log, At(13), At(fromHour, fromMinute), At(toHour, toMinute));

        Assert.NotNull(refusal);
        Assert.Contains(because, refusal, StringComparison.Ordinal);
    }

    /// <summary>Незакрытый перерыв править нечем: он ещё идёт.</summary>
    [Fact]
    public void OpenBreakCannotBeEdited()
    {
        var log = Log(In(9), BreakStart(13));

        Assert.NotNull(BreakEdit.Reject(log, At(13), At(13), At(13, 30)));
        Assert.False(BreakEdit.Move(log, At(13), At(13), At(13, 30)));
        Assert.False(BreakEdit.Remove(log, At(13)));
    }

    /// <summary>Сдвиг границ меняет обе отметки и помечает их ручными.</summary>
    [Fact]
    public void MoveShiftsBothPunchesAndMarksThem()
    {
        var log = Day();

        Assert.True(BreakEdit.Move(log, At(13), At(12, 50), At(13, 40)));

        var start = Assert.Single(log.Punches, punch => punch.Kind == PunchKind.BreakStart && punch.At == At(12, 50));
        var end = Assert.Single(log.Punches, punch => punch.Kind == PunchKind.BreakEnd && punch.At == At(13, 40));

        Assert.Equal(BreakEdit.ManualSource, start.Source);
        Assert.Equal(BreakEdit.ManualSource, end.Source);

        // Отметки остаются упорядоченными: журнал читают и глазами тоже.
        Assert.Equal(log.Punches.OrderBy(punch => punch.At), log.Punches);
    }

    /// <summary>
    /// Сдвиг начала уносит с собой поправку зачёта. Иначе снятая пометка обеда молча
    /// вернулась бы на следующем расчёте — автоматика оказалась бы сильнее человека.
    /// </summary>
    [Fact]
    public void MoveCarriesTheAdjustmentAlong()
    {
        var log = Day().With(Paid(13));

        Assert.True(BreakEdit.Move(log, At(13), At(12, 50), At(13, 40)));

        var adjustment = Assert.Single(log.Adjustments!);
        Assert.Equal(At(12, 50), adjustment.BreakAt);
        Assert.Equal(BreakKind.Paid, adjustment.Kind);
    }

    /// <summary>Удаление убирает обе отметки: время до и после смыкается в работу.</summary>
    [Fact]
    public void RemoveTakesBothPunches()
    {
        var log = Day();

        Assert.True(BreakEdit.Remove(log, At(13)));

        Assert.DoesNotContain(log.Punches, punch => punch.At == At(13));
        Assert.DoesNotContain(log.Punches, punch => punch.At == At(13, 45));

        // Второй перерыв на месте: удалили один, а не все.
        Assert.Contains(log.Punches, punch => punch.At == At(11));
    }

    /// <summary>Поправка удалённого перерыва не остаётся мусором в файле.</summary>
    [Fact]
    public void RemoveDropsTheOrphanedAdjustment()
    {
        var log = Day().With(Paid(13), Unpaid(11));

        Assert.True(BreakEdit.Remove(log, At(13)));

        var left = Assert.Single(log.Adjustments!);
        Assert.Equal(At(11), left.BreakAt);
    }

    /// <summary>Правка чужого перерыва журнал не трогает.</summary>
    [Fact]
    public void UnknownBreakChangesNothing()
    {
        var log = Day();
        var before = log.Punches.Count;

        Assert.False(BreakEdit.Move(log, At(15), At(15), At(15, 30)));
        Assert.False(BreakEdit.Remove(log, At(15)));
        Assert.Equal(before, log.Punches.Count);
    }

    /// <summary>Допустимая правка проходит проверку.</summary>
    [Fact]
    public void ReasonableEditIsAccepted()
    {
        Assert.Null(BreakEdit.Reject(Day(), At(13), At(12, 30), At(13, 20)));
    }

    /// <summary>День с двумя перерывами: 11:00–11:15 и 13:00–13:45.</summary>
    private static DayLog Day(bool closed = false)
    {
        var punches = new List<Punch>
        {
            In(9),
            BreakStart(11),
            BreakEnd(11, 15),
            BreakStart(13),
            BreakEnd(13, 45),
        };

        if (closed)
        {
            punches.Add(Out(18));
        }

        return Fresh([.. punches]);
    }
}
