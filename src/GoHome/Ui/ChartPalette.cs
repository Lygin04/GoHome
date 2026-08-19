using GoHome.Interop;

namespace GoHome.Ui;

/// <summary>
/// Цвета графиков.
/// </summary>
/// <remarks>
/// Своя палитра, а не системные цвета: график рисуется вручную, и подобрать столбцу
/// «ниже нормы» и столбцу «выше нормы» пару, различимую в обеих темах, системными
/// цветами нельзя — их там просто нет. Оттенки те же, что у кольца в трее, чтобы
/// приложение выглядело одним приложением.
/// <para>
/// Тёмная тема спрашивается у самого WinForms, а не у настройки и не у панели задач.
/// Настройку в режим оформления уже перевёл <see cref="WindowTheme.Apply"/>, и брать
/// её второй раз значит завести второй источник правды: график в тёмном окне окажется
/// светлым ровно тогда, когда эти два источника разойдутся. Кольца в трее это не касается —
/// оно живёт на панели задач и следует её оформлению.
/// </para>
/// </remarks>
/// <param name="Surface">Фон полотна.</param>
/// <param name="Ink">Подписи и числа.</param>
/// <param name="Muted">Тихие подписи: даты, оси.</param>
/// <param name="Grid">Линии сетки.</param>
/// <param name="Below">Столбец дня, не дотянувшего до нормы.</param>
/// <param name="Above">Столбец дня, норму закрывшего.</param>
/// <param name="DayOff">Нерабочий день: нормы у него нет, и цвет у него свой.</param>
/// <param name="Empty">День без данных.</param>
/// <param name="Goal">Линия нормы.</param>
internal readonly record struct ChartPalette(
    Color Surface,
    Color Ink,
    Color Muted,
    Color Grid,
    Color Below,
    Color Above,
    Color DayOff,
    Color Empty,
    Color Goal)
{
    private static readonly ChartPalette Light = new(
        Surface: Color.FromArgb(0xFF, 0xFF, 0xFF),
        Ink: Color.FromArgb(0x1B, 0x1B, 0x1B),
        Muted: Color.FromArgb(0x6B, 0x72, 0x80),
        Grid: Color.FromArgb(0xE3, 0xE6, 0xEA),
        Below: Color.FromArgb(0x0B, 0x5C, 0xC8),
        Above: Color.FromArgb(0x0E, 0x7A, 0x3C),
        DayOff: Color.FromArgb(0xC9, 0xCE, 0xD6),
        Empty: Color.FromArgb(0xEF, 0xF1, 0xF4),
        Goal: Color.FromArgb(0x8A, 0x93, 0xA0));

    private static readonly ChartPalette Dark = new(
        Surface: Color.FromArgb(0x20, 0x21, 0x24),
        Ink: Color.FromArgb(0xE8, 0xEA, 0xED),
        Muted: Color.FromArgb(0x9A, 0xA0, 0xA6),
        Grid: Color.FromArgb(0x35, 0x38, 0x3C),
        Below: Color.FromArgb(0x4F, 0xA8, 0xFF),
        Above: Color.FromArgb(0x4A, 0xD6, 0x83),
        DayOff: Color.FromArgb(0x4A, 0x4E, 0x54),
        Empty: Color.FromArgb(0x2A, 0x2D, 0x31),
        Goal: Color.FromArgb(0x7C, 0x84, 0x8E));

    /// <summary>Палитра под то оформление окон, которое действует прямо сейчас.</summary>
    public static ChartPalette Current()
    {
        if (SystemTheme.IsHighContrast())
        {
            return HighContrast();
        }

        return Application.IsDarkModeEnabled ? Dark : Light;
    }

    /// <summary>Цвет столбца: закрытая норма отличается от незакрытой, нерабочий день — от обоих.</summary>
    public Color BarColor(bool dayOff, bool goalReached) =>
        dayOff ? DayOff : goalReached ? Above : Below;

    /// <summary>
    /// Высокая контрастность: своих цветов здесь быть не может — тему выбирает человек,
    /// и подбирать под неё оттенки бессмысленно. Системные цвета с ней согласованы.
    /// </summary>
    private static ChartPalette HighContrast() => new(
        Surface: SystemColors.Window,
        Ink: SystemColors.WindowText,
        Muted: SystemColors.GrayText,
        Grid: SystemColors.GrayText,
        Below: SystemColors.Highlight,
        Above: SystemColors.HotTrack,
        DayOff: SystemColors.GrayText,
        Empty: SystemColors.Control,
        Goal: SystemColors.WindowText);
}
