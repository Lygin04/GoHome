using WorkClock.App;
using WorkClock.Core;
using WorkClock.Storage;
using static WorkClock.Tests.TestClock;

namespace WorkClock.Tests;

public sealed class WorkClockServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "workclock-tests",
        Guid.NewGuid().ToString("N"));

    private readonly DayLogStore _store;
    private readonly WorkClockService _service;

    public WorkClockServiceTests()
    {
        _store = new DayLogStore(_root);
        _service = new WorkClockService(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Возврат_в_пустой_день_становится_приходом()
    {
        _service.RecordReturn(At(9), "unlock");

        var summary = _service.Summarize(At(13));

        Assert.Equal(At(9), summary.ArrivedAt);
        Assert.Equal(Hm(4), summary.Worked);
        Assert.Equal(WorkState.Working, summary.State);
    }

    [Fact]
    public void Повторный_возврат_не_дублируется()
    {
        Assert.True(_service.RecordReturn(At(9), "unlock"));
        Assert.False(_service.RecordReturn(At(9, 30), "startup"));

        Assert.Single(_store.Load(Today).Punches);
    }

    [Fact]
    public void Пауза_без_простоя_ставится_текущим_временем()
    {
        _service.RecordReturn(At(9), "unlock");

        _service.RecordPause(At(13), TimeSpan.FromSeconds(3), "SessionLock");

        var summary = _service.Summarize(At(14));
        Assert.Equal(Hm(4), summary.Worked);
        Assert.Equal(WorkState.OnBreak, summary.State);
    }

    [Fact]
    public void Автолок_по_политике_не_дарит_минуты_простоя()
    {
        _service.RecordReturn(At(9), "unlock");

        // Сессия погасла сама через 15 минут после того, как человек отошёл.
        _service.RecordPause(At(13, 15), TimeSpan.FromMinutes(15), "SessionLock");

        Assert.Equal(Hm(4), _service.Summarize(At(14)).Worked);
    }

    [Fact]
    public void Пауза_не_ставится_если_время_и_так_не_идёт()
    {
        _service.RecordReturn(At(9), "unlock");
        Assert.True(_service.RecordPause(At(13), TimeSpan.Zero, "SessionLock"));
        Assert.False(_service.RecordPause(At(13, 5), TimeSpan.Zero, "shutdown"));

        Assert.Equal(2, _store.Load(Today).Punches.Count);
    }

    [Fact]
    public void Пауза_не_уезжает_раньше_прихода()
    {
        _service.RecordReturn(At(9), "unlock");

        // Человек разблокировал экран и тут же ушёл, а простой копился с ночи.
        _service.RecordPause(At(9, 2), TimeSpan.FromHours(8), "SessionLock");

        Assert.Equal(TimeSpan.Zero, _service.Summarize(At(12)).Worked);
        Assert.All(_store.Load(Today).Punches, punch => Assert.True(punch.At >= At(9)));
    }

    [Fact]
    public void Heartbeat_не_создаёт_файл_за_день_без_прихода()
    {
        _service.Heartbeat(At(9), TimeSpan.FromMinutes(1));

        Assert.False(File.Exists(_store.PathFor(Today)));
    }

    [Fact]
    public void Heartbeat_запоминает_последнюю_активность()
    {
        _service.RecordReturn(At(9), "unlock");

        _service.Heartbeat(At(13), TimeSpan.FromMinutes(10));

        var log = _store.Load(Today);
        Assert.Equal(At(13), log.LastHeartbeat);
        Assert.Equal(At(12, 50), log.LastUserActivity);
    }

    [Fact]
    public void Активность_не_едет_назад()
    {
        _service.RecordReturn(At(9), "unlock");
        _service.Heartbeat(At(13), TimeSpan.Zero);
        _service.Heartbeat(At(13, 1), TimeSpan.FromMinutes(30));

        Assert.Equal(At(13), _store.Load(Today).LastUserActivity);
    }

    [Fact]
    public void Незакрытый_вчерашний_день_закрывается_по_последней_активности()
    {
        var yesterday = Today.AddDays(-1);
        _service.RecordReturn(At(9, 0, -1), "unlock");
        _service.Heartbeat(At(19, 30, -1), TimeSpan.FromMinutes(10)); // человек ушёл в 19:20
        _service.Heartbeat(At(3, 0), Hm(7, 40));                       // машина дотикала до ночного ребута

        Assert.Equal(1, _service.CloseStaleDays(At(9)));

        var log = _store.Load(yesterday);
        var last = log.Punches[^1];
        Assert.Equal(PunchKind.Out, last.Kind);
        Assert.Equal(At(19, 20, -1), last.At);
        Assert.Equal(Hm(10, 20), WorkTimeCalculator.Compute(log, At(9)).Worked);
    }

    [Fact]
    public void День_завершившийся_блокировкой_не_переписывается()
    {
        // Висящая пауза вчерашним днём и так трактуется как уход — трогать журнал незачем.
        _service.RecordReturn(At(9, 0, -1), "unlock");
        _service.RecordPause(At(18, 0, -1), TimeSpan.Zero, "SessionLock");

        Assert.Equal(0, _service.CloseStaleDays(At(9)));
        Assert.Equal(2, _store.Load(Today.AddDays(-1)).Punches.Count);
    }

    [Fact]
    public void Текущий_день_не_закрывается()
    {
        _service.RecordReturn(At(9), "unlock");

        Assert.Equal(0, _service.CloseStaleDays(At(13)));
        Assert.Equal(WorkState.Working, _service.Summarize(At(13)).State);
    }

    [Fact]
    public void Уведомление_о_норме_срабатывает_ровно_один_раз()
    {
        _service.RecordReturn(At(9), "unlock");

        Assert.False(_service.TryTakeGoalNotification(At(16, 59)));
        Assert.True(_service.TryTakeGoalNotification(At(17)));
        Assert.False(_service.TryTakeGoalNotification(At(17, 1)));
        Assert.False(_service.TryTakeGoalNotification(At(19)));
    }

    [Fact]
    public void Флаг_уведомления_переживает_перезапуск()
    {
        _service.RecordReturn(At(9), "unlock");
        Assert.True(_service.TryTakeGoalNotification(At(17)));

        var restarted = new WorkClockService(new DayLogStore(_root));
        Assert.False(restarted.TryTakeGoalNotification(At(17, 30)));
    }

    [Fact]
    public void Уведомление_не_приходит_в_день_без_прихода()
    {
        Assert.False(_service.TryTakeGoalNotification(At(19)));
    }

    [Fact]
    public void Убийство_процесса_не_теряет_накопленное()
    {
        _service.RecordReturn(At(9), "unlock");
        _service.RecordPause(At(13), TimeSpan.Zero, "SessionLock");
        _service.RecordReturn(At(14), "unlock");

        // Процесс убили, приложение поднялось заново на том же каталоге.
        var restarted = new WorkClockService(new DayLogStore(_root));
        restarted.CloseStaleDays(At(15));
        restarted.RecordReturn(At(15), "startup");

        Assert.Equal(Hm(5), restarted.Summarize(At(15)).Worked);
    }

    [Fact]
    public void Работа_через_четыре_утра_переносится_в_новый_день()
    {
        _service.RecordReturn(At(22), "unlock");

        _service.HandleRollover(At(4, 0, 1), Today, TimeSpan.FromSeconds(5), workstationLocked: false);

        Assert.Equal(Hm(6), WorkTimeCalculator.Compute(_store.Load(Today), At(5, 0, 1)).Worked);

        var next = _service.Summarize(At(5, 0, 1));
        Assert.Equal(Hm(1), next.Worked);
        Assert.Equal(WorkState.Working, next.State);
    }

    [Fact]
    public void Смена_дня_у_заблокированной_машины_ничего_не_переносит()
    {
        _service.RecordReturn(At(22), "unlock");
        _service.Heartbeat(At(23), TimeSpan.FromMinutes(30));

        _service.HandleRollover(At(4, 0, 1), Today, TimeSpan.FromHours(5), workstationLocked: true);

        Assert.Equal(WorkState.NotStarted, _service.Summarize(At(5, 0, 1)).State);
        Assert.Equal(PunchKind.Out, _store.Load(Today).Punches[^1].Kind);
    }

    [Fact]
    public void История_отдаёт_запрошенное_окно()
    {
        _service.RecordReturn(At(9, 0, -1), "unlock");
        _service.RecordPause(At(18, 0, -1), TimeSpan.Zero, "SessionLock");
        _service.RecordReturn(At(9), "unlock");

        var history = _service.History(At(13), 7);

        Assert.Equal(7, history.Count);
        Assert.Equal(Today, history[0].Date);
        Assert.Equal(Hm(4), history[0].Worked);
        Assert.Equal(Hm(9), history[1].Worked);
    }
}