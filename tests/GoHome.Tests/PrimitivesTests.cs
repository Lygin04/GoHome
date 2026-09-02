using System.Runtime.InteropServices;
using GoHome.Core;
using GoHome.Ui;
using GoHome.Ui.Design;

namespace GoHome.Tests;

/// <summary>
/// Примитивы, из которых собраны все окна.
/// </summary>
/// <remarks>
/// Нарисованный вручную элемент не получает от системы ни клавиатуры, ни видимого фокуса,
/// ни отмены нажатия уведённым курсором — всё это написано руками и потому проверяется.
/// Клавиши шлются тем же сообщением, что и настоящие: <c>WM_KEYDOWN</c> проходит через
/// <c>WndProc</c> ровно так же, как из очереди сообщений.
/// <para>
/// Отдельная тема каждой проверки — рисование в обеих палитрах и в высокой контрастности:
/// пустая ссылка на цвет или нулевой размер видны только на отрисовке.
/// </para>
/// </remarks>
[Collection(UiThemeCollection.Name)]
public sealed class PrimitivesTests
{
    private const int WmKeyDown = 0x0100;

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, int msg, nint wParam, nint lParam);

    // ---- кнопка --------------------------------------------------------------------

    /// <summary>Кнопка держит высоту из дизайна и раздаётся вширь под надпись.</summary>
    [Fact]
    public void ButtonKeepsTheDesignHeightAndFitsItsText()
    {
        using var host = new Host();
        var button = host.Add(new DesignButton { Text = "Засчитать как работу" });
        button.FitToText();

        var narrow = host.Add(new DesignButton { Text = "Да" });
        narrow.FitToText();

        Assert.Equal(Metrics.Of(button).ControlHeight, button.Height);
        Assert.Equal(button.Height, narrow.Height);
        Assert.True(button.Width > narrow.Width);
    }

    /// <summary>Пробел и Enter нажимают кнопку — как любую кнопку Windows.</summary>
    [Theory]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Enter)]
    public void ButtonAnswersTheKeyboard(Keys key)
    {
        using var host = new Host();
        var button = host.Add(new DesignButton { Text = "Сохранить" });

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Press(button, key);

        Assert.Equal(1, clicks);
    }

    /// <summary>Недоступная кнопка не нажимается ни мышью, ни с клавиатуры.</summary>
    [Fact]
    public void DisabledButtonStaysSilent()
    {
        using var host = new Host();
        var button = host.Add(new DesignButton { Text = "Сохранить", Enabled = false });

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Press(button, Keys.Space);

        Assert.Equal(0, clicks);
    }

    /// <summary>Все три вида и все четыре состояния рисуются в любой теме.</summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.System)]
    public void EveryButtonKindPaints(AppTheme theme)
    {
        WindowTheme.Apply(theme);

        using var host = new Host();
        foreach (var kind in Enum.GetValues<ButtonKind>())
        {
            foreach (var enabled in new[] { true, false })
            {
                var button = host.Add(new DesignButton { Kind = kind, Text = "Кнопка", Enabled = enabled });
                button.FitToText();
                Render(button);
            }
        }
    }

    // ---- шаг ------------------------------------------------------------------------

    /// <summary>Шаг остаётся квадратным на любом масштабе и рисуется в обе стороны.</summary>
    [Fact]
    public void StepButtonIsSquareAndPointsBothWays()
    {
        using var host = new Host();
        var back = host.Add(new StepButton { Direction = Chevron.Left });
        var next = host.Add(new StepButton { Direction = Chevron.Right, Enabled = false });

        Assert.Equal(back.Width, back.Height);
        Assert.Equal(Metrics.Of(back).StepperSize, back.Width);

        Render(back);
        Render(next);
    }

    // ---- переключатель ---------------------------------------------------------------

    /// <summary>Пробел переключает и сообщает об этом.</summary>
    [Fact]
    public void SwitchTogglesFromTheKeyboard()
    {
        using var host = new Host();
        var toggle = host.Add(new DesignSwitch());

        var changes = 0;
        toggle.CheckedChanged += (_, _) => changes++;

        Press(toggle, Keys.Space);
        Assert.True(toggle.Checked);

        Press(toggle, Keys.Space);
        Assert.False(toggle.Checked);
        Assert.Equal(2, changes);
    }

    /// <summary>Недоступный переключатель не переключается и событий не шлёт.</summary>
    [Fact]
    public void DisabledSwitchDoesNotToggle()
    {
        using var host = new Host();
        var toggle = host.Add(new DesignSwitch { Enabled = false });

        var changes = 0;
        toggle.CheckedChanged += (_, _) => changes++;

        Press(toggle, Keys.Space);

        Assert.False(toggle.Checked);
        Assert.Equal(0, changes);
    }

    /// <summary>Оба положения рисуются в любой теме.</summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.System)]
    public void SwitchPaintsInEveryTheme(AppTheme theme)
    {
        WindowTheme.Apply(theme);

        using var host = new Host();
        foreach (var on in new[] { true, false })
        {
            foreach (var enabled in new[] { true, false })
            {
                Render(host.Add(new DesignSwitch { Checked = on, Enabled = enabled }));
            }
        }
    }

    // ---- сегментированные вкладки ------------------------------------------------------

    /// <summary>Стрелки переключают период, не выводя фокус из контрола.</summary>
    [Fact]
    public void TabsMoveWithTheArrows()
    {
        using var host = new Host();
        var tabs = host.Add(new SegmentedTabs { Items = ["Неделя", "Месяц", "Год"] });

        var changes = 0;
        tabs.SelectedChanged += (_, _) => changes++;

        Press(tabs, Keys.Right);
        Assert.Equal(1, tabs.Selected);

        Press(tabs, Keys.End);
        Assert.Equal(2, tabs.Selected);

        // Дальше края не уходит и лишнего события не шлёт.
        Press(tabs, Keys.Right);
        Assert.Equal(2, tabs.Selected);
        Assert.Equal(2, changes);

        Press(tabs, Keys.Home);
        Assert.Equal(0, tabs.Selected);
    }

    /// <summary>Смена набора надписей не оставляет выбранным несуществующий сегмент.</summary>
    [Fact]
    public void TabsClampSelectionWhenItemsShrink()
    {
        using var host = new Host();
        var tabs = host.Add(new SegmentedTabs { Items = ["Неделя", "Месяц", "Год"], Selected = 2 });

        tabs.Items = ["Неделя"];

        Assert.Equal(0, tabs.Selected);
        Render(tabs);
    }

    // ---- поле времени -------------------------------------------------------------------

    /// <summary>Время набирают по-разному, и все три способа разбираются.</summary>
    [Theory]
    [InlineData("09:12", 9, 12)]
    [InlineData("9:05", 9, 5)]
    [InlineData("0905", 9, 5)]
    [InlineData(" 23:59 ", 23, 59)]
    public void TimeFieldParsesWhatPeopleType(string typed, int hour, int minute)
    {
        using var host = new Host();
        var field = host.Add(new DesignField { Kind = FieldKind.Time });
        field.Type(typed);

        Assert.Equal(new TimeOnly(hour, minute), field.Time);
        Assert.True(field.IsValid);
    }

    /// <summary>Недобранное и невозможное время значением не становится.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("9")]
    [InlineData("25:00")]
    [InlineData("12:70")]
    public void TimeFieldRefusesNonsense(string typed)
    {
        using var host = new Host();
        var field = host.Add(new DesignField { Kind = FieldKind.Time });
        field.Type(typed);

        Assert.Null(field.Time);
        Assert.False(field.IsValid);
    }

    /// <summary>
    /// Отказ снаружи и неразобранный текст — разные вещи, но красным поле становится
    /// в обоих случаях, а набранное заново снимает отказ.
    /// </summary>
    [Fact]
    public void RejectedValueClearsWhenRetyped()
    {
        using var host = new Host();
        var field = host.Add(new DesignField { Kind = FieldKind.Time });
        field.Time = new TimeOnly(12, 40);

        field.Rejected = true;
        Assert.False(field.IsValid);
        Assert.NotNull(field.Time);

        field.Type("13:31");
        Assert.False(field.Rejected);
        Assert.True(field.IsValid);
    }

    /// <summary>Заполнение кодом — не правка человека, и событием не притворяется.</summary>
    [Fact]
    public void FillingDoesNotLookLikeEditing()
    {
        using var host = new Host();
        var field = host.Add(new DesignField { Kind = FieldKind.Time });

        var edits = 0;
        field.ValueChanged += (_, _) => edits++;

        field.Time = new TimeOnly(9, 12);
        Assert.Equal(0, edits);

        field.Type("10:00");
        Assert.True(edits > 0);
    }

    /// <summary>Все четыре состояния поля рисуются в любой теме.</summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.System)]
    public void TimeFieldPaintsInEveryTheme(AppTheme theme)
    {
        WindowTheme.Apply(theme);

        using var host = new Host();
        foreach (var (rejected, enabled) in new[] { (false, true), (true, true), (false, false) })
        {
            var field = host.Add(new DesignField { Kind = FieldKind.Time, Rejected = rejected, Enabled = enabled });
            field.Time = new TimeOnly(9, 12);
            Render(field);
        }
    }

    // ---- список ---------------------------------------------------------------------------

    /// <summary>
    /// Строка держит свою высоту при любом размере окна. Дизайн настаивает на этом даже
    /// в самом узком варианте: ниже 38 мышь начинает промахиваться.
    /// </summary>
    [Fact]
    public void RowHeightSurvivesEveryWindowSize()
    {
        using var host = new Host();
        var list = host.Add(new Rows { Count = 4, Size = new Size(1180, 600) });
        var wide = list.Row;

        list.Size = new Size(200, 120);

        Assert.Equal(wide, list.Row);
        Assert.Equal(Metrics.Of(list).RowHeight, list.Row);
    }

    /// <summary>Стрелки ходят по строкам и не уходят за края.</summary>
    [Fact]
    public void ListWalksWithTheArrows()
    {
        using var host = new Host();
        var list = host.Add(new Rows { Count = 3, Size = new Size(400, 400), SelectedIndex = 0 });

        var changes = 0;
        list.SelectionChanged += (_, _) => changes++;

        Press(list, Keys.Down);
        Assert.Equal(1, list.SelectedIndex);

        Press(list, Keys.End);
        Assert.Equal(2, list.SelectedIndex);

        Press(list, Keys.Down);
        Assert.Equal(2, list.SelectedIndex);

        Press(list, Keys.Home);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(3, changes);
    }

    /// <summary>Enter на выбранной строке открывает её.</summary>
    [Fact]
    public void EnterOpensTheSelectedRow()
    {
        using var host = new Host();
        var list = host.Add(new Rows { Count = 3, Size = new Size(400, 400), SelectedIndex = 1 });

        var opened = 0;
        list.RowActivated += (_, _) => opened++;

        Press(list, Keys.Enter);

        Assert.Equal(1, opened);
    }

    /// <summary>Укоротившийся список не оставляет выбранной строку, которой больше нет.</summary>
    [Fact]
    public void SelectionSurvivesTheListShrinking()
    {
        using var host = new Host();
        var list = host.Add(new Rows { Count = 5, Size = new Size(400, 400), SelectedIndex = 4 });

        list.Count = 2;
        Assert.Equal(1, list.SelectedIndex);

        list.Count = 0;
        Assert.Equal(-1, list.SelectedIndex);
    }

    /// <summary>Список рисуется и пустым, и не помещающимся, и в крошечном окне.</summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.System)]
    public void ListPaintsInEveryTheme(AppTheme theme)
    {
        WindowTheme.Apply(theme);

        using var host = new Host();
        Render(host.Add(new Rows { Count = 0, Size = new Size(400, 200) }));
        Render(host.Add(new Rows { Count = 40, Size = new Size(400, 200), SelectedIndex = 20 }));
        Render(host.Add(new Rows { Count = 4, Size = new Size(12, 9) }));
    }

    // ---- карточка ----------------------------------------------------------------------------

    /// <summary>Заголовок карточки отодвигает содержимое, а карточка без него — нет.</summary>
    [Fact]
    public void CardHeaderPushesTheContentDown()
    {
        using var host = new Host();

        var titled = host.Add(new DesignCard
        {
            Label = "Учёт времени",
            Note = "Пояснение",
            Size = new Size(400, 200),
        });

        var plain = host.Add(new DesignCard { Size = new Size(400, 200) });

        Assert.True(titled.ContentBounds.Top > plain.ContentBounds.Top);
        Assert.True(plain.ContentBounds.Left > 0);
    }

    /// <summary>Карточка без рамки прижимает содержимое к краю: она только заголовок и отступы.</summary>
    [Fact]
    public void BareCardHasNoInset()
    {
        using var host = new Host();
        var bare = host.Add(new DesignCard { Bare = true, Size = new Size(400, 200) });

        Assert.Equal(0, bare.ContentBounds.Left);
    }

    /// <summary>Карточка рисуется в любой теме — и с заголовком, и без.</summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.System)]
    public void CardPaintsInEveryTheme(AppTheme theme)
    {
        WindowTheme.Apply(theme);

        using var host = new Host();
        Render(host.Add(new DesignCard { Label = "Учёт времени", Note = "Пояснение", Size = new Size(400, 200) }));
        Render(host.Add(new DesignCard { Bare = true, Size = new Size(400, 200) }));
        Render(host.Add(new DesignCard { Size = new Size(4, 4) }));
    }

    // ---- рисование ------------------------------------------------------------------------------

    /// <summary>
    /// Скругление не бывает больше половины меньшей стороны: иначе дуги перехлёстываются
    /// и контур выворачивается наизнанку.
    /// </summary>
    [Theory]
    [InlineData(40, 30, 10)]
    [InlineData(8, 8, 40)]
    [InlineData(1, 1, 6)]
    public void RoundedPathStaysInsideItsBounds(int width, int height, int radius)
    {
        var bounds = new Rectangle(5, 7, width, height);
        using var path = Draw.RoundedPath(bounds, radius);

        var box = Rectangle.Round(path.GetBounds());

        Assert.True(box.Left >= bounds.Left);
        Assert.True(box.Top >= bounds.Top);
        Assert.True(box.Right <= bounds.Right);
        Assert.True(box.Bottom <= bounds.Bottom);
    }

    /// <summary>Прозрачная грань означает «не рисовать», а не «нарисовать прозрачным».</summary>
    [Fact]
    public void TransparentFaceDrawsNothing()
    {
        var face = new Face(Color.Transparent, Color.Transparent, Color.Red);

        Assert.False(face.HasBack);
        Assert.False(face.HasLine);

        using var bitmap = new Bitmap(20, 20);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Lime);

        Draw.Surface(graphics, new Rectangle(0, 0, 20, 20), face, 6, 1);

        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
    }

    // ---- вспомогательное ---------------------------------------------------------------------------

    /// <summary>Шлёт клавишу тем же сообщением, каким её присылает система.</summary>
    private static void Press(Control control, Keys key) =>
        SendMessageW(control.Handle, WmKeyDown, (nint)key, 0);

    private static void Render(Control control)
    {
        using var bitmap = new Bitmap(Math.Max(control.Width, 1), Math.Max(control.Height, 1));
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    }

    /// <summary>Окно, в котором примитивы получают хендл и родительский фон.</summary>
    private sealed class Host : Form
    {
        public Host()
        {
            _ = Handle;
        }

        public T Add<T>(T control)
            where T : Control
        {
            Controls.Add(control);
            _ = control.Handle;
            return control;
        }
    }

    /// <summary>Список без содержимого строк: проверяется поведение, а не то, что в строках.</summary>
    private sealed class Rows : DesignList
    {
        /// <summary>Высота строки: у списка она защищённая, а проверять её надо.</summary>
        public int Row => RowHeight;

        protected override void PaintRow(
            Graphics graphics,
            int index,
            Rectangle bounds,
            Palette palette,
            RowState state)
        {
            TextRenderer.DrawText(graphics, $"строка {index}", Fonts.Body, bounds, RowInk(palette, state));
        }
    }
}

/// <summary>Набор текста в поле — как его набирает человек.</summary>
internal static class FieldTyping
{
    /// <summary>Набирает текст в поле так, будто его ввели с клавиатуры.</summary>
    public static void Type(this DesignField field, string text)
    {
        ArgumentNullException.ThrowIfNull(field);

        var input = field.Controls.OfType<TextBox>().Single();
        input.Text = text;
    }
}
