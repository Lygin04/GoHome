using GoHome.Interop;

namespace GoHome.Ui.Design;

/// <summary>
/// Палитра окон — единственный источник цвета для всего, что приложение рисует само.
/// </summary>
/// <remarks>
/// Значения взяты из <c>docs/design/GoHome UI.dc.html</c> и не подбираются на глаз: акцент
/// в светлой теме темнее тёмного (<c>#2563EB</c> против <c>#60A5FA</c>), и «цель взята» тоже
/// (<c>#15803D</c> против <c>#4CC27A</c>) — это контраст на светлом фоне, а не вкус.
/// Поменять их местами нельзя.
/// <para>
/// Тёмная тема спрашивается у самого WinForms, а не у настройки: настройку в режим оформления
/// уже перевёл <see cref="WindowTheme.Apply"/>, и брать её второй раз значит завести второй
/// источник правды — окно окажется светлым, а нарисованное в нём тёмным ровно тогда, когда
/// эти два источника разойдутся.
/// </para>
/// <para>
/// Кольца в трее это не касается вовсе. Кольцо живёт на панели задач и берёт цвета из
/// <see cref="SystemTheme.IsDarkTaskbar"/> — из оформления самой панели. Принудительно тёмное
/// кольцо на светлой панели снова стало бы невидимым.
/// </para>
/// </remarks>
internal sealed class Palette
{
    // ---- поверхности ---------------------------------------------------------------

    /// <summary>Фон окна.</summary>
    public required Color Window { get; init; }

    /// <summary>Фон карточки — на тон отличается от окна, и эта разница заменяет тень.</summary>
    public required Color Card { get; init; }

    /// <summary>Поле ввода и подсветка строки под курсором.</summary>
    public required Color Field { get; init; }

    /// <summary>Полоса заголовка окна.</summary>
    public required Color Caption { get; init; }

    /// <summary>Колонка якорей в настройках.</summary>
    public required Color Sidebar { get; init; }

    /// <summary>Пункт колонки якорей под курсором.</summary>
    public required Color SidebarHover { get; init; }

    /// <summary>Незаполненная часть полосы — дня в форме дня и прогресса в меню трея.</summary>
    public required Color Track { get; init; }

    /// <summary>
    /// Утопленная подложка: жёлоб сегментированных вкладок.
    /// </summary>
    /// <remarks>
    /// Отдельная роль, а не «поле» или «мягкая линия»: «утоплено» в тёмной теме светлее фона,
    /// а в светлой темнее, и вычислить одно из другого нельзя. Контрол, который выбирал бы
    /// между двумя ролями по теме, завёл бы второй источник правды о том, какая тема идёт.
    /// </remarks>
    public required Color Well { get; init; }

    // ---- текст ---------------------------------------------------------------------

    /// <summary>Основной текст.</summary>
    public required Color Ink { get; init; }

    /// <summary>Второстепенный текст: подписи, пояснения, числа в строках.</summary>
    public required Color Muted { get; init; }

    /// <summary>Блёклый текст: метки секций, оси, неактивный заголовок окна.</summary>
    public required Color Faint { get; init; }

    /// <summary>Значок недоступного элемента. Тише, чем недоступный текст.</summary>
    public required Color GlyphDisabled { get; init; }

    // ---- линии ---------------------------------------------------------------------

    /// <summary>Рамка карточки, поля, кнопки.</summary>
    public required Color Line { get; init; }

    /// <summary>Разделитель строк — тише рамки.</summary>
    public required Color LineSoft { get; init; }

    // ---- смысловые -----------------------------------------------------------------

    /// <summary>Акцент, он же цвет работы на полосе и на графике.</summary>
    public required Color Accent { get; init; }

    /// <summary>Норма закрыта.</summary>
    public required Color Goal { get; init; }

    /// <summary>Не идёт в рабочее время.</summary>
    public required Color Unpaid { get; init; }

    /// <summary>Необратимое действие и недопустимое значение.</summary>
    public required Color Danger { get; init; }

    // ---- выделение -----------------------------------------------------------------

    /// <summary>Фон выбранной строки.</summary>
    public required Color Selection { get; init; }

    /// <summary>Текст выбранной строки.</summary>
    public required Color SelectionInk { get; init; }

    /// <summary>Рамка бейджа внутри выбранной строки — на её фоне обычная теряется.</summary>
    public required Color SelectionLine { get; init; }

    // ---- составные ------------------------------------------------------------------

