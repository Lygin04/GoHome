using System.Diagnostics;
using System.Globalization;
using GoHome.App;
using GoHome.Core;
using GoHome.Ui.Design;

namespace GoHome.Ui;

/// <summary>
/// Настройки: рабочий день, отлучки, уведомления, внешний вид, данные.
/// </summary>
/// <remarks>
/// Правила расчёта помечены прямо в форме: они действуют со следующего дня, потому что
/// иначе день окажется посчитан двумя способами. Продолжительность дня — не правило,
/// она подхватывается сразу, как и тема окон.
/// <para>
/// Разделы показываются по одному. Дизайн (1g) держит их одним полотном с якорями слева,
/// но полотно с прокруткой — это ещё один способ раскладывать содержимое, а по одному
/// разделу за раз то же самое и без него. В узком окне якоря становятся строкой вкладок,
/// как в дизайне (1h).
/// </para>
/// </remarks>
internal sealed class SettingsForm : DesignForm
{
    /// <summary>Названия разделов для колонки якорей.</summary>
    private static readonly string[] Names =
        ["Рабочая неделя", "Исключения", "Отлучки и обед", "Уведомления", "Внешний вид", "Данные"];

    /// <summary>
    /// Те же разделы для строки вкладок в узком окне.
    /// </summary>
    /// <remarks>
    /// Короче: шесть полных названий в шестьсот двадцать точек не помещаются, а дизайн
    /// в узком окне и так их сокращает.
    /// </remarks>
    private static readonly string[] Short = ["Неделя", "Даты", "Отлучки", "Уведом.", "Вид", "Данные"];

    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;

    private readonly Nav _nav;
    private readonly SegmentedTabs _tabs;
    private readonly SettingsSection[] _sections;

    private readonly DesignField[] _dayHours = new DesignField[7];
    private readonly DesignSwitch[] _dayOff = new DesignSwitch[7];

    private readonly ExceptionList _exceptions;
    private readonly DesignButton _addException;
    private readonly DesignButton _editException;
    private readonly DesignButton _removeException;

    private readonly DesignSwitch _countShortBreaks;
    private readonly DesignField _windowStart;
    private readonly DesignField _windowEnd;
    private readonly DesignField _breakMinimum;
    private readonly DesignField _lunchMinimum;

    private readonly DesignSwitch _warnEnabled;
    private readonly DesignField _warnBefore;

    private readonly SegmentedTabs _theme;

    private readonly Problems _problems;
    private readonly DesignButton _save;

    private List<DateException> _draftExceptions = [];
    private int _section;
    private bool _built;

    /// <summary>Поля заполняются кодом, и события при этом сыплются — это не правка человека.</summary>
    private bool _filling;

