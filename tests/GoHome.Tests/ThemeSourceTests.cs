using GoHome.Core;
using GoHome.Interop;
using GoHome.Ui;
using GoHome.Ui.Design;

namespace GoHome.Tests;

/// <summary>
/// Оформление приложения задаётся из одного места на процесс, поэтому проверки, его
/// трогающие, идут по очереди: параллельные тесты иначе перекрывают друг другу тему.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UiThemeCollection
{
    /// <summary>Имя очереди.</summary>
    public const string Name = "оформление приложения";
}

/// <summary>
/// Откуда берутся цвета: у окон — из настройки приложения, у кольца в трее — из оформления
/// панели задач.
/// </summary>
/// <remarks>
/// Два независимых источника, и смешивать их нельзя. Настройка, не влияющая на окна, — это
/// обещание, которого приложение не держит; кольцо, следующее за настройкой, становится
/// невидимым на панели противоположного оформления.
/// </remarks>
[Collection(UiThemeCollection.Name)]
public sealed class ThemeSourceTests : IDisposable
{
    public void Dispose() => WindowTheme.Apply(AppTheme.System);

    /// <summary>Светлая тема в настройке даёт светлые окна — какой бы ни была система.</summary>
    [Fact]
    public void LightSettingWinsOverTheSystem()
    {
        if (SystemTheme.IsHighContrast())
        {
            return;
        }

        WindowTheme.Apply(AppTheme.Light);

        Assert.Same(Palette.Light, Palette.Current());
    }

    /// <summary>И тёмная тоже.</summary>
    [Fact]
    public void DarkSettingWinsOverTheSystem()
    {
        if (SystemTheme.IsHighContrast())
        {
            return;
        }

        WindowTheme.Apply(AppTheme.Dark);

        Assert.Same(Palette.Dark, Palette.Current());
    }

    /// <summary>Настройка «как в системе» идёт за оформлением окон Windows, а не панели задач.</summary>
    [Fact]
    public void SystemSettingFollowsWindows()
    {
        if (SystemTheme.IsHighContrast())
        {
            return;
        }

        WindowTheme.Apply(AppTheme.System);

        var expected = SystemTheme.IsDarkWindows() ? Palette.Dark : Palette.Light;
        Assert.Same(expected, Palette.Current());
    }

    /// <summary>
    /// Кольцо в трее настройки не видит вовсе. Оно живёт на панели задач и обязано следовать
    /// её оформлению: принудительно тёмное кольцо на светлой панели снова станет невидимым.
    /// </summary>
    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void RingIgnoresTheApplicationSetting(AppTheme theme)
    {
        var expected = TrayRing.CurrentPalette;

        WindowTheme.Apply(theme);

        Assert.Equal(expected, TrayRing.CurrentPalette);
    }

    /// <summary>
    /// Уже открытое окно меняет фон сразу, а не при следующем открытии.
    /// </summary>
    /// <remarks>
    /// Самый неприятный вид расхождения: нарисованное внутри окна перечитывает палитру само
    /// на отрисовке, а фон окна берётся из <see cref="Control.BackColor"/> и без обхода
    /// дерева остаётся прежним. Окно оказывается тёмным со светлым содержимым.
    /// </remarks>
    [Fact]
    public void OpenWindowChangesWithTheSetting()
    {
        if (SystemTheme.IsHighContrast())
        {
            return;
        }

        WindowTheme.Apply(AppTheme.Dark);

        using var form = new Sample();
        form.Show();

        try
        {
            Assert.Equal(Palette.Dark.Window, form.BackColor);

            WindowTheme.Apply(AppTheme.Light);
            Assert.Equal(Palette.Light.Window, form.BackColor);

            WindowTheme.Apply(AppTheme.Dark);
            Assert.Equal(Palette.Dark.Window, form.BackColor);
        }
        finally
        {
            form.Close();
        }
    }

    /// <summary>
    /// Карточка внутри окна тоже перечитывает палитру: дети берут фон у неё, и без этого
    /// кнопка внутри карточки затирала бы свой угол цветом окна.
    /// </summary>
    [Fact]
    public void CardInsideTheWindowFollowsToo()
    {
        if (SystemTheme.IsHighContrast())
        {
            return;
        }

        WindowTheme.Apply(AppTheme.Dark);

        using var form = new Sample();
        var card = new DesignCard { Size = new Size(200, 100) };
        form.Place(card);
        form.Show();

        try
        {
            Assert.Equal(Palette.Dark.Card, card.BackColor);

            WindowTheme.Apply(AppTheme.Light);
            Assert.Equal(Palette.Light.Card, card.BackColor);
        }
        finally
        {
            form.Close();
        }
    }

    /// <summary>
    /// Панель задач и окна оформляются в Windows по отдельности, и приложение спрашивает
    /// про них разные значения.
    /// </summary>
    [Fact]
    public void WindowsAndTaskbarAreAskedSeparately()
    {
        // Значения могут совпадать — важно, что читаются они из разных мест и одно
        // не подставляется вместо другого.
        _ = SystemTheme.IsDarkWindows();
        _ = SystemTheme.IsDarkTaskbar();

        Assert.NotEqual(nameof(SystemTheme.IsDarkWindows), nameof(SystemTheme.IsDarkTaskbar));
    }

    /// <summary>Окно за пределами экрана: проверяется смена темы, а не то, что в нём.</summary>
    private sealed class Sample : DesignForm
    {
        public Sample()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-4000, -4000);
            ShowInTaskbar = false;
        }

        public void Place(Control control) => Content.Controls.Add(control);
    }
}