    /// <summary>Обычная кнопка: между состояниями меняется рамка.</summary>
    public required ButtonStyle NeutralButton { get; init; }

    /// <summary>Основная кнопка: между состояниями меняется заливка целиком.</summary>
    public required ButtonStyle PrimaryButton { get; init; }

    /// <summary>Опасная кнопка: в покое ни подложки, ни рамки.</summary>
    public required ButtonStyle DangerButton { get; init; }

    /// <summary>
    /// Залитая красным кнопка подтверждения.
    /// </summary>
    /// <remarks>
    /// Красный один на обе темы — тот же, которым Windows подсвечивает закрытие окна.
    /// Подбирать под тему оттенок необратимого действия незачем: он должен читаться
    /// одинаково тревожно и там и там.
    /// </remarks>
    public required ButtonStyle DangerFilledButton { get; init; }

    /// <summary>Поле ввода.</summary>
    public required FieldStyle TextField { get; init; }

    /// <summary>Бейдж «правлено вручную».</summary>
    public required Face ManualBadge { get; init; }

    // ---- заголовок окна -------------------------------------------------------------

    /// <summary>Кнопка заголовка под курсором.</summary>
    public required Color CaptionHover { get; init; }

    /// <summary>Кнопка заголовка нажата.</summary>
    public required Color CaptionPressed { get; init; }

    /// <summary>Закрытие под курсором. В обеих темах одно и то же — как в самой Windows.</summary>
    public required Color CaptionClose { get; init; }

    /// <summary>Значок закрытия на красном.</summary>
    public required Color CaptionCloseInk { get; init; }

    // ---- всплывающее ----------------------------------------------------------------

    /// <summary>Фон подсказки над столбцом графика.</summary>
    public required Color TooltipBack { get; init; }

    /// <summary>Рамка подсказки — заметнее обычной: подсказка лежит поверх карточки.</summary>
    public required Color TooltipLine { get; init; }

    /// <summary>Фон меню трея.</summary>
    public required Color MenuBack { get; init; }

    /// <summary>Рамка меню трея.</summary>
    public required Color MenuLine { get; init; }

    /// <summary>Пункт меню под курсором.</summary>
    public required Color MenuHover { get; init; }

    /// <summary>Пункт меню, выходящий из приложения.</summary>
    public required Color MenuDangerInk { get; init; }

    // ---- переключатель ---------------------------------------------------------------

    /// <summary>Кружок переключателя.</summary>
    public required Color SwitchKnob { get; init; }

    /// <summary>Кружок недоступного переключателя.</summary>
    public required Color SwitchKnobDisabled { get; init; }

    // ---- тепловая карта ---------------------------------------------------------------

    /// <summary>
    /// Ступени заполнения от меньшего к большему. Норму закрывает не последняя ступень,
    /// а <see cref="Goal"/>: «много» и «хватило» — разные вещи, и цветом они разные.
    /// </summary>
    public required IReadOnlyList<Color> Heat { get; init; }

    /// <summary>Клетка дня, в который не работали.</summary>
    public required Color HeatEmpty { get; init; }

    // ---- готовые палитры ---------------------------------------------------------------

