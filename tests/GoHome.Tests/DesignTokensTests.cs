using GoHome.Ui.Design;

namespace GoHome.Tests;

/// <summary>
/// Проверки основания дизайна.
/// </summary>
/// <remarks>
/// Смысл этих проверок не в том, что цвет равен константе — константу видно и в файле.
/// Смысл в свойствах, которые легко нечаянно нарушить: контраст акцента в светлой теме,
/// пересчёт по масштабу, приоритет угла над стороной при растяжении окна, моноширинность
/// шрифта чисел. Каждое из них уже было причиной видимой поломки в подобных переносах.
/// </remarks>
public sealed class DesignTokensTests
{
    /// <summary>
    /// Акцент в светлой теме темнее, чем в тёмной. Дизайн называет это требованием контраста,
    /// а не вкусом: поменять их местами — значит потерять акцент на белом фоне.
    /// </summary>
    [Fact]
    public void LightAccentIsDarkerThanDarkAccent()
    {
        Assert.True(Luminance(Palette.Light.Accent) < Luminance(Palette.Dark.Accent));
        Assert.True(Luminance(Palette.Light.Goal) < Luminance(Palette.Dark.Goal));
    }

    /// <summary>Текст читается на своём фоне в обеих темах.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InkContrastsWithItsSurface(bool dark)
    {
        var palette = dark ? Palette.Dark : Palette.Light;

        Assert.True(Contrast(palette.Ink, palette.Window) >= 7d);
        Assert.True(Contrast(palette.Ink, palette.Card) >= 7d);
        Assert.True(Contrast(palette.Muted, palette.Window) >= 4.5d);
        Assert.True(Contrast(palette.Muted, palette.Card) >= 4.5d);

        // Основная кнопка — единственное место, где текст лежит на акценте.
        Assert.True(Contrast(palette.PrimaryButton.Rest.Ink, palette.PrimaryButton.Rest.Back) >= 4.5d);
    }

    /// <summary>
    /// Карточка отличается от окна. На этой разнице держится замена теней: если фоны совпадут,
    /// карточка перестанет читаться как отдельная — а тени, которой она была бы обведена
    /// в вебе, здесь нет.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CardStandsOutFromWindow(bool dark)
    {
        var palette = dark ? Palette.Dark : Palette.Light;

        Assert.NotEqual(palette.Card, palette.Window);
        Assert.NotEqual(palette.Line, palette.LineSoft);
    }

    /// <summary>
    /// «Не засчитано» отличается от «работа» и от «цель взята» — иначе полоса дня врёт.
    /// Проверяется и смешанный с фоном вариант засчитанной отлучки: он рисуется тем же
    /// акцентом, приглушённым до 0.42, и не должен сливаться с «не засчитано».
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BreakColorsStayApart(bool dark)
    {
        var palette = dark ? Palette.Dark : Palette.Light;
        var faded = Palette.Blend(palette.Accent, palette.Track, 0.42);

        Assert.NotEqual(palette.Unpaid, palette.Accent);
        Assert.NotEqual(palette.Unpaid, palette.Goal);
        Assert.True(Distance(faded, palette.Unpaid) > 24d);
    }

    /// <summary>Высокая контрастность берёт цвета у системы, а не из своих двух палитр.</summary>
    [Fact]
    public void HighContrastBorrowsSystemColors()
    {
        // Обе палитры непригодны в этом режиме целиком: тему выбрал человек.
        Assert.NotEqual(Palette.Dark.Window, SystemColors.Window);
        Assert.NotEqual(Palette.Light.Ink, SystemColors.WindowText);

        var contrast = Palette.HighContrast();

        Assert.Equal(SystemColors.Window, contrast.Window);
        Assert.Equal(SystemColors.WindowText, contrast.Ink);
        Assert.Equal(SystemColors.Highlight, contrast.Accent);
        Assert.Equal(SystemColors.GrayText, contrast.Faint);
    }