    public SettingsForm(GoHomeService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;

        SetTitle("GoHome — настройки", "Настройки");
        MaximizeBox = false;
        SetMinimum(new Size(620, 520));
        SetInitialSize(new Size(880, 640));

        _nav = new Nav(Names);
        _nav.SelectionChanged += (_, _) => Choose(_nav.SelectedIndex);

        _tabs = new SegmentedTabs { Items = Short, Visible = false };
        _tabs.SelectedChanged += (_, _) => Choose(_tabs.Selected);

        // ---- рабочий день ------------------------------------------------------------
        var day = new SettingsSection
        {
            Label = "Рабочая неделя",
            Note = "Продолжительность дня и переключатель «нерабочий» справа. "
                + "Отсюда считаются цель, прогноз ухода и знак «к цели» в статистике.",
        };

        for (var index = 0; index < WeekSchedule.Days.Count; index++)
        {
            var weekday = WeekSchedule.Days[index];
            var hours = new DesignField { Kind = FieldKind.Duration };
            var off = new DesignSwitch();
            var slot = index;

            off.CheckedChanged += (_, _) =>
            {
                _dayHours[slot].Enabled = !off.Checked;
                Revalidate();
            };

            hours.ValueChanged += (_, _) => Revalidate();

            _dayHours[index] = hours;
            _dayOff[index] = off;

            day.Add(Capitalize(SettingsCheck.DayName(weekday)), string.Empty, hours, off);
        }

        _exceptions = new ExceptionList();
        _exceptions.SelectionChanged += (_, _) => UpdateExceptionButtons();
        _exceptions.RowActivated += (_, _) => EditException();

        _addException = new DesignButton { Text = "Добавить…" };
        _addException.Click += (_, _) => AddException();

        _editException = new DesignButton { Text = "Изменить…", Enabled = false };
        _editException.Click += (_, _) => EditException();

        _removeException = new DesignButton { Kind = ButtonKind.Danger, Text = "Удалить", Enabled = false };
        _removeException.Click += (_, _) => RemoveException();

        // ---- отлучки и обед -----------------------------------------------------------
        _countShortBreaks = new DesignSwitch();
        _countShortBreaks.CheckedChanged += (_, _) =>
        {
            UpdateLunchEnabled();
            Revalidate();
        };

        _windowStart = new DesignField { Kind = FieldKind.Time };
        _windowEnd = new DesignField { Kind = FieldKind.Time };
        _breakMinimum = new DesignField { Kind = FieldKind.Duration };
        _lunchMinimum = new DesignField { Kind = FieldKind.Duration };

        foreach (var field in new[] { _windowStart, _windowEnd, _breakMinimum, _lunchMinimum })
        {
            field.ValueChanged += (_, _) => Revalidate();
        }

        var breaks = new SettingsSection
        {
            Label = "Отлучки и обед",
            Note = "Как автоматика решает, что засчитать. Ручная правка дня всегда сильнее этих правил.",
        };

        breaks.Add(
            "Засчитывать короткие отлучки",
            "Выключено — счёт останавливает любая блокировка экрана",
            _countShortBreaks);

        breaks.Add("Обеденное окно, с", "Раньше этого отлучка обедом не считается", _windowStart);
        breaks.Add("Обеденное окно, по", "Позже этого — тоже", _windowEnd);
        breaks.Add("Короче этого — не отлучка", "Совсем короткие блокировки в список не попадают", _breakMinimum);
        breaks.Add("Короче этого — не обед", "Короткая отлучка в окне обедом не станет", _lunchMinimum);

        // ---- уведомления ---------------------------------------------------------------
        _warnBefore = new DesignField { Kind = FieldKind.Duration };
        _warnBefore.ValueChanged += (_, _) => Revalidate();

        // Создаётся после поля: выключатель им распоряжается, и поле должно уже быть.
        _warnEnabled = new DesignSwitch();
        _warnEnabled.CheckedChanged += (_, _) =>
        {
            _warnBefore.Enabled = _warnEnabled.Checked;
            Revalidate();
        };

        var notifications = new SettingsSection
        {
            Label = "Уведомления",
            Note = "Единственное, что GoHome показывает поверх работы.",
        };

        notifications.Add("Предупреждать, что норма скоро", "Приходит один раз за день", _warnEnabled);
        notifications.Add("За сколько до нормы", "Столько, чтобы успеть свернуть дела", _warnBefore);

        // ---- внешний вид ------------------------------------------------------------------
        _theme = new SegmentedTabs { Items = ["Как в системе", "Светлая", "Тёмная"] };
        _theme.SelectedChanged += (_, _) =>
        {
            // Тема — предпочтение, а не правило: применяется сразу, не дожидаясь сохранения.
            WindowTheme.Apply((AppTheme)_theme.Selected);
            Revalidate();
        };

        var appearance = new SettingsSection
        {
            Label = "Внешний вид",
            Note = "Применяется сразу ко всем открытым окнам.",
        };

        appearance.Add(
            "Тема окон",
            "На кольцо в трее не влияет: оно следует оформлению панели задач",
            _theme);

        // ---- данные --------------------------------------------------------------------------
        var settingsFile = new DesignButton { Text = "Показать в папке" };
        settingsFile.Click += (_, _) => Reveal(_service.SettingsPath);

        var dataFolder = new DesignButton { Text = "Показать в папке" };
        dataFolder.Click += (_, _) => Reveal(_service.DataRoot);

        var reset = new DesignButton { Kind = ButtonKind.Danger, Text = "Сбросить всё" };
        reset.Click += (_, _) => Fill(AppSettings.Default);

        var data = new SettingsSection
        {
            Label = "Данные",
            Note = "Журнал — обычные файлы. Их можно открыть, скопировать и править руками.",
        };

        data.Add("Файл настроек", _service.SettingsPath ?? string.Empty, settingsFile);
        data.Add("Каталог данных", _service.DataRoot ?? string.Empty, dataFolder);
        data.Add("Сбросить настройки", "Файлы дней это не затронет", reset);

        var dates = new SettingsSection
        {
            Label = "Исключения по датам",
            Note = "Отпуск, праздники, сокращённые дни. Исключение сильнее недельного графика. "
                + "Производственный календарь не загружается — сеть в приложении не используется.",
        };

        _sections = [day, dates, breaks, notifications, appearance, data];

        _problems = new Problems();
        _save = new DesignButton { Kind = ButtonKind.Primary, Text = "Сохранить" };
        _save.Click += (_, _) => Save();

        var close = new DesignButton { Text = "Закрыть" };
        close.Click += (_, _) => Close();
        CloseButton = close;

        Content.Controls.AddRange([_nav, _tabs, _problems, _save, close]);
        Content.Controls.AddRange(_sections);
        Content.Controls.AddRange([_exceptions, _addException, _editException, _removeException]);

        _built = true;
        Fill(_service.Settings);
        Choose(0);
    }