    /// <summary>
    /// Тёмная тема — та, под которую дизайн рисовался.
    /// </summary>
    /// <remarks>
    /// Выбирать палитру напрямую вместо <see cref="Current"/> нельзя нигде, кроме показа
    /// обеих тем рядом: иначе нарисованное разойдётся с самим окном.
    /// </remarks>
    public static readonly Palette Dark = new()
    {
        Window = Rgb(0x14171C),
        Card = Rgb(0x1B1F26),
        Field = Rgb(0x232830),
        Caption = Rgb(0x1B1F26),
        Sidebar = Rgb(0x171B21),
        SidebarHover = Rgb(0x1F242B),
        Track = Rgb(0x272C34),
        Well = Rgb(0x232830),

        Ink = Rgb(0xE6EAF0),
        Muted = Rgb(0x8B96A5),
        Faint = Rgb(0x5D6673),
        GlyphDisabled = Rgb(0x3E4551),

        Line = Rgb(0x2C323C),
        LineSoft = Rgb(0x22262E),

        Accent = Rgb(0x60A5FA),
        Goal = Rgb(0x4CC27A),
        Unpaid = Rgb(0x68717F),
        Danger = Rgb(0xE06C60),

        Selection = Rgb(0x22344E),
        SelectionInk = Rgb(0xC7CEDA),
        SelectionLine = Rgb(0x3C4759),

        NeutralButton = new ButtonStyle(
            Rest: new Face(Rgb(0x232830), Rgb(0x2C323C), Rgb(0xE6EAF0)),
            Hover: new Face(Rgb(0x232830), Rgb(0x5D6673), Rgb(0xE6EAF0)),
            Pressed: new Face(Rgb(0x1B1F26), Rgb(0x5D6673), Rgb(0xE6EAF0)),
            Disabled: new Face(Rgb(0x191D23), Rgb(0x22262E), Rgb(0x5D6673))),

        PrimaryButton = new ButtonStyle(
            Rest: new Face(Rgb(0x60A5FA), Rgb(0x60A5FA), Rgb(0x0B0D11)),
            Hover: new Face(Rgb(0x7DB6FB), Rgb(0x7DB6FB), Rgb(0x0B0D11)),
            Pressed: new Face(Rgb(0x4A8CD8), Rgb(0x4A8CD8), Rgb(0x0B0D11)),
            Disabled: new Face(Rgb(0x253141), Rgb(0x253141), Rgb(0x5D6673))),

        DangerButton = new ButtonStyle(
            Rest: new Face(Color.Transparent, Color.Transparent, Rgb(0xE06C60)),
            Hover: new Face(Rgb(0x272C34), Color.Transparent, Rgb(0xE06C60)),
            Pressed: new Face(Rgb(0x2A1E1E), Rgb(0xE06C60), Rgb(0xE9857A)),
            Disabled: new Face(Color.Transparent, Color.Transparent, Rgb(0x4A3330))),

        DangerFilledButton = new ButtonStyle(
            Rest: new Face(Rgb(0xC4362C), Rgb(0xC4362C), Rgb(0xFFFFFF)),
            Hover: new Face(Rgb(0xD44A3F), Rgb(0xD44A3F), Rgb(0xFFFFFF)),
            Pressed: new Face(Rgb(0xA82C24), Rgb(0xA82C24), Rgb(0xFFFFFF)),
            Disabled: new Face(Rgb(0x3A2A2A), Rgb(0x3A2A2A), Rgb(0x5D6673))),

        TextField = new FieldStyle(
            Rest: new Face(Rgb(0x232830), Rgb(0x2C323C), Rgb(0xE6EAF0)),
            Focus: new Face(Rgb(0x232830), Rgb(0x60A5FA), Rgb(0xE6EAF0)),
            Error: new Face(Rgb(0x232830), Rgb(0xE06C60), Rgb(0xE9857A)),
            Disabled: new Face(Rgb(0x191D23), Rgb(0x22262E), Rgb(0x5D6673))),

        ManualBadge = new Face(Rgb(0x1B2635), Rgb(0x2E4763), Rgb(0x60A5FA)),

        CaptionHover = Rgb(0x232830),
        CaptionPressed = Rgb(0x2C323C),
        CaptionClose = Rgb(0xC4362C),
        CaptionCloseInk = Rgb(0xFFFFFF),

        TooltipBack = Rgb(0x232830),
        TooltipLine = Rgb(0x3C4759),

        MenuBack = Rgb(0x1B1F26),
        MenuLine = Rgb(0x3C4759),
        MenuHover = Rgb(0x272C34),
        MenuDangerInk = Rgb(0xE9857A),

        SwitchKnob = Rgb(0xFFFFFF),
        SwitchKnobDisabled = Rgb(0x5D6673),

        // Ступени — акцент, подмешанный к карточке на треть и на две трети. Дизайн даёт
        // эту шкалу только в светлой теме; здесь она построена тем же приёмом от тех же
        // ролей, поэтому клетки читаются одинаково в обеих.
        Heat = [Rgb(0x22262E), Rgb(0x2E4563), Rgb(0x426BA0), Rgb(0x60A5FA)],
        HeatEmpty = Rgb(0x14171C),
    };