    /// <summary>
    /// В палитре высокой контрастности заполнено всё.
    /// </summary>
    /// <remarks>
    /// Незаполненная роль здесь означает прозрачный или чёрный цвет посреди окна, и увидит
    /// это первым тот, кому этот режим и нужен. Проверяется каждое свойство разом,
    /// а не выборочно: забыть одно при добавлении новой роли слишком легко.
    /// </remarks>
    [Fact]
    public void HighContrastLeavesNothingUnset()
    {
        var contrast = Palette.HighContrast();

        foreach (var property in typeof(Palette).GetProperties())
        {
            var value = property.GetValue(contrast);

            if (value is Color colour)
            {
                Assert.True(colour.A != 0, $"роль «{property.Name}» осталась прозрачной");
            }

            Assert.NotNull(value);
        }

        Assert.Equal(4, contrast.Heat.Count);
        Assert.All(contrast.Heat, colour => Assert.NotEqual(0, (int)colour.A));
    }

    /// <summary>Смешивание доходит до концов и ничего не теряет по дороге.</summary>
    [Fact]
    public void BlendReachesBothEnds()
    {
        var over = Color.FromArgb(0x60, 0xA5, 0xFA);
        var under = Color.FromArgb(0x27, 0x2C, 0x34);

        Assert.Equal(over.ToArgb(), Palette.Blend(over, under, 1d).ToArgb());
        Assert.Equal(under.ToArgb(), Palette.Blend(over, under, 0d).ToArgb());

        // За пределами отрезка результат не выходит: доля зажимается, а не экстраполируется.
        Assert.Equal(over.ToArgb(), Palette.Blend(over, under, 4d).ToArgb());
        Assert.Equal(under.ToArgb(), Palette.Blend(over, under, -1d).ToArgb());
    }

    /// <summary>
    /// Закрытая норма красится своим цветом, а не верхней ступенью шкалы: «много» и «хватило»
    /// человек читает по-разному, и в тепловой карте это разные клетки.
    /// </summary>
    [Fact]
    public void ReachedGoalIsNotJustTheTopStep()
    {
        var palette = Palette.Dark;

        Assert.Equal(palette.Goal, palette.HeatColor(0.4d, goalReached: true));
        Assert.Equal(palette.HeatEmpty, palette.HeatColor(0d, goalReached: false));
        Assert.Equal(palette.Heat[^1], palette.HeatColor(1d, goalReached: false));
        Assert.NotEqual(palette.Goal, palette.Heat[^1]);
    }

    /// <summary>Ступени шкалы идут в одну сторону и не повторяются.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeatStepsRiseMonotonically(bool dark)
    {
        var palette = dark ? Palette.Dark : Palette.Light;
        var steps = palette.Heat.Select(Luminance).ToList();

        Assert.Equal(4, steps.Count);

        // В тёмной теме «больше» — светлее, в светлой — темнее. Направление разное, порядок один.
        var rising = dark ? steps : steps.AsEnumerable().Reverse().ToList();
        Assert.Equal(rising.Order(), rising);
        Assert.Equal(steps.Count, steps.Distinct().Count());
    }

    /// <summary>Единица дизайна равна пикселю при ста процентах и растёт вместе с масштабом.</summary>
    [Theory]
    [InlineData(96, 38)]
    [InlineData(120, 48)]
    [InlineData(144, 57)]
    [InlineData(168, 67)]
    public void MetricsFollowTheScale(int dpi, int expected)
    {
        Assert.Equal(expected, new Metrics(dpi).Scale(38));
    }

    /// <summary>
    /// Строка списка держит свою высоту на любом масштабе, а минимум окна растёт вместе с ним:
    /// иначе на 175 % окно откроется, а содержимое в него не поместится.
    /// </summary>
    [Fact]
    public void WindowMinimumsGrowWithTheScale()
    {
        var normal = new Metrics(96);
        var large = new Metrics(168);

        Assert.Equal(new Size(700, 520), normal.DayMinimum);
        Assert.True(large.DayMinimum.Width > normal.DayMinimum.Width);
        Assert.True(large.DayMinimum.Height > normal.DayMinimum.Height);
        Assert.Equal(38, normal.RowHeight);
    }