    /// <summary>Настройки сохранены — трею пора перерисоваться.</summary>
    public event EventHandler? Saved;

    private DesignButton? CloseButton { get; init; }

    /// <inheritdoc/>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    /// <inheritdoc/>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Узкое окно: якоря слева сворачиваются в строку вкладок сверху.</summary>
    private bool Narrow => Width < Sizes.SettingsTabsBreakpoint;

    private void Choose(int index)
    {
        _section = Math.Clamp(index, 0, _sections.Length - 1);
        _nav.SelectedIndex = _section;
        _tabs.Selected = _section;

        for (var slot = 0; slot < _sections.Length; slot++)
        {
            _sections[slot].Visible = slot == _section;
        }

        var exceptions = _section == 1;
        _exceptions.Visible = exceptions;
        _addException.Visible = exceptions;
        _editException.Visible = exceptions;
        _removeException.Visible = exceptions;

        Relayout();
    }

    private void Relayout()
    {
        if (!_built)
        {
            return;
        }

        var metrics = Sizes;
        var pad = metrics.Space(4);
        var gap = metrics.Space(2);
        var narrow = Narrow;

        _nav.Visible = !narrow;
        _tabs.Visible = narrow;

        var top = pad;
        var left = pad;
        var right = Content.ClientSize.Width - pad;

        if (narrow)
        {
            _tabs.FitToItems();
            _tabs.Location = new Point(left, top);
            top += _tabs.Height + metrics.Space(4);
        }
        else
        {
            _nav.SetBounds(0, 0, metrics.Scale(196), Content.ClientSize.Height);
            left = _nav.Right + metrics.Space(5);
        }

        // ---- подвал: проверка и кнопки ------------------------------------------------
        var bottom = Content.ClientSize.Height - pad;
        _save.FitToText();
        CloseButton!.FitToText();

        CloseButton.Location = new Point(right - CloseButton.Width, bottom - CloseButton.Height);
        _save.Location = new Point(CloseButton.Left - _save.Width - gap, bottom - _save.Height);

        var footer = bottom - _save.Height - metrics.Space(3);
        _problems.SetBounds(left, footer - metrics.Scale(40), Math.Max(0, right - left), metrics.Scale(40));

        // ---- раздел ----------------------------------------------------------------------
        var width = Math.Max(metrics.Scale(200), right - left);
        var section = _sections[_section];

        // Ширина ставится раньше высоты: пояснение раздела переносится по словам,
        // и сколько оно займёт строк, известно только после того, как ширина задана.
        section.Width = width;
        section.SetBounds(left, top, width, section.PreferredHeight);

        if (_exceptions.Visible)
        {
            foreach (var button in new[] { _addException, _editException, _removeException })
            {
                button.FitToText();
            }

            var listTop = section.Bottom + metrics.Space(4);
            var buttons = _problems.Top - metrics.Space(3) - _addException.Height;

            var x = left;
            foreach (var button in new[] { _addException, _editException, _removeException })
            {
                button.Location = new Point(x, buttons);
                x += button.Width + gap;
            }

            _exceptions.SetBounds(left, listTop, width, Math.Max(0, buttons - listTop - gap));
        }
    }

    // ---- чтение и запись -------------------------------------------------------------------

    private void Fill(AppSettings settings)
    {
        _filling = true;

        _countShortBreaks.Checked = settings.CountShortBreaks;
        _windowStart.Time = settings.Lunch.WindowStart;
        _windowEnd.Time = settings.Lunch.WindowEnd;
        _breakMinimum.Duration = settings.Lunch.Minimum;
        _lunchMinimum.Duration = settings.Lunch.GuessMinimum;

        for (var index = 0; index < WeekSchedule.Days.Count; index++)
        {
            var hours = settings.Schedule[WeekSchedule.Days[index]];
            _dayOff[index].Checked = hours is null;
            _dayHours[index].Duration = hours ?? WorkTimeCalculator.DefaultGoal;
            _dayHours[index].Enabled = hours is not null;
        }

        _draftExceptions = [.. settings.Exceptions];

        _warnEnabled.Checked = settings.WarnsBeforeGoal;
        _warnBefore.Duration = settings.WarnsBeforeGoal ? settings.WarnBefore : AppSettings.DefaultWarnBefore;
        _warnBefore.Enabled = settings.WarnsBeforeGoal;

        _theme.Selected = (int)settings.Theme;

        _filling = false;

        ShowExceptions();
        UpdateLunchEnabled();
        Revalidate();
    }

