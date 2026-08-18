using GoHome.App;
using GoHome.Core;
using GoHome.Ui;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Окна собираются кодом, и ошибка в раскладке видна только в рантайме: компилятор
/// про пустую строку столбца или неинициализированное поле ничего не скажет.
/// Здесь окна создаются и заполняются по-настоящему, на живом каталоге.
/// </summary>
public sealed class FormsSmokeTests : IDisposable
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

    [Fact]
    public void Окно_настроек_собирается_и_заполняется()
    {
        Run(service =>
        {
            using var form = new SettingsForm(service, () => At(13));
            Assert.Equal("GoHome — настройки", form.Text);

            // Хендл создаётся только сейчас: до него половина раскладки не выполняется.
            Assert.NotEqual(nint.Zero, form.Handle);
        });
    }

    [Fact]
    public void Окно_истории_собирается_на_дне_с_перерывами()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(13), TimeSpan.Zero, "lock");
            service.RecordReturn(At(13, 45), "unlock");

            using var form = new HistoryForm(service, () => At(15));
            Assert.NotEqual(nint.Zero, form.Handle);

            form.Reload();
        });
    }

    [Fact]
    public void Окно_истории_собирается_на_нерабочем_дне()
    {
        Run(
            service =>
            {
                service.RecordReturn(At(9), "unlock");

                using var form = new HistoryForm(service, () => At(13));
                Assert.NotEqual(nint.Zero, form.Handle);
            },
            AppSettings.Default with { Schedule = Flat(null) });
    }

    [Fact]
    public void Окно_исключения_собирается_для_обоих_состояний()
    {
        Run(_ =>
        {
            using var dayOff = new DayExceptionDialog(Today, null, null, lockDate: true);
            Assert.NotEqual(nint.Zero, dayOff.Handle);

            var existing = new DateException { Date = Today, Hours = Hm(7), Note = "предпраздничный" };
            using var shortened = new DayExceptionDialog(Today, existing, Hm(8), lockDate: false);
            Assert.Equal(Hm(7), shortened.Result.Hours);
            Assert.Equal("предпраздничный", shortened.Result.Note);
        });
    }

    /// <summary>Формы живут только на STA-потоке — xUnit по умолчанию даёт MTA.</summary>
    private void Run(Action<GoHomeService> body, AppSettings? settings = null)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body(TestApp.Service(_root, settings ?? Even()));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("окно не собралось", failure);
        }
    }
}