    /// <summary>
    /// Квадрат растяжения в углу шире полосы вдоль стороны. Если это перестанет быть так,
    /// угол достанется стороне и окно перестанет тянуться по диагонали — про этот угол
    /// забывают чаще всего.
    /// </summary>
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    public void ResizeCornerIsWiderThanResizeBorder(int dpi)
    {
        var metrics = new Metrics(dpi);
        Assert.True(metrics.ResizeCorner > metrics.ResizeBorder);
    }

    /// <summary>Шаг сетки отступов — только шесть значений дизайна, без промежуточных.</summary>
    [Fact]
    public void SpacingKeepsToTheDesignScale()
    {
        var metrics = new Metrics(96);
        Assert.Equal([4, 8, 12, 16, 24, 32], Enumerable.Range(1, 6).Select(metrics.Space));
    }

    /// <summary>
    /// Числа рисуются моноширинным шрифтом. Счётчик обновляется раз в минуту, и на
    /// пропорциональном шрифте строка дёргалась бы на каждой смене цифры.
    /// </summary>
    [Fact]
    public void NumbersUseAMonospacedFont()
    {
        var fonts = Typography.Of(new Metrics(96));
        var widths = "0123456789".Select(digit => Width(digit, fonts.Number)).Distinct();

        Assert.Single(widths);
        Assert.Equal(Width('0', fonts.Counter), Width('8', fonts.Counter));
    }

    /// <summary>Иерархия кеглей сохраняется: счётчик крупнее числа карточки и так далее вниз.</summary>
    [Fact]
    public void TypeScaleKeepsItsOrder()
    {
        var fonts = Typography.Of(new Metrics(96));

        Assert.True(fonts.Counter.Size > fonts.CardNumber.Size);
        Assert.True(fonts.CardNumber.Size > fonts.Projection.Size);
        Assert.True(fonts.Heading.Size > fonts.Body.Size);
        Assert.True(fonts.Body.Size > fonts.Caption.Size);
        Assert.True(fonts.Caption.Size > fonts.Tick.Size);

        // Узкое окно понижает шкалу, но не переворачивает её.
        Assert.True(fonts.CounterNarrow.Size < fonts.Counter.Size);
        Assert.True(fonts.CounterNarrow.Size > fonts.CardNumber.Size);
    }

    /// <summary>Набор шрифтов на масштаб один и тот же — шрифт нельзя создавать на каждой отрисовке.</summary>
    [Fact]
    public void FontsAreSharedPerScale()
    {
        Assert.Same(Typography.Of(new Metrics(96)), Typography.Of(new Metrics(96)));
        Assert.NotSame(Typography.Of(new Metrics(96)), Typography.Of(new Metrics(144)));
    }

    /// <summary>Метка секции шире того же текста без разрядки — иначе разрядка не применилась.</summary>
    [Fact]
    public void LabelIsTracked()
    {
        var fonts = Typography.Of(new Metrics(96));
        const string text = "УЧЁТ ВРЕМЕНИ";

        var plain = TextRenderer.MeasureText(text, fonts.Label, Size.Empty, TextFormatFlags.NoPadding).Width;
        Assert.True(fonts.MeasureLabel(text) > plain);
        Assert.Equal(0, fonts.MeasureLabel(string.Empty));
    }

    private static int Width(char glyph, Font font) =>
        TextRenderer.MeasureText(glyph.ToString(), font, Size.Empty, TextFormatFlags.NoPadding).Width;

    /// <summary>Относительная яркость по WCAG — та же формула, по которой считают контраст.</summary>
    private static double Luminance(Color color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var part = value / 255d;
        return part <= 0.03928d ? part / 12.92d : Math.Pow((part + 0.055d) / 1.055d, 2.4d);
    }

    private static double Contrast(Color first, Color second)
    {
        var one = Luminance(first);
        var other = Luminance(second);
        return (Math.Max(one, other) + 0.05d) / (Math.Min(one, other) + 0.05d);
    }

    private static double Distance(Color first, Color second) => Math.Sqrt(
        Math.Pow(first.R - second.R, 2)
        + Math.Pow(first.G - second.G, 2)
        + Math.Pow(first.B - second.B, 2));
}
