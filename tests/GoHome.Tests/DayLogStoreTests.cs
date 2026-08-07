using GoHome.Core;
using GoHome.Storage;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

public sealed class DayLogStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "gohome-tests",
        Guid.NewGuid().ToString("N"));

    private DayLogStore Store => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Отсутствующий_файл_читается_как_пустой_день()
    {
        var log = Store.Load(Today);

        Assert.Equal(Today, log.Date);
        Assert.Empty(log.Punches);
        Assert.False(log.IsUnreadable);
    }

    [Fact]
    public void Журнал_переживает_запись_и_чтение()
    {
        var store = Store;
        store.Save(new DayLog
        {
            Date = Today,
            Punches = [In(9), BreakStart(13), BreakEnd(14)],
            LastHeartbeat = At(18),
            LastUserActivity = At(17, 55),
            GoalNotified = true,
        });

        var log = store.Load(Today);

        Assert.Equal(3, log.Punches.Count);
        Assert.Equal(At(9), log.Punches[0].At);
        Assert.Equal(PunchKind.BreakStart, log.Punches[1].Kind);
        Assert.Equal(At(17, 55), log.LastUserActivity);
        Assert.True(log.GoalNotified);
    }

    [Fact]
    public void Файл_остаётся_человекочитаемым()
    {
        var store = Store;
        store.Save(new DayLog { Date = Today, Punches = [new Punch(At(9), PunchKind.In, "unlock")] });

        var text = File.ReadAllText(store.PathFor(Today));

        Assert.Contains("\"date\": \"2026-08-06\"", text);
        Assert.Contains("\"kind\": \"In\"", text);      // не число
        Assert.Contains("\"source\": \"unlock\"", text);
        Assert.Contains(Environment.NewLine, text);      // с отступами, а не одной строкой
    }

    [Fact]
    public void Временный_файл_не_остаётся_после_записи()
    {
        var store = Store;
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });
        store.Save(new DayLog { Date = Today, Punches = [In(9), Out(18)] });

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public void Правки_руками_переживают_вольности_json()
    {
        var store = Store;
        File.WriteAllText(store.PathFor(Today), """
            {
              // забыл отметиться утром, дописал руками
              "date": "2026-08-06",
              "punches": [
                { "at": "2026-08-06T09:00:00+03:00", "kind": "In" },
                { "at": "2026-08-06T18:00:00+03:00", "kind": "Out" },
              ],
            }
            """);

        var log = store.Load(Today);

        Assert.False(log.IsUnreadable);
        Assert.Equal(2, log.Punches.Count);
        Assert.Equal(TimeSpan.FromHours(9), WorkTimeCalculator.Compute(log, At(20)).Worked);
    }

    [Fact]
    public void Битый_файл_не_затирается_и_помечается()
    {
        var store = Store;
        var path = store.PathFor(Today);
        const string broken = "{ \"punches\": [ { \"at\": ";
        File.WriteAllText(path, broken);

        var log = store.Load(Today);
        Assert.True(log.IsUnreadable);

        log.Punches.Add(In(9));
        store.Save(log);

        Assert.Equal(broken, File.ReadAllText(path));
    }

    [Fact]
    public void Update_читает_меняет_и_сохраняет_за_один_заход()
    {
        var store = Store;

        store.Update(Today, log => log.Punches.Add(In(9)));
        store.Update(Today, log => log.Punches.Add(Out(18)));

        Assert.Equal(2, store.Load(Today).Punches.Count);
    }

    [Fact]
    public void Имя_файла_авторитетнее_даты_внутри()
    {
        var store = Store;
        File.WriteAllText(store.PathFor(Today), """{ "date": "2000-01-01", "punches": [] }""");

        Assert.Equal(Today, store.Load(Today).Date);
    }

    [Fact]
    public void Даты_перечисляются_по_возрастанию_и_без_чужих_файлов()
    {
        var store = Store;
        store.Save(new DayLog { Date = Today });
        store.Save(new DayLog { Date = Today.AddDays(-3) });
        File.WriteAllText(Path.Combine(_root, "readme.json"), "{}");

        Assert.Equal([Today.AddDays(-3), Today], store.ListDates());
    }

    [Fact]
    public void Диапазон_включает_дни_без_файлов()
    {
        var store = Store;
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });

        var logs = store.LoadRange(Today.AddDays(-2), Today);

        Assert.Equal(3, logs.Count);
        Assert.All(logs.Take(2), log => Assert.Empty(log.Punches));
        Assert.Single(logs[2].Punches);
    }
}