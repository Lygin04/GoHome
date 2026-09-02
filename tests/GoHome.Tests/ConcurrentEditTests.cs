using GoHome.App;
using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Правка дня, который прямо сейчас дописывает служба.
/// </summary>
/// <remarks>
/// Форма и служба работают через одно хранилище с одним замком. Проверка и запись правки
/// идут под этим замком на свежепрочитанном журнале — иначе форма записала бы устаревшую
/// копию поверх того, что служба успела добавить.
/// <para>
/// Проверить это глазами нельзя: расхождение появляется только на настоящем совпадении
/// по времени, и заметно оно потерянными минутами, а не сообщением об ошибке.
/// </para>
/// </remarks>
public sealed class ConcurrentEditTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "gohome-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Правка не теряет отметку, которую служба записала между чтением и записью.</summary>
    [Fact]
    public void EditKeepsWhatTheServiceWroteMeanwhile()
    {
        var service = Service();

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(13, 45), "unlock");

        // Форма прочитала день и показывает его человеку.
        var shown = service.OpenDay(Today, At(14));
        var lunch = Assert.Single(shown.Summary.Intervals);

        // Пока человек набирал время, служба записала ещё одну отлучку.
        service.RecordPause(At(15), TimeSpan.Zero, "lock");
        service.RecordReturn(At(15, 30), "unlock");

        Assert.Null(service.MoveBreak(Today, lunch.Start, At(12, 50), At(13, 40)));

        var after = service.OpenDay(Today, At(16)).Summary;

        // Обе отлучки на месте: правка легла на журнал, а не заменила его собой.
        Assert.Equal(2, after.Intervals.Count);
        Assert.Contains(after.Intervals, interval => interval.Start == At(12, 50));
        Assert.Contains(after.Intervals, interval => interval.Start == At(15));
    }

    /// <summary>
    /// Отказ решается на журнале, прочитанном под замком: значение, годное на глаз формы,
    /// может перестать годиться к моменту записи.
    /// </summary>
    [Fact]
    public void RefusalIsDecidedOnTheFreshJournal()
    {
        var service = Service();

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(13, 45), "unlock");

        var lunch = Assert.Single(service.OpenDay(Today, At(14)).Summary.Intervals);

        // Пока человек тянул конец обеда к 15:30, служба записала там отлучку.
        Assert.Null(service.CheckBreak(Today, lunch.Start, At(13), At(15, 30)));

        service.RecordPause(At(15), TimeSpan.Zero, "lock");
        service.RecordReturn(At(15, 30), "unlock");

        var refusal = service.MoveBreak(Today, lunch.Start, At(13), At(15, 30));

        Assert.NotNull(refusal);
        Assert.Contains("наезжает", refusal, StringComparison.Ordinal);

        // Журнал при этом не тронут: отказ — это отказ, а не половина правки.
        var after = service.OpenDay(Today, At(16)).Summary;
        Assert.Contains(after.Intervals, interval => interval.Start == At(13) && interval.End == At(13, 45));
    }

    /// <summary>
    /// Ручная правка сильнее автоматики: снятая пометка обеда не возвращается на следующем
    /// расчёте, даже после сдвига границ.
    /// </summary>
    [Fact]
    public void ManualDecisionSurvivesTheGuess()
    {
        var service = Service();

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(13, 45), "unlock");

        var guessed = Assert.Single(service.OpenDay(Today, At(14)).Summary.Intervals);
        Assert.True(guessed.IsUnpaid);
        Assert.True(guessed.Guessed);

        // Человек говорит: это был не обед.
        Assert.True(service.Reclassify(Today, guessed.Start, BreakKind.Paid, "day-form"));
        Assert.False(Assert.Single(service.OpenDay(Today, At(14)).Summary.Intervals).IsUnpaid);

        // И двигает границы. Поправка уезжает вместе с началом, а не остаётся висеть.
        Assert.Null(service.MoveBreak(Today, guessed.Start, At(12, 50), At(13, 40)));

        var after = Assert.Single(service.OpenDay(Today, At(17)).Summary.Intervals);

        Assert.Equal(At(12, 50), after.Start);
        Assert.False(after.IsUnpaid);
    }

    /// <summary>Правка помечает отметки ручными — чтобы позже было видно происхождение времени.</summary>
    [Fact]
    public void EditedPunchesAreMarked()
    {
        var service = Service();

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(13, 45), "unlock");

        var lunch = Assert.Single(service.OpenDay(Today, At(14)).Summary.Intervals);
        Assert.Empty(service.OpenDay(Today, At(14)).Edited);

        Assert.Null(service.MoveBreak(Today, lunch.Start, At(12, 50), At(13, 40)));

        var edited = service.OpenDay(Today, At(14)).Edited;
        Assert.Equal(At(12, 50), Assert.Single(edited));
    }

    /// <summary>Удаление смыкает работу вокруг убранного перерыва.</summary>
    [Fact]
    public void RemovingABreakClosesTheGap()
    {
        var service = Service();

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(13, 45), "unlock");
        service.RecordPause(At(18), TimeSpan.Zero, "lock");

        var before = service.OpenDay(Today, At(19)).Summary;
        var lunch = Assert.Single(before.Intervals);

        Assert.NotNull(service.RemoveBreak(Today, lunch.Start));

        var after = service.OpenDay(Today, At(19)).Summary;

        Assert.Empty(after.Intervals);
        Assert.True(after.Worked > before.Worked);
    }

    /// <summary>Отмена удаления возвращает ровно те отметки, которые были убраны.</summary>
    [Fact]
    public void RemovalIsUndoneThroughTheService()
    {
        var service = Service();

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(13, 45), "unlock");
        service.RecordPause(At(18), TimeSpan.Zero, "lock");

        var lunch = Assert.Single(service.OpenDay(Today, At(19)).Summary.Intervals);

        var removed = service.RemoveBreak(Today, lunch.Start);
        Assert.NotNull(removed);
        Assert.Empty(service.OpenDay(Today, At(19)).Summary.Intervals);

        Assert.True(service.RestoreBreak(Today, removed));

        var back = Assert.Single(service.OpenDay(Today, At(19)).Summary.Intervals);
        Assert.Equal(lunch.Start, back.Start);
        Assert.Equal(lunch.End, back.End);
    }

    /// <summary>
    /// Правка файла руками остаётся допустимой: дописанное снаружи читается формой,
    /// а форма ничего поверх него не теряет.
    /// </summary>
    [Fact]
    public void HandEditedFileIsStillRead()
    {
        var service = Service();

        service.RecordReturn(At(9), "unlock");
        service.RecordPause(At(13), TimeSpan.Zero, "lock");
        service.RecordReturn(At(13, 45), "unlock");

        var page = service.OpenDay(Today, At(14));
        var text = File.ReadAllText(page.Path);

        // Человек поправил файл в редакторе: сдвинул конец обеда.
        File.WriteAllText(page.Path, text.Replace("13:45", "14:00", StringComparison.Ordinal));

        var after = Assert.Single(service.OpenDay(Today, At(15)).Summary.Intervals);
        Assert.Equal(At(14), after.End);
    }

    private GoHomeService Service() => TestApp.Service(_root, Even());
}