    /// <summary>Что сейчас набрано в форме.</summary>
    private AppSettings Collect()
    {
        var schedule = WeekSchedule.Default;
        for (var index = 0; index < WeekSchedule.Days.Count; index++)
        {
            schedule = schedule.With(
                WeekSchedule.Days[index],
                _dayOff[index].Checked ? null : _dayHours[index].Duration ?? TimeSpan.Zero);
        }

        return _service.Settings with
        {
            CountShortBreaks = _countShortBreaks.Checked,
            Lunch = new LunchRules(
                _windowStart.Time ?? LunchRules.Default.WindowStart,
                _windowEnd.Time ?? LunchRules.Default.WindowEnd,
                _breakMinimum.Duration ?? TimeSpan.Zero,
                _lunchMinimum.Duration ?? TimeSpan.Zero),
            Schedule = schedule,
            Exceptions = _draftExceptions,

            // Снятая галочка — это ноль, а не спрятанное значение: в файле настроек
            // выключенное предупреждение должно выглядеть выключенным.
            WarnBefore = _warnEnabled.Checked ? _warnBefore.Duration ?? TimeSpan.Zero : TimeSpan.Zero,
            Theme = (AppTheme)Math.Max(_theme.Selected, 0),
        };
    }

    private void Save()
    {
        var settings = Collect();
        if (SettingsCheck.Validate(settings).Count > 0)
        {
            Revalidate();
            return;
        }

        _service.SaveSettings(settings, _clock());
        WindowTheme.Apply(settings.Theme);

        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>Недопустимое значение не даёт сохранить и объясняет причину.</summary>
    private void Revalidate()
    {
        if (_filling || !_built)
        {
            return;
        }

        // Неразобранное поле — тоже недопустимое значение, и краснеет оно само.
        var unparsed = Fields().Any(field => field.Visible && field.Enabled && !field.IsValid);
        var problems = SettingsCheck.Validate(Collect());

        _problems.Show(unparsed
            ? ["Время набрано не полностью."]
            : [.. problems.Select(problem => problem.Message)]);

        _save.Enabled = !unparsed && problems.Count == 0;
        Relayout();
    }

    private IEnumerable<DesignField> Fields()
    {
        foreach (var field in _dayHours)
        {
            yield return field;
        }

        yield return _windowStart;
        yield return _windowEnd;
        yield return _breakMinimum;
        yield return _lunchMinimum;
        yield return _warnBefore;
    }

    /// <summary>
    /// При выключенном зачёте настройки обеда недоступны: они не влияют ни на что,
    /// а активные поля, ничего не делающие, — это обещание, которого форма не держит.
    /// </summary>
    private void UpdateLunchEnabled()
    {
        var enabled = _countShortBreaks.Checked;

        foreach (var field in new[] { _windowStart, _windowEnd, _breakMinimum, _lunchMinimum })
        {
            field.Enabled = enabled;
        }
    }

    // ---- исключения ------------------------------------------------------------------------

    private void ShowExceptions()
    {
        _draftExceptions.Sort((left, right) => left.Date.CompareTo(right.Date));
        _exceptions.Show(_draftExceptions);
        UpdateExceptionButtons();
    }

    private void UpdateExceptionButtons()
    {
        var selected = Selected() is not null;
        _editException.Enabled = selected;
        _removeException.Enabled = selected;
    }

    private DateException? Selected() =>
        _exceptions.SelectedIndex >= 0 && _exceptions.SelectedIndex < _draftExceptions.Count
            ? _draftExceptions[_exceptions.SelectedIndex]
            : null;

    private void AddException()
    {
        var date = WorkDay.DateOf(_clock());
        using var dialog = new DayExceptionDialog(date, null, _service.Settings.Schedule[date.DayOfWeek], lockDate: false);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Put(dialog.Result);
        }
    }