    /// <summary>Светлая тема. Акцент и «цель взята» в ней темнее — это контраст, а не вкус.</summary>
    /// <inheritdoc cref="Dark" path="/remarks"/>
    public static readonly Palette Light = new()
    {
        Window = Rgb(0xF1F3F6),
        Card = Rgb(0xFFFFFF),
        Field = Rgb(0xF5F7F9),
        Caption = Rgb(0xFFFFFF),
        Sidebar = Rgb(0xEAEDF1),
        SidebarHover = Rgb(0xE3E7EC),
        Track = Rgb(0xE8EBEF),
        Well = Rgb(0xE9ECF0),

        Ink = Rgb(0x171A1F),
        Muted = Rgb(0x606A78),
        Faint = Rgb(0x98A1AE),
        GlyphDisabled = Rgb(0xC3C9D2),

        Line = Rgb(0xDDE1E7),
        LineSoft = Rgb(0xE9ECF0),

        Accent = Rgb(0x2563EB),
        Goal = Rgb(0x15803D),
        Unpaid = Rgb(0x98A1AE),
        Danger = Rgb(0xC0392F),

        Selection = Rgb(0xDCE8FD),
        SelectionInk = Rgb(0x1B2B4B),
        SelectionLine = Rgb(0xB9CCF3),

        NeutralButton = new ButtonStyle(
            Rest: new Face(Rgb(0xF5F7F9), Rgb(0xDDE1E7), Rgb(0x171A1F)),
            Hover: new Face(Rgb(0xF5F7F9), Rgb(0x98A1AE), Rgb(0x171A1F)),
            Pressed: new Face(Rgb(0xE9ECF0), Rgb(0x98A1AE), Rgb(0x171A1F)),
            Disabled: new Face(Rgb(0xF7F8FA), Rgb(0xE9ECF0), Rgb(0x98A1AE))),

        PrimaryButton = new ButtonStyle(
            Rest: new Face(Rgb(0x2563EB), Rgb(0x2563EB), Rgb(0xFFFFFF)),
            Hover: new Face(Rgb(0x1D4FD7), Rgb(0x1D4FD7), Rgb(0xFFFFFF)),
            Pressed: new Face(Rgb(0x1A45BC), Rgb(0x1A45BC), Rgb(0xFFFFFF)),
            Disabled: new Face(Rgb(0xC7D3EE), Rgb(0xC7D3EE), Rgb(0xFFFFFF))),

        DangerButton = new ButtonStyle(
            Rest: new Face(Color.Transparent, Color.Transparent, Rgb(0xC0392F)),
            Hover: new Face(Rgb(0xE8EBEF), Color.Transparent, Rgb(0xC0392F)),
            Pressed: new Face(Rgb(0xF7E3E1), Rgb(0xC0392F), Rgb(0xA62F26)),
            Disabled: new Face(Color.Transparent, Color.Transparent, Rgb(0xE3BFBB))),

        DangerFilledButton = new ButtonStyle(
            Rest: new Face(Rgb(0xC0392F), Rgb(0xC0392F), Rgb(0xFFFFFF)),
            Hover: new Face(Rgb(0xA82C24), Rgb(0xA82C24), Rgb(0xFFFFFF)),
            Pressed: new Face(Rgb(0x8F231C), Rgb(0x8F231C), Rgb(0xFFFFFF)),
            Disabled: new Face(Rgb(0xE3BFBB), Rgb(0xE3BFBB), Rgb(0xFFFFFF))),

        TextField = new FieldStyle(
            Rest: new Face(Rgb(0xF5F7F9), Rgb(0xDDE1E7), Rgb(0x171A1F)),
            Focus: new Face(Rgb(0xFFFFFF), Rgb(0x2563EB), Rgb(0x171A1F)),
            Error: new Face(Rgb(0xFFFFFF), Rgb(0xC0392F), Rgb(0xC0392F)),
            Disabled: new Face(Rgb(0xF7F8FA), Rgb(0xE9ECF0), Rgb(0x98A1AE))),

        ManualBadge = new Face(Rgb(0xEEF4FE), Rgb(0xB9CCF3), Rgb(0x2563EB)),

        CaptionHover = Rgb(0xF5F7F9),
        CaptionPressed = Rgb(0xE9ECF0),
        CaptionClose = Rgb(0xC4362C),
        CaptionCloseInk = Rgb(0xFFFFFF),

        TooltipBack = Rgb(0xFFFFFF),
        TooltipLine = Rgb(0xC3C9D2),

        MenuBack = Rgb(0xFFFFFF),
        MenuLine = Rgb(0xDDE1E7),
        MenuHover = Rgb(0xF5F7F9),
        MenuDangerInk = Rgb(0xC0392F),

        SwitchKnob = Rgb(0xFFFFFF),
        SwitchKnobDisabled = Rgb(0xFFFFFF),

        Heat = [Rgb(0xE9ECF0), Rgb(0xBFD4FA), Rgb(0x7FA8F2), Rgb(0x2563EB)],
        HeatEmpty = Rgb(0xF5F7F9),
    };

    /// <summary>Палитра под то оформление окон, которое действует прямо сейчас.</summary>
    public static Palette Current() =>
        SystemTheme.IsHighContrast() ? HighContrast() : Application.IsDarkModeEnabled ? Dark : Light;

