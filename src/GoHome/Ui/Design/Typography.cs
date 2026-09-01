using System.Collections.Concurrent;
using System.Drawing.Text;

namespace GoHome.Ui.Design;

/// <summary>
/// Шрифты дизайна, готовые под конкретный масштаб экрана.
/// </summary>
/// <remarks>
/// В дизайне два веб-шрифта — IBM Plex Sans и JetBrains Mono, и сам дизайн подписывает их
/// как метрических двойников системных. Веб-шрифты в сборку не тащим: текст рисуется
/// интерфейсным шрифтом Windows, числа — моноширинным из поставки системы.
/// <para>
/// Числа обязательно моноширинные. Счётчик обновляется раз в минуту, и пропорциональная
/// единица заставила бы строку дёргаться на каждой смене цифры. Табличные цифры при этом
/// включать нечем и незачем: у моноширинного шрифта все цифры и так одной ширины.
/// </para>
/// <para>
/// Начертаний доступно два — обычное и полужирное из отдельного семейства. Дизайн просит
/// четыре: 400, 500, 600, 700. Средних между ними в поставке нет, поэтому 500 округляется
/// вниз до обычного, а 700 — вниз до полужирного. Там, где дизайн отделял 500 от 400, вес
/// всегда идёт вместе с цветом или заливкой, так что различие не теряется.
/// </para>
/// <para>
/// Размеры заданы в пикселях, а не в пунктах, и умножаются на масштаб вручную. Пункты
/// пересчитала бы сама система, но по своему округлению — а числа дизайна должны совпадать
/// с сеткой <see cref="Metrics"/> ровно, иначе строка в 38 единиц перестанет вмещать текст.
/// </para>
/// </remarks>
internal sealed class Typography
{
    /// <summary>Насколько разрежена метка секции. Из дизайна: 0.13em.</summary>
    private const double LabelTracking = 0.13;

    private static readonly ConcurrentDictionary<int, Typography> Cache = [];

    private static readonly string SansFamily = FirstInstalled("Segoe UI Variable Text", "Segoe UI");
    private static readonly string SansStrongFamily =
        FirstInstalled("Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");

    private static readonly string MonoFamily = FirstInstalled("Cascadia Mono", "Consolas");
    private static readonly string MonoStrongFamily =
        FirstInstalled("Cascadia Mono SemiBold", "Consolas");

    private readonly Metrics _metrics;

    private Typography(Metrics metrics)
    {
        _metrics = metrics;

        Counter = Mono(38, strong: true);
        CounterNarrow = Mono(32, strong: true);
        CardNumber = Mono(27, strong: true);
        CardNumberNarrow = Mono(25, strong: true);
        Projection = Mono(19, strong: true);
        ProjectionNarrow = Mono(17, strong: true);

        Heading = Sans(20, strong: true);
        Large = Sans(15);
        Title = Sans(14);
        TitleNarrow = Sans(13.5);
        Body = Sans(13);
        Control = Sans(12.5);
        Note = Sans(12);
        Caption = Sans(11.5);
        Small = Sans(11);

        Number = Mono(12.5);
        NumberNarrow = Mono(12);
        Label = Mono(10.5, strong: true);
        Tick = Mono(10);
    }

    /// <summary>Шрифты под тот масштаб, в котором рисует этот контрол.</summary>
    public static Typography Of(Control control) => Of(Metrics.Of(control));

    /// <inheritdoc cref="Of(Control)"/>
    /// <remarks>
    /// Набор кешируется на весь запуск и не освобождается: разных масштабов на машине единицы,
    /// а освобождать шрифт, который прямо сейчас может рисовать другое окно, нельзя.
    /// </remarks>
    public static Typography Of(Metrics metrics) =>
        Cache.GetOrAdd(metrics.Dpi, dpi => new Typography(new Metrics(dpi)));

    /// <summary>Счётчик дня — самое крупное число в приложении.</summary>
    public Font Counter { get; }

    /// <inheritdoc cref="Counter"/>
    public Font CounterNarrow { get; }

    /// <summary>Число в карточке статистики.</summary>
    public Font CardNumber { get; }

