using System.Runtime.InteropServices;
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

    /// <summary>
    /// Править предлагается только там, где есть что править: у работы своих отметок
    /// в журнале нет, она — то, что осталось между отлучками.
    /// </summary>
    [Fact]
    public void ActionsWakeUpOnlyOnABreak()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(13), TimeSpan.Zero, "lock");
            service.RecordReturn(At(13, 45), "unlock");

            using var form = Open(service, At(17));
            form.Size = new Size(1180, 620);

            var list = Find<DesignList>(form).First();
            var edit = Button(form, "Изменить время");

            ClickRow(list, 0);
            Assert.False(edit.Enabled);

            ClickRow(list, 1);
            Assert.True(edit.Enabled);
        });
    }

    /// <summary>Правка открывается на границах выбранной отлучки.</summary>
    [Fact]
    public void EditorOpensOnTheSelectedBreak()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(13), TimeSpan.Zero, "lock");
            service.RecordReturn(At(13, 45), "unlock");

            using var form = Open(service, At(17));
            form.Size = new Size(1180, 620);

            ClickRow(Find<DesignList>(form).First(), 1);
            Click(Button(form, "Изменить время"));

            var editor = Assert.Single(Find<BreakEditor>(form));
            Assert.True(editor.Visible);
            Assert.Equal((new TimeOnly(13, 0), new TimeOnly(13, 45)), editor.Value);
        });
    }

    /// <summary>Удаление спрашивает подтверждение, а не убирает перерыв по щелчку.</summary>
    [Fact]
    public void DeleteAsksFirst()
    {
        Run(service =>
        {
            service.RecordReturn(At(9), "unlock");
            service.RecordPause(At(13), TimeSpan.Zero, "lock");
            service.RecordReturn(At(13, 45), "unlock");

            using var form = Open(service, At(17));
            form.Size = new Size(1180, 620);

            ClickRow(Find<DesignList>(form).First(), 1);
            Click(Button(form, "Удалить"));

            // Перерыв на месте: спросили, но не сделали.
            Assert.Single(service.OpenDay(Today, At(17)).Summary.Intervals);

            // И подтверждение показано — с той же надписью и своей кнопкой отмены.
            Assert.Contains(Find<DesignButton>(form), button => button.Text == "Отмена" && button.Visible);
        });
    }

    // ---- щелчки теми же сообщениями, что шлёт система --------------------------------

    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, int msg, nint wParam, nint lParam);

    private static DesignButton Button(Control root, string text) =>
        Find<DesignButton>(root).First(button => button.Text == text);

    private static IEnumerable<T> Find<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T found)
            {
                yield return found;
            }

            foreach (var deeper in Find<T>(child))
            {
                yield return deeper;
            }
        }
    }

    private static void ClickRow(DesignList list, int index)
    {
        var row = Metrics.Of(list).RowHeight;
        Click(list, list.Width / 2, (row * index) + (row / 2));
    }

    private static void Click(Control control) =>
        Click(control, control.Width / 2, control.Height / 2);

    private static void Click(Control control, int x, int y)
    {
        var at = (y << 16) | (x & 0xFFFF);
        SendMessageW(control.Handle, WmLButtonDown, 1, at);
        SendMessageW(control.Handle, WmLButtonUp, 0, at);
    }

    private static DayForm Open(GoHomeService service, DateTimeOffset now)
    {
        var form = new DayForm(service, () => now)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
        };

        // За пределами экрана, но по-настоящему показано: без этого раскладки нет,
        // контролы нулевого размера, и щелчок не попадает никуда.
        form.Show();
        Application.DoEvents();
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
