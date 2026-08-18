using GoHome.Core;
using GoHome.Diagnostics;
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

    /// <summary>Хранилище, которое не ждёт между попытками: тесту незачем спать секундами.</summary>
    private DayLogStore Impatient
    {
        get
        {
            var store = Store;
            store.WriteBackoff = [];
            store.ReadBackoff = [];
            return store;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Держит файл так, как его держат антивирус, индексатор или обновляющийся проводник:
    /// читать можно, подменить нельзя.
    /// </summary>
    private static FileStream Hold(string path) =>
        new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

    /// <summary>Читает файл, не мешая тому, кто его держит: <see cref="File.ReadAllText(string)"/> так не умеет.</summary>
    private static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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

    [Fact]
    public void Занятый_файл_дня_не_роняет_запись()
    {
        var store = Impatient;
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });

        using var held = Hold(store.PathFor(Today));

        // Именно это раньше и валило приложение: исключение уходило из хранилища наверх,
        // через тик таймера, в модальное окно .NET.
        Assert.False(store.Save(new DayLog { Date = Today, Punches = [In(9), Out(18)] }));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Освободившийся_со_второй_попытки_файл_записывается()
    {
        var store = Store;
        store.WriteBackoff = JsonFile.DefaultWriteBackoff;
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });

        var held = Hold(store.PathFor(Today));
        var release = Task.Run(() =>
        {
            Thread.Sleep(50);
            held.Dispose();
        });

        // Первая попытка натыкается на занятый файл, одна из следующих — уже на свободный.
        var saved = store.Save(new DayLog { Date = Today, Punches = [In(9), Out(18)] });
        await release;

        Assert.True(saved);
        Assert.Equal(2, store.Load(Today).Punches.Count);
    }

    [Fact]
    public void Неудачная_запись_попадает_в_журнал_ошибок()
    {
        var logPath = Path.Combine(_root, "errors.log");
        var store = new DayLogStore(_root, new ErrorLog(logPath)) { WriteBackoff = [] };
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });

        using (Hold(store.PathFor(Today)))
        {
            store.Save(new DayLog { Date = Today, Punches = [In(9), Out(18)] });
        }

        var written = ReadText(logPath);
        Assert.Contains(store.PathFor(Today), written, StringComparison.Ordinal);
        Assert.Contains("не удалось сохранить", written, StringComparison.Ordinal);
        Assert.Contains("Exception", written, StringComparison.Ordinal);   // и причина, а не только факт
    }

    [Fact]
    public void Файл_открытый_на_чтение_читается()
    {
        var store = Impatient;
        store.Save(new DayLog { Date = Today, Punches = [In(9), Out(18)] });

        // Заказчик открыл файл дня в блокноте посмотреть, что там записалось.
        using var reader = new FileStream(store.PathFor(Today), FileMode.Open, FileAccess.Read, FileShare.Read);

        var log = store.Load(Today);

        Assert.False(log.IsUnreadable);
        Assert.Equal(2, log.Punches.Count);
    }

    [Fact]
    public void Битый_файл_не_роняет_обновление()
    {
        var store = Impatient;
        var path = store.PathFor(Today);
        const string broken = "{ \"punches\": [ { \"at\": ";
        File.WriteAllText(path, broken);

        // Тик таймера ходит сюда каждую минуту и на битом файле падать не должен.
        Assert.True(store.TryUpdate(Today, log =>
        {
            log.Punches.Add(In(9));
            return true;
        }));

        Assert.Equal(broken, File.ReadAllText(path));
    }

    [Fact]
    public void О_битом_файле_говорится_один_раз()
    {
        var store = Impatient;
        File.WriteAllText(store.PathFor(Today), "{ \"punches\": [ { \"at\": ");

        store.Load(Today);
        var alert = store.TryTakeAlert();

        Assert.Equal(StorageAlertKind.FileUnreadable, alert?.Kind);
        Assert.Equal(store.PathFor(Today), alert?.Path);

        // Второй раз про то же самое человека не тревожим.
        store.Load(Today);
        Assert.Null(store.TryTakeAlert());
    }

    [Fact]
    public void О_затянувшейся_неудаче_записи_говорится_один_раз()
    {
        var store = Impatient;
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });

        using (Hold(store.PathFor(Today)))
        {
            // Одна-две неудачи — обычная занятость файла, молчим.
            store.Save(new DayLog { Date = Today, Punches = [In(9), Out(17)] });
            Assert.Null(store.TryTakeAlert());
            store.Save(new DayLog { Date = Today, Punches = [In(9), Out(17)] });
            Assert.Null(store.TryTakeAlert());

            store.Save(new DayLog { Date = Today, Punches = [In(9), Out(18)] });
            var alert = store.TryTakeAlert();

            Assert.Equal(StorageAlertKind.WriteFailing, alert?.Kind);
            Assert.Equal(store.PathFor(Today), alert?.Path);

            store.Save(new DayLog { Date = Today, Punches = [In(9), Out(18)] });
            Assert.Null(store.TryTakeAlert());
        }
    }

    [Fact]
    public void Накопленное_переживает_серию_неудачных_записей()
    {
        var store = Impatient;
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });
        var onDisk = File.ReadAllText(store.PathFor(Today));

        using (Hold(store.PathFor(Today)))
        {
            store.Update(Today, log => log.Punches.Add(BreakStart(13)));
            store.Update(Today, log => log.Punches.Add(BreakEnd(14)));
            store.Update(Today, log => log.Punches.Add(Out(18)));

            // На диске пока прежний снимок, но в памяти день целиком.
            Assert.Equal(onDisk, ReadText(store.PathFor(Today)));
            Assert.Equal(4, store.Load(Today).Punches.Count);
            Assert.True(store.HasPendingWrites);
        }

        Assert.True(store.Flush());
        Assert.False(store.HasPendingWrites);

        // Всё, что накопилось за время недоступности файла, оказалось записанным.
        var saved = Store.Load(Today);
        Assert.Equal(4, saved.Punches.Count);
        Assert.Equal([At(9), At(13), At(14), At(18)], saved.Punches.Select(punch => punch.At));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Оставшиеся_временные_файлы_убираются_при_запуске()
    {
        Directory.CreateDirectory(_root);

        // Имя прошлых версий и имя нынешних — обрывок мог остаться от любой.
        File.WriteAllText(Path.Combine(_root, "2026-08-14.json.tmp"), "{ обрыв");
        File.WriteAllText(Path.Combine(_root, "2026-08-06.json.9f3c2b.tmp"), "{ обрыв");
        File.WriteAllText(Path.Combine(_root, "чужое.tmp"), "не наше");

        _ = Store;

        Assert.Equal(["чужое.tmp"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void Перечисление_дней_не_видит_ничего_постороннего()
    {
        Directory.CreateDirectory(_root);
        var store = Store;
        store.Save(new DayLog { Date = Today });

        File.WriteAllText(Path.Combine(_root, "заметки.txt"), "напоминание");
        File.WriteAllText(Path.Combine(_root, "2026-08-06.json.bak"), "{}");
        File.WriteAllText(Path.Combine(_root, "2026-13-40.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "2026-08-14.json.9f3c2b.tmp"), "{ обрыв");
        Directory.CreateDirectory(Path.Combine(_root, "2026-08-05.json"));

        Assert.Equal([Today], store.ListDates());
    }

    [Fact]
    public void День_не_легший_на_диск_остаётся_в_перечислении()
    {
        var store = Impatient;
        store.Save(new DayLog { Date = Today, Punches = [In(9)] });

        using var held = Hold(store.PathFor(Today.AddDays(-1)));
        store.Update(Today.AddDays(-1), log => log.Punches.Add(In(9)));

        // Файла за вчера нет, но день уже существует — закрытие незакрытых дней
        // не должно его потерять.
        Assert.Equal([Today.AddDays(-1), Today], store.ListDates());
    }
}