    /// <inheritdoc cref="CardNumber"/>
    public Font CardNumberNarrow { get; }

    /// <summary>Прогноз ухода рядом со счётчиком.</summary>
    public Font Projection { get; }

    /// <inheritdoc cref="Projection"/>
    public Font ProjectionNarrow { get; }

    /// <summary>Заголовок раздела.</summary>
    public Font Heading { get; }

    /// <summary>Крупный текст.</summary>
    public Font Large { get; }

    /// <summary>Заголовок дня, название строки настроек.</summary>
    public Font Title { get; }

    /// <inheritdoc cref="Title"/>
    public Font TitleNarrow { get; }

    /// <summary>Основной текст.</summary>
    public Font Body { get; }

    /// <summary>Кнопка, вкладка, значение поля.</summary>
    public Font Control { get; }

    /// <summary>Пояснение под названием настройки.</summary>
    public Font Note { get; }

    /// <summary>Подпись, легенда.</summary>
    public Font Caption { get; }

    /// <summary>Самая мелкая подпись: день под столбцом графика.</summary>
    public Font Small { get; }

    /// <summary>Число в строке списка.</summary>
    public Font Number { get; }

    /// <inheritdoc cref="Number"/>
    public Font NumberNarrow { get; }

    /// <summary>Метка секции: прописные, разреженные. Рисовать через <see cref="DrawLabel"/>.</summary>
    public Font Label { get; }

    /// <summary>Метка часа на оси.</summary>
    public Font Tick { get; }

    /// <summary>
    /// Рисует метку секции — прописными и вразрядку.
    /// </summary>
    /// <remarks>
    /// Разрядки GDI+ не умеет, поэтому знаки ставятся по одному. Для метки это дёшево:
    /// шрифт моноширинный, ширина знака одна на всех, и мерить её достаточно единожды.
    /// Обратной разрядки у крупных чисел дизайн тоже просит, но там её нет намеренно —
    /// сжать моноширинный шрифт значит сломать выравнивание колонок, ради которого он и взят.
    /// </remarks>
    /// <returns>Ширина нарисованного, чтобы вызывающий знал, где продолжать.</returns>
    public int DrawLabel(Graphics graphics, string text, Point at, Color color)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(text);

        var upper = text.ToUpperInvariant();
        var advance = LabelAdvance();
        var x = at.X;

        foreach (var glyph in upper)
        {
            TextRenderer.DrawText(
                graphics,
                glyph.ToString(),
                Label,
                new Point(x, at.Y),
                color,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            x += advance;
        }

        return upper.Length == 0 ? 0 : x - at.X - _metrics.Scale((int)Math.Round(10.5 * LabelTracking));
    }

    /// <summary>Ширина метки секции с учётом разрядки — чтобы отвести под неё место заранее.</summary>
    public int MeasureLabel(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? 0 : text.Length * LabelAdvance() - Tracking();
    }

    private int LabelAdvance() =>
        TextRenderer.MeasureText("0", Label, Size.Empty, TextFormatFlags.NoPadding).Width + Tracking();

    private int Tracking() => Math.Max(1, _metrics.Scale((int)Math.Round(10.5 * LabelTracking)));

    private Font Sans(double px, bool strong = false) =>
        new(strong ? SansStrongFamily : SansFamily, _metrics.Exact(px), GraphicsUnit.Pixel);

    private Font Mono(double px, bool strong = false) =>
        new(strong ? MonoStrongFamily : MonoFamily, _metrics.Exact(px), GraphicsUnit.Pixel);

    /// <summary>
    /// Первое установленное семейство из списка.
    /// </summary>
    /// <remarks>
    /// <c>Segoe UI Variable</c> и <c>Cascadia Mono</c> есть в Windows 11, но не в каждой
    /// Windows 10, а приложение ставится на корпоративные машины. Запрос отсутствующего
    /// семейства не падает, а молча подставляет что придётся — поэтому список проверяется,
    /// а не задаётся строкой с запятыми.
    /// </remarks>
    private static string FirstInstalled(params string[] families)
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Array.Find(families, names.Contains) ?? families[^1];
    }
}
