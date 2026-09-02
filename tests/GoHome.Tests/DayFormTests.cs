using GoHome.App;
using GoHome.Core;
using GoHome.Ui;
using GoHome.Ui.Design;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Форма дня на настоящем каталоге.
/// </summary>
/// <remarks>
/// Проверяются состояния, в которых окно ломается охотнее всего: день без единого события,
/// нерабочий день, прошлый день, день через полночь и неразобравшийся файл. Ошибка в любом
/// из них — либо деление на ноль, либо окно, обещающее то, чего в этом дне быть не может.
/// </remarks>
[Collection(UiThemeCollection.Name)]
public sealed class DayFormTests : IDisposable
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

    /// <summary>Обычный день собирается и рисуется.</summary>
    [Fact]
    public void OrdinaryDayOpens()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(13), TimeSpan.Zero, "lock");
            service.RecordReturn(At(13, 45), "unlock");

            using var form = Open(service, At(17));
            Assert.Contains("День", form.Text, StringComparison.Ordinal);
            Render(form);
        });
    }

    /// <summary>
    /// День без единого события не выглядит сломанным: ни полосы, ни списка, ни деления
    /// на ноль — только сообщение, что событий не было.
    /// </summary>
    [Fact]
    public void EmptyDayShowsNothingRatherThanZeroes()
    {
        Run(service =>
        {
            using var form = Open(service, At(6));
            Render(form);

            var page = service.OpenDay(Today, At(6));
            Assert.Equal(TimeSpan.Zero, page.Summary.Worked);
            Assert.True(DayBand.For(page.Summary, At(6), 2).IsEmpty);
        });
    }

    /// <summary>У нерабочего дня нет ни цели, ни прогноза, ни отметки цели на полосе.</summary>
    [Fact]
    public void DayOffHasNoGoal()
    {
        Run(
            service =>
            {
                service.RecordReturn(At(10), "unlock");

                using var form = Open(service, At(14));
                Render(form);

                var page = service.OpenDay(Today, At(14));
                Assert.True(page.Summary.IsDayOff);
                Assert.Null(DayBand.For(page.Summary, At(14), 2).Goal);
            },

            // Не Even(null): там ?? подменяет пустую продолжительность восемью часами,
            // и выходного не получается. График задаётся прямо.
            AppSettings.Default with { Schedule = Flat(null) });
    }

    /// <summary>
    /// У прошлого дня нет ни отметки «сейчас», ни прогноза ухода: время в нём больше
    /// не идёт, и обещать что-либо про уход нельзя.
    /// </summary>
    [Fact]
    public void PastDayCarriesNoLiveMarks()
    {
        Run(service =>
        {
            service.RecordReturn(At(9, 0, -1), "unlock");
            service.RecordPause(At(18, 0, -1), TimeSpan.Zero, "lock");

            using var form = Open(service, At(12));
            form.ShowDate(Today.AddDays(-1));
            Render(form);

            var page = service.OpenDay(Today.AddDays(-1), At(12));
            var band = DayBand.For(page.Summary, At(12), 2);

            Assert.Null(band.Now);
            Assert.Null(band.Goal);
        });
    }

    /// <summary>Вечер с переходом за полночь показывается непрерывным отрезком.</summary>
    [Fact]
    public void DayCrossingMidnightOpens()
    {
        Run(service =>
        {
            service.RecordReturn(At(21), "unlock");
            service.RecordPause(At(1, 0, 1), TimeSpan.Zero, "lock");

            using var form = Open(service, At(2, 0, 1));
            Render(form);

            var band = DayBand.For(service.OpenDay(Today, At(2, 0, 1)).Summary, At(2, 0, 1), 2);
            Assert.True(band.To > band.From);
            Assert.All(band.Segments, segment => Assert.True(segment.End > segment.Start));
        });
    }

    /// <summary>
    /// Неразобравшийся файл не притворяется пустым днём: форма про него говорит и ничего
    /// в нём не предлагает.
    /// </summary>
    [Fact]
    public void UnreadableFileIsReported()
    {
        Run(service =>
        {
            var path = service.OpenDay(Today, At(12)).Path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ это не json");

            var page = service.OpenDay(Today, At(12));
            Assert.True(page.Unreadable);

            using var form = Open(service, At(12));
            Render(form);
        });
    }

    /// <summary>Вперёд дальше сегодняшнего дня форма не пускает: того дня ещё не было.</summary>
    [Fact]
    public void TomorrowIsOutOfReach()
    {
        Run(service =>
        {
            using var form = Open(service, At(12));
            form.ShowDate(Today.AddDays(5));

            Assert.Contains(
                Today.ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),
                form.Text,
                StringComparison.Ordinal);
        });
    }

    /// <summary>Окно рисуется в обеих темах и в узком, и в широком размере.</summary>
    [Theory]
    [InlineData(AppTheme.Dark, 1180)]
    [InlineData(AppTheme.Light, 960)]
    [InlineData(AppTheme.Dark, 700)]
    public void DayFormPaintsAtEverySize(AppTheme theme, int width)
    {
        Run(service =>
        {
            WindowTheme.Apply(theme);

            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(13), TimeSpan.Zero, "lock");
            service.RecordReturn(At(13, 45), "unlock");

            using var form = Open(service, At(17));
            form.Size = new Size(width, 620);
            Render(form);
        });
    }

    private static DayForm Open(GoHomeService service, DateTimeOffset now)
    {
        var form = new DayForm(service, () => now)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
        };

        _ = form.Handle;
        return form;
    }

    private static void Render(Form form)
    {
        using var bitmap = new Bitmap(Math.Max(form.Width, 1), Math.Max(form.Height, 1));
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
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
            throw new InvalidOperationException("форма дня не собралась", failure);
        }
    }
}
