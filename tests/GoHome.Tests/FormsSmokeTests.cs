using GoHome.App;
using GoHome.Core;
using GoHome.Ui;
using GoHome.Ui.Design;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Окна собираются кодом, и ошибка в раскладке видна только в рантайме: компилятор
/// про пустую строку столбца или неинициализированное поле ничего не скажет.
/// Здесь окна создаются и заполняются по-настоящему, на живом каталоге.
/// Графики вдобавок рисуются в картинку — деление на ноль в масштабе оси иначе
/// обнаружится только у пользователя.
/// </summary>
[Collection(UiCollection.Name)]
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
    public void Окно_статистики_собирается_на_дне_с_перерывами()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(13), TimeSpan.Zero, "lock");
            service.RecordReturn(At(13, 45), "unlock");

            using var form = Stats(service, At(15));
            Assert.NotEqual(nint.Zero, form.Handle);

            form.Reload();
        });
    }

    [Fact]
    public void Окно_статистики_собирается_на_нерабочем_дне()
    {
        Run(
            service =>
            {
                service.RecordReturn(At(9), "unlock");

                using var form = Stats(service, At(13));
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

    [Fact]
    public void Статистика_досчитывается_и_доезжает_до_окна()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");

            using var form = Stats(service, At(13));

            // Ответ возвращается в UI-поток, поэтому без насоса сообщений он не доедет.
            Assert.True(
                Pump(() => form.Controls.Count > 0 && form.Text.Contains("статистика", StringComparison.Ordinal)),
                "статистика не доехала до окна");
        });
    }

    /// <summary>Щелчок по дню в статистике просит открыть форму дня.</summary>
    [Fact]
    public void Статистика_просит_открыть_день()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(18), TimeSpan.Zero, "lock");

            using var form = Stats(service, At(19));
            Assert.True(Pump(() => form.Text.Contains("статистика", StringComparison.Ordinal)));

            DateOnly? asked = null;
            form.DayRequested += (_, date) => asked = date;

            var list = form.Controls
                .OfType<Control>()
                .SelectMany(Descendants)
                .OfType<DesignList>()
                .First();

            list.SelectedIndex = 0;

            // Enter на выбранной строке — тем же сообщением, каким его шлёт система.
            SendMessageW(list.Handle, WmKeyDown, (nint)Keys.Enter, 0);

            Assert.Equal(Today, asked);
        });
    }

    private const int WmKeyDown = 0x0100;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, int msg, nint wParam, nint lParam);

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Окно статистики за пределами экрана: показанное, но не мешающее.</summary>
    private static StatsForm Stats(GoHomeService service, DateTimeOffset now)
    {
        var form = new StatsForm(service, () => now)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
        };

        form.Show();
        Application.DoEvents();
        return form;
    }

    [Fact]
    public void Пустой_период_рисуется_без_деления_на_ноль()
    {
        Run(_ =>
        {
            using var chart = new DayBarChart();
            Paint(chart, Stats([], StatsPeriod.Week));
        });
    }

    [Fact]
    public void Неделя_с_разными_целями_и_нерабочим_днём_рисуется()
    {
        var monday = HistoryCalculator.WeekStart(Today);

        Run(_ =>
        {
            var logs = new[]
            {
                Log(monday, In(9, 0, monday.DayNumber - Today.DayNumber), Out(18, 0, monday.DayNumber - Today.DayNumber))
                    .By(Goal(Hm(8))),
                Log(monday.AddDays(1), In(10, 0, monday.AddDays(1).DayNumber - Today.DayNumber), Out(14, 0, monday.AddDays(1).DayNumber - Today.DayNumber))
                    .By(Goal(Hm(4))),
                Log(monday.AddDays(2), In(11, 0, monday.AddDays(2).DayNumber - Today.DayNumber), Out(13, 0, monday.AddDays(2).DayNumber - Today.DayNumber))
                    .By(Goal(null)),
            };

            using var chart = new DayBarChart();
            Paint(chart, Stats(logs, StatsPeriod.Week));
        });
    }

    [Fact]
    public void Месяц_рисуется_с_подписями_не_каждого_дня()
    {
        Run(_ =>
        {
            using var chart = new DayBarChart();
            Paint(chart, Stats([], StatsPeriod.Month), new Size(520, 300));
        });
    }

    [Fact]
    public void Год_рисуется_сеткой_целиком()
    {
        Run(_ =>
        {
            var logs = new[]
            {
                Log(Today, In(9), Out(18)).By(Goal(Hm(8))),
            };

            using var grid = new YearHeatGrid();
            Paint(grid, Stats(logs, StatsPeriod.Year), new Size(900, 220));
        });
    }

    [Fact]
    public void Год_в_крошечном_окне_не_падает()
    {
        Run(_ =>
        {
            using var grid = new YearHeatGrid();
            Paint(grid, Stats([], StatsPeriod.Year), new Size(40, 30));
        });
    }

    private static PeriodStats Stats(IEnumerable<DayLog> logs, StatsPeriod period) =>
        StatsCalculator.For(logs, At(20), Even(Hm(8)), PeriodRange.Of(period, Today));

    private static void Paint(DayBarChart chart, PeriodStats stats, Size? size = null)
    {
        chart.Display(stats, Palette.Current());
        Render(chart, size);
    }

    private static void Paint(YearHeatGrid grid, PeriodStats stats, Size? size = null)
    {
        grid.Display(stats, Palette.Current());
        Render(grid, size);
    }

    /// <summary>Крутит очередь сообщений, пока условие не выполнится или не кончится терпение.</summary>
    private static bool Pump(Func<bool> done)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (done())
            {
                return true;
            }

            Application.DoEvents();
            Thread.Sleep(10);
        }

        return done();
    }

    /// <summary>Заставляет контрол нарисоваться по-настоящему, без показа окна.</summary>
    private static void Render(Control control, Size? size)
    {
        control.Size = size ?? new Size(640, 320);
        Assert.NotEqual(nint.Zero, control.Handle);

        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
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