    /// <summary>Цвет отрезка на полосе дня и столбца на графике.</summary>
    public Color WorkColor(bool unpaid, bool goalReached) =>
        unpaid ? Unpaid : goalReached ? Goal : Accent;

    /// <summary>
    /// Ступень тепловой карты по доле от нормы. Закрытая норма — не последняя ступень,
    /// а отдельный цвет: «много» и «хватило» человек читает по-разному.
    /// </summary>
    public Color HeatColor(double share, bool goalReached)
    {
        if (goalReached)
        {
            return Goal;
        }

        if (share <= 0d)
        {
            return HeatEmpty;
        }

        var step = (int)(Math.Clamp(share, 0d, 1d) * Heat.Count);
        return Heat[Math.Min(step, Heat.Count - 1)];
    }

    /// <summary>
    /// Смешивает цвет с фоном заранее. Настоящей полупрозрачности в слое нет, но фон под
    /// засчитанной отлучкой известен всегда, поэтому смешать можно один раз при отрисовке.
    /// </summary>
    /// <remarks>
    /// Результат обязательно проверять в обеих темах: на светлом фоне акцент при 0.42 уходит
    /// в блёкло-голубой, и от <see cref="Unpaid"/> его надо отличать на глаз.
    /// </remarks>
    public static Color Blend(Color over, Color under, double alpha)
    {
        var share = Math.Clamp(alpha, 0d, 1d);
        return Color.FromArgb(
            Mix(over.R, under.R, share),
            Mix(over.G, under.G, share),
            Mix(over.B, under.B, share));

        static int Mix(byte top, byte bottom, double share) =>
            (int)Math.Round(top * share + bottom * (1 - share));
    }

    /// <summary>
    /// Высокая контрастность: своих цветов здесь быть не может — тему выбрал человек, и
    /// подбирать под неё оттенки бессмысленно. Это третья палитра, а не испорченная одна
    /// из двух: системные цвета между собой уже согласованы.
    /// </summary>
    /// <remarks>
    /// Строится каждый раз заново: режим переключается горячими клавишами в любой момент,
    /// и системные цвета вместе с ним.
    /// </remarks>
    public static Palette HighContrast()
    {
        var window = SystemColors.Window;
        var text = SystemColors.WindowText;
        var control = SystemColors.Control;
        var controlText = SystemColors.ControlText;
        var gray = SystemColors.GrayText;
        var highlight = SystemColors.Highlight;
        var highlightText = SystemColors.HighlightText;

        var normal = new Face(control, controlText, controlText);
        var active = new Face(highlight, highlight, highlightText);
        var off = new Face(control, gray, gray);

        return new Palette
        {
            Window = window,
            Card = window,
            Field = window,
            Caption = control,
            Sidebar = control,
            SidebarHover = highlight,
            Track = control,
            Well = control,

            Ink = text,
            Muted = text,
            Faint = gray,
            GlyphDisabled = gray,

            Line = controlText,
            LineSoft = gray,

            Accent = highlight,
            Goal = SystemColors.HotTrack,
            Unpaid = gray,
            Danger = controlText,

            Selection = highlight,
            SelectionInk = highlightText,
            SelectionLine = highlightText,

            NeutralButton = new ButtonStyle(normal, active, active, off),
            PrimaryButton = new ButtonStyle(active, normal, normal, off),
            DangerButton = new ButtonStyle(normal, active, active, off),
            DangerFilledButton = new ButtonStyle(active, normal, normal, off),

            TextField = new FieldStyle(
                Rest: new Face(window, controlText, text),
                Focus: new Face(window, highlight, text),
                Error: new Face(window, highlight, highlightText),
                Disabled: off),

            ManualBadge = new Face(window, gray, text),

            CaptionHover = highlight,
            CaptionPressed = highlight,
            CaptionClose = highlight,
            CaptionCloseInk = highlightText,

            TooltipBack = SystemColors.Info,
            TooltipLine = controlText,

            MenuBack = SystemColors.Menu,
            MenuLine = controlText,
            MenuHover = highlight,
            MenuDangerInk = SystemColors.MenuText,

            SwitchKnob = highlightText,
            SwitchKnobDisabled = gray,

            Heat = [control, gray, highlight, highlight],
            HeatEmpty = window,
        };
    }

    private static Color Rgb(int value) => Color.FromArgb(unchecked((int)0xFF000000) | value);
}
