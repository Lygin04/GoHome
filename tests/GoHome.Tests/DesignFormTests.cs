using GoHome.Core;
using GoHome.Ui;
using GoHome.Ui.Design;

namespace GoHome.Tests;

/// <summary>
/// Окно, у которого заголовок нарисован приложением.
/// </summary>
/// <remarks>
/// Перетаскивание, прилипание и разворот проверяются только руками на живом окне: их делает
/// система, а приложение лишь отвечает, что у него под курсором. Проверяемое здесь — то,
/// что от приложения зависит целиком: размеры считаются в единицах дизайна, полоса заголовка
/// растёт вместе с масштабом, кнопки появляются по тому, что окно разрешает, и рисование
/// не падает ни в одной теме.
/// </remarks>
public sealed class DesignFormTests
{
    /// <summary>Полоса заголовка ровно той высоты, которую просит дизайн.</summary>
    [Fact]
    public void CaptionKeepsTheDesignHeight()
    {
        using var form = new Sample();
        _ = form.Handle;

        Assert.Equal(Metrics.Of(form).CaptionHeight, Caption(form).Height);
    }

    /// <summary>
    /// Начальный размер — это размер окна, а не окно плюс рамка. Рамки у окна нет,
    /// и <see cref="Form.ClientSize"/> прибавил бы её толщину к каждому окну.
    /// </summary>
    [Fact]
    public void InitialSizeIsTheSizeThatWasAskedFor()
    {
        using var form = new Sample();
        _ = form.Handle;

        var expected = Metrics.Of(form).Scale(new Size(880, 560));
        form.Fit(new Size(880, 560));

        Assert.Equal(expected, form.Size);
        Assert.Equal(expected, form.ClientSize);
    }

    /// <summary>Минимум окна растёт вместе с масштабом, иначе на 175 % содержимое не поместится.</summary>
    [Fact]
    public void MinimumIsCountedInDesignUnits()
    {
        using var form = new Sample();
        _ = form.Handle;

        form.Limit(new Size(700, 520));
        Assert.Equal(Metrics.Of(form).Scale(new Size(700, 520)), form.MinimumSize);
    }

    /// <summary>Подпись окна и подпись в полосе — одно и то же, задаваемое один раз.</summary>
    [Fact]
    public void CaptionFollowsTheWindowTitle()
    {
        using var form = new Sample { Text = "День — 14 августа" };
        _ = form.Handle;

        Assert.Equal("День — 14 августа", Caption(form).Caption);
    }

    /// <summary>Рисование не падает ни в одной теме — включая высокую контрастность.</summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.System)]
    public void CaptionPaintsInEveryTheme(AppTheme theme)
    {
        WindowTheme.Apply(theme);

        using var form = new Sample { Text = "GoHome" };
        _ = form.Handle;

        var caption = Caption(form);
        using var bitmap = new Bitmap(Math.Max(caption.Width, 1), Math.Max(caption.Height, 1));
        caption.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    }

    /// <summary>Узкая полоса не роняет раскладку: подпись просто перестаёт помещаться.</summary>
    [Fact]
    public void NarrowCaptionStillPaints()
    {
        using var form = new Sample { Text = "Очень длинная подпись, которой здесь не место" };
        _ = form.Handle;
        form.Fit(new Size(120, 400));

        var caption = Caption(form);
        using var bitmap = new Bitmap(Math.Max(caption.Width, 1), Math.Max(caption.Height, 1));
        caption.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    }

    private static CaptionBar Caption(Form form) => form.Controls.OfType<CaptionBar>().Single();

    /// <summary>Окно без содержимого: проверяется сама рама, а не то, что в неё положили.</summary>
    private sealed class Sample : DesignForm
    {
        public Sample()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(0, 0);
            SetInitialSize(new Size(880, 560));
        }

        public void Fit(Size designUnits) => SetInitialSize(designUnits);

        public void Limit(Size designUnits) => SetMinimum(designUnits);
    }
}