    private void EditException()
    {
        if (Selected() is not { } exception)
        {
            return;
        }

        using var dialog = new DayExceptionDialog(
            exception.Date,
            exception,
            _service.Settings.Schedule[exception.Date.DayOfWeek],
            lockDate: false);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _draftExceptions.RemoveAll(item => item.Date == exception.Date);
            Put(dialog.Result);
        }
    }

    private void RemoveException()
    {
        if (Selected() is { } exception)
        {
            _draftExceptions.RemoveAll(item => item.Date == exception.Date);
            ShowExceptions();
            Revalidate();
        }
    }

    private void Put(DateException exception)
    {
        _draftExceptions.RemoveAll(item => item.Date == exception.Date);
        _draftExceptions.Add(exception);
        ShowExceptions();
        Revalidate();
    }

    // ---- мелочи ---------------------------------------------------------------------------------

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("ru-RU")) + text[1..];

    /// <summary>Открывает файл или каталог в проводнике. Нечем открыть — молча ничего.</summary>
    private static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
                return;
            }

            Process.Start(new ProcessStartInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or ArgumentException)
        {
            // Показывать нечего — молча пропускаем.
        }
    }

    // ---- нарисованные части окна ------------------------------------------------------------------

    /// <summary>Колонка якорей слева.</summary>
    private sealed class Nav : DesignList
    {
        private readonly string[] _items;

        public Nav(string[] items)
        {
            _items = items;
            Separators = false;
            Count = items.Length;
            SelectedIndex = 0;
        }

        protected override void PaintRow(
            Graphics graphics,
            int index,
            Rectangle bounds,
            Palette palette,
            RowState state)
        {
            TextRenderer.DrawText(
                graphics,
                _items[index],
                Fonts.Control,
                bounds,
                state.Selected ? palette.Ink : palette.Muted,
                Middle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            var palette = Colors;
            using (var back = new SolidBrush(palette.Sidebar))
            {
                e.Graphics.FillRectangle(back, ClientRectangle);
            }

            using (var line = new Pen(palette.LineSoft))
            {
                e.Graphics.DrawLine(line, Width - 1, 0, Width - 1, Height);
            }

            base.OnPaint(e);
        }
    }

    /// <summary>Исключения по датам списком.</summary>
    private sealed class ExceptionList : DesignList
    {
        private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        private readonly List<DateException> _items = [];

        public void Show(IReadOnlyList<DateException> exceptions)
        {
            ArgumentNullException.ThrowIfNull(exceptions);

            _items.Clear();
            _items.AddRange(exceptions);
            Count = _items.Count;
        }

        protected override void PaintRow(
            Graphics graphics,
            int index,
            Rectangle bounds,
            Palette palette,
            RowState state)
        {
            var item = _items[index];
            var metrics = Sizes;
            var fonts = Fonts;

            var dateWidth = metrics.Scale(110);
            TextRenderer.DrawText(
                graphics,
                item.Date.ToString("dd.MM.yyyy", Russian),
                fonts.Number,
                new Rectangle(bounds.Left, bounds.Top, dateWidth, bounds.Height),
                RowInk(palette, state),
                Middle);

            var hours = item.Hours is { } value ? WorkTimeFormat.Duration(value) : "нерабочий";
            var hoursWidth = metrics.Scale(90);

            TextRenderer.DrawText(
                graphics,
                hours,
                item.Hours is null ? fonts.Body : fonts.Number,
                new Rectangle(bounds.Left + dateWidth, bounds.Top, hoursWidth, bounds.Height),
                item.Hours is null ? RowMuted(palette, state) : RowInk(palette, state),
                Middle);

            TextRenderer.DrawText(
                graphics,
                item.Note ?? string.Empty,
                fonts.Body,
                Rectangle.FromLTRB(bounds.Left + dateWidth + hoursWidth, bounds.Top, bounds.Right, bounds.Bottom),
                RowMuted(palette, state),
                Middle);
        }
    }

    /// <summary>Список того, что мешает сохранить.</summary>
    private sealed class Problems : DrawnPanel
    {
        private string[] _messages = [];

        public void Show(IReadOnlyList<string> messages)
        {
            _messages = [.. messages];
            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
        {
            var y = 0;

            foreach (var message in _messages)
            {
                TextRenderer.DrawText(
                    graphics,
                    "• " + message,
                    fonts.Caption,
                    new Rectangle(0, y, Width, fonts.Caption.Height),
                    palette.Danger,
                    Flat);

                y += fonts.Caption.Height + metrics.Scale(2);
            }
        }
    }
}
