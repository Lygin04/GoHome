namespace GoHome.Ui.Design;

/// <summary>
/// Размеры дизайна, пересчитанные под текущий масштаб экрана.
/// </summary>
/// <remarks>
/// Все числа в <c>docs/design/GoHome UI.dc.html</c> — единицы при ста процентах. Задавать их
/// в пикселях напрямую нельзя: на мониторе со масштабом 125 или 150 всё поедет, а при переносе
/// окна на другой монитор поедет ещё раз. Поэтому размер берётся отсюда, а не пишется числом.
/// <para>
/// Считается от <see cref="Control.DeviceDpi"/> того контрола, который рисует, а не от экрана:
/// при <c>PerMonitorV2</c> у окон на разных мониторах он разный, и общего значения не существует.
/// </para>
/// </remarks>
/// <param name="Dpi">Точек на дюйм у того, кто рисует.</param>
internal readonly record struct Metrics(int Dpi)
{
    /// <summary>Масштаб, при котором единица дизайна равна пикселю.</summary>
    private const double BaseDpi = 96d;

    /// <summary>Метрики того контрола, который сейчас рисует.</summary>
    public static Metrics Of(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new Metrics(control.DeviceDpi);
    }

    /// <summary>Переводит единицы дизайна в пиксели.</summary>
    public int Scale(int units) => (int)Math.Round(units * Dpi / BaseDpi, MidpointRounding.AwayFromZero);

    /// <summary>Переводит единицы дизайна в пиксели, сохраняя дробную часть.</summary>
    /// <remarks>Нужно там, где округление копится: доли полосы, толщина линии, радиус дуги.</remarks>
    public float Exact(double units) => (float)(units * Dpi / BaseDpi);

    /// <inheritdoc cref="Scale(int)"/>
    public Size Scale(Size size) => new(Scale(size.Width), Scale(size.Height));

    /// <inheritdoc cref="Scale(int)"/>
    public Padding Scale(Padding padding) => new(
        Scale(padding.Left),
        Scale(padding.Top),
        Scale(padding.Right),
        Scale(padding.Bottom));

    // ---- ритм отступов --------------------------------------------------------------

    /// <summary>Шаг сетки отступов: 4 · 8 · 12 · 16 · 24 · 32.</summary>
    /// <remarks>Промежуточных значений в дизайне нет, и заводить их не надо.</remarks>
    public int Space(int step) => Scale(step switch
    {
        <= 1 => 4,
        2 => 8,
        3 => 12,
        4 => 16,
        5 => 24,
        _ => 32,
    });

    // ---- скругления ------------------------------------------------------------------

    /// <summary>Кнопка, поле, строка списка.</summary>
    public int RadiusControl => Scale(6);

    /// <summary>Карточка.</summary>
    public int RadiusCard => Scale(10);

    /// <summary>
    /// Окно. Дизайн просит 13, но радиус углов окна задаёт Windows, а не приложение:
    /// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> знает только «круглые» и «чуть круглые».
    /// Значение остаётся здесь для внутренних полотен, повторяющих форму окна.
    /// </summary>
    public int RadiusWindow => Scale(13);

    // ---- высоты ----------------------------------------------------------------------

    /// <summary>Полоса заголовка окна.</summary>
    public int CaptionHeight => Scale(38);

    /// <summary>Кнопка в заголовке окна.</summary>
    public Size CaptionButton => new(Scale(44), Scale(38));

    /// <summary>
    /// Строка списка. Ниже не опускается ни на одном размере окна: на 32 мышь начинает
    /// промахиваться, и дизайн держит 38 даже в самом узком варианте.
    /// </summary>
    public int RowHeight => Scale(38);

    /// <summary>Шапка таблицы — ниже строки.</summary>
    public int HeaderHeight => Scale(34);

    /// <summary>Кнопка и поле ввода.</summary>
    public int ControlHeight => Scale(30);

    /// <summary>Полоса дня в широком окне.</summary>
    public int TimelineHeight => Scale(34);

    /// <summary>Полоса дня в узком окне.</summary>
    public int TimelineHeightNarrow => Scale(30);

    // ---- элементы ---------------------------------------------------------------------

    /// <summary>Переключатель целиком.</summary>
    public Size Switch => new(Scale(38), Scale(22));

    /// <summary>Кружок переключателя.</summary>
    public int SwitchKnob => Scale(18);

    /// <summary>Отступ кружка от края переключателя.</summary>
    public int SwitchInset => Scale(2);

    /// <summary>Квадратная кнопка со стрелкой: шаг дня, шаг периода.</summary>
    public int StepperSize => Scale(26);

    /// <summary>Значок внутри квадратной кнопки.</summary>
    public int GlyphSize => Scale(9);

    /// <summary>Значок в кнопке заголовка окна.</summary>
    public int CaptionGlyphSize => Scale(10);

    /// <summary>Квадратик типа в строке списка.</summary>
    public int MarkerSize => Scale(8);

    /// <summary>Квадратик типа в легенде — на единицу крупнее строчного.</summary>
    public int LegendMarkerSize => Scale(9);

    // ---- фокус и рамки -------------------------------------------------------------------

    /// <summary>
    /// Толщина контура фокуса. Мягкого ореола из дизайна GDI+ не рисует, но радиус размытия
    /// там и так нулевой: сплошной контур той же толщины и того же цвета — это он и есть.
    /// </summary>
    public int FocusWidth => Scale(2);

    /// <summary>Насколько контур фокуса отходит от края элемента.</summary>
    public int FocusInset => Scale(2);

    // ---- окно без системной рамки ----------------------------------------------------------

    /// <summary>Полоса растяжения вдоль стороны окна.</summary>
    /// <remarks>Пять, а не один: курсор должен меняться на подходе к границе, а не на ней.</remarks>
    public int ResizeBorder => Scale(5);

    /// <summary>
    /// Сторона квадрата растяжения в углу окна. Проверять его надо раньше сторон, иначе угол
    /// достанется стороне и окно перестанет тянуться по диагонали — про это забывают чаще всего.
    /// </summary>
    public int ResizeCorner => Scale(8);

    /// <summary>Ширина под кнопками заголовка, за которую окно не перетаскивается.</summary>
    public int CaptionButtonsReserve => Scale(132);

    // ---- минимальные размеры окон ------------------------------------------------------------

    /// <summary>Форма дня. Ниже содержимое ломается раньше окна.</summary>
    public Size DayMinimum => new(Scale(700), Scale(520));

    /// <summary>Статистика.</summary>
    public Size StatsMinimum => new(Scale(760), Scale(560));

    /// <summary>Настройки.</summary>
    public Size SettingsMinimum => new(Scale(620), Scale(480));

    // ---- точки перелома раскладки -------------------------------------------------------------

    /// <summary>Ширина, с которой форма дня раскладывается в две колонки.</summary>
    public int WideBreakpoint => Scale(1100);

    /// <summary>Ширина, ниже которой раскладка переходит в сжатый вариант.</summary>
    public int NarrowBreakpoint => Scale(760);

    /// <summary>Ширина, ниже которой якоря настроек сворачиваются в строку вкладок.</summary>
    public int SettingsTabsBreakpoint => Scale(700);
}
