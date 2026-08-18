using System.Diagnostics;
using System.Globalization;
using GoHome.App;
using GoHome.Core;

namespace GoHome.Ui;

/// <summary>
/// Настройки: учёт времени, график, оформление. Собирается кодом — дизайнер здесь
/// ничего не добавляет, как и в окне истории.
/// </summary>
/// <remarks>
/// Настроек много, поэтому они разложены по вкладкам, а не свалены в одно полотно.
/// Правила расчёта помечены прямо в форме: они действуют со следующего дня, потому что
/// иначе день окажется посчитан двумя способами. Цель — не правило, она подхватывается сразу.
/// </remarks>
public sealed class SettingsForm : Form
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;

    private readonly CheckBox _countShortBreaks;
    private readonly GroupBox _lunchBox;
    private readonly Label _lunchDisabled;
    private readonly DateTimePicker _windowStart;
    private readonly DateTimePicker _windowEnd;
    private readonly DurationBox _breakMinimum;
    private readonly DurationBox _lunchMinimum;

    private readonly Dictionary<DayOfWeek, CheckBox> _dayOff = [];
    private readonly Dictionary<DayOfWeek, DurationBox> _dayHours = [];
    private readonly ListView _exceptions;
    private readonly Button _editException;
    private readonly Button _removeException;

    private readonly ComboBox _theme;
    private readonly Label _problems;
    private readonly Button _save;

    private List<DateException> _draftExceptions = [];

    /// <summary>Поля заполняются кодом, и события при этом сыплются — это не правка человека.</summary>
    private bool _filling;

    public SettingsForm(GoHomeService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;

        Text = "GoHome — настройки";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 560);
        ClientSize = new Size(700, 620);
        Icon = AppIcon.ForWindows;
        ShowIcon = Icon is not null;
        MinimizeBox = false;

        _countShortBreaks = new CheckBox
        {
            Text = "Засчитывать короткие отлучки",
            AutoSize = true,
        };

        _windowStart = TimePicker();
        _windowEnd = TimePicker();
        _breakMinimum = new DurationBox();
        _lunchMinimum = new DurationBox();

        _lunchDisabled = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = SystemColors.GrayText,
            Text = "Короткие отлучки не засчитываются, поэтому обед не определяется: "
                + "из рабочего времени вычитается любая блокировка экрана.",
        };

        _lunchBox = new GroupBox { Text = "Обед", Dock = DockStyle.Top, Height = 180 };

        _exceptions = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            HideSelection = false,
            UseCompatibleStateImageBehavior = false,
        };

        _exceptions.Columns.Add("Дата", 150);
        _exceptions.Columns.Add("День", 110);
        _exceptions.Columns.Add("Продолжительность", 140, HorizontalAlignment.Center);
        _exceptions.Columns.Add("Пометка", 220);
        _exceptions.DoubleClick += (_, _) => EditException();

        _editException = new Button { Text = "Изменить…", Width = 110, Enabled = false };
        _removeException = new Button { Text = "Удалить", Width = 110, Enabled = false };
        _exceptions.SelectedIndexChanged += (_, _) => UpdateExceptionButtons();

        _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _theme.Items.AddRange(["Как в системе", "Светлая", "Тёмная"]);

        _problems = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(200, 60, 60),
            Padding = new Padding(12, 4, 12, 4),
        };

        _save = new Button { Text = "Сохранить", Width = 110 };
        _save.Click += (_, _) => Save();

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        tabs.TabPages.Add(AccountingPage());
        tabs.TabPages.Add(SchedulePage());
        tabs.TabPages.Add(AppearancePage());

        Controls.Add(tabs);
        Controls.Add(Footer());

        Fill(_service.Settings);
        Watch();
    }

    /// <summary>Настройки сохранены — трею пора перерисоваться.</summary>
    public event EventHandler? Saved;

    // ---- вкладки -------------------------------------------------------------------

    private TabPage AccountingPage()
    {
        var page = new TabPage("Учёт времени") { Padding = new Padding(12), UseVisualStyleBackColor = true };

        var lunch = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12, 8, 12, 8),
        };

        lunch.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        lunch.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        lunch.Controls.Add(Caption("Обеденное окно, с"), 0, 0);
        lunch.Controls.Add(_windowStart, 1, 0);
        lunch.Controls.Add(Caption("Обеденное окно, по"), 0, 1);
        lunch.Controls.Add(_windowEnd, 1, 1);
        lunch.Controls.Add(Caption("Короче этого — не отлучка"), 0, 2);
        lunch.Controls.Add(_breakMinimum, 1, 2);
        lunch.Controls.Add(Caption("Короче этого — не обед"), 0, 3);
        lunch.Controls.Add(_lunchMinimum, 1, 3);

        _lunchBox.Controls.Add(lunch);

        var explain = new Label
        {
            Dock = DockStyle.Top,
            Height = 60,
            ForeColor = SystemColors.GrayText,
            Text = "Включено: короткая отлучка идёт в рабочее время, а из него вычитается только обед — "
                + "одна отлучка за день, попавшая в окно и достаточно длинная.\r\n"
                + "Выключено: счёт останавливает любая блокировка экрана, независимо от длительности.",
        };

        var rulesNote = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            ForeColor = SystemColors.GrayText,
            Text = "Правила расчёта действуют со следующего дня: сегодняшний уже посчитан по тем, "
                + "с которыми начался, и пересчитывать его половину задним числом нельзя. "
                + "Продолжительность дня — не правило, она подхватывается сразу.",
        };

        page.Controls.Add(_lunchBox);
        page.Controls.Add(_lunchDisabled);
        page.Controls.Add(explain);
        page.Controls.Add(_countShortBreaks);
        page.Controls.Add(rulesNote);

        return page;
    }

    private TabPage SchedulePage()
    {
        var page = new TabPage("График") { Padding = new Padding(12), UseVisualStyleBackColor = true };

        var week = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            Height = 230,
            Padding = new Padding(4),
        };

        week.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        week.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        week.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;
        foreach (var day in WeekSchedule.Days)
        {
            var hours = new DurationBox();
            var off = new CheckBox { Text = "нерабочий", AutoSize = true, Anchor = AnchorStyles.Left };

            var captured = day;
            off.CheckedChanged += (_, _) =>
            {
                _dayHours[captured].Enabled = !off.Checked;
                Revalidate();
            };

            hours.ValueChanged += (_, _) => Revalidate();

            _dayHours[day] = hours;
            _dayOff[day] = off;

            week.Controls.Add(Caption(Capitalize(SettingsCheck.DayName(day))), 0, row);
            week.Controls.Add(hours, 1, row);
            week.Controls.Add(off, 2, row);
            row++;
        }

        var exceptionsBox = new GroupBox { Text = "Исключения по датам", Dock = DockStyle.Fill };

        var add = new Button { Text = "Добавить…", Width = 110 };
        add.Click += (_, _) => AddException();
        _editException.Click += (_, _) => EditException();
        _removeException.Click += (_, _) => RemoveException();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 40,
            Padding = new Padding(6),
        };

        buttons.Controls.AddRange([add, _editException, _removeException]);

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(6, 0, 6, 0),
            Text = "Отпуск, праздники, сокращённые дни. Исключение сильнее недельного графика. "
                + "Производственный календарь не загружается — сеть в приложении не используется.",
        };

        exceptionsBox.Controls.Add(_exceptions);
        exceptionsBox.Controls.Add(buttons);
        exceptionsBox.Controls.Add(hint);

        page.Controls.Add(exceptionsBox);
        page.Controls.Add(week);

        return page;
    }

    private TabPage AppearancePage()
    {
        var page = new TabPage("Оформление") { Padding = new Padding(12), UseVisualStyleBackColor = true };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Height = 44,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(Caption("Тема окон"), 0, 0);
        layout.Controls.Add(_theme, 1, 0);

        var note = new Label
        {
            Dock = DockStyle.Top,
            Height = 74,
            ForeColor = SystemColors.GrayText,
            Text = "Тема применяется к окнам настроек и истории. На кольцо в трее она не влияет: "
                + "кольцо живёт на панели задач и следует её оформлению — иначе тёмное кольцо "
                + "на светлой панели станет невидимым.\r\n"
                + "Заголовок уже открытого окна догонит тему при следующем его открытии.",
        };

        page.Controls.Add(note);
        page.Controls.Add(layout);

        return page;
    }

    private Control Footer()
    {
        var openSettings = new Button { Text = "Файл настроек", Width = 130 };
        openSettings.Click += (_, _) => Reveal(_service.SettingsPath);

        var openData = new Button { Text = "Каталог данных", Width = 130 };
        openData.Click += (_, _) => Reveal(_service.DataRoot);

        var reset = new Button { Text = "Сбросить всё", Width = 120 };
        reset.Click += (_, _) => ResetToDefaults();

        var close = new Button { Text = "Закрыть", Width = 100, DialogResult = DialogResult.Cancel };

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
        };

        left.Controls.AddRange([openSettings, openData, reset]);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
        };

        right.Controls.AddRange([close, _save]);

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12, 6, 12, 6) };
        buttons.Controls.Add(left);
        buttons.Controls.Add(right);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 96 };
        footer.Controls.Add(_problems);
        footer.Controls.Add(buttons);

        CancelButton = close;
        return footer;
    }

    // ---- чтение и запись -----------------------------------------------------------

    private void Fill(AppSettings settings)
    {
        _filling = true;

        _countShortBreaks.Checked = settings.CountShortBreaks;
        _windowStart.Value = Today(settings.Lunch.WindowStart);
        _windowEnd.Value = Today(settings.Lunch.WindowEnd);
        _breakMinimum.Value = settings.Lunch.Minimum;
        _lunchMinimum.Value = settings.Lunch.GuessMinimum;

        foreach (var day in WeekSchedule.Days)
        {
            var hours = settings.Schedule[day];
            _dayOff[day].Checked = hours is null;
            _dayHours[day].Value = hours ?? WorkTimeCalculator.DefaultGoal;
            _dayHours[day].Enabled = hours is not null;
        }

        _draftExceptions = [.. settings.Exceptions];
        _theme.SelectedIndex = (int)settings.Theme;

        _filling = false;

        ShowExceptions();
        UpdateLunchEnabled();
        Revalidate();
    }

    /// <summary>Что сейчас набрано в форме.</summary>
    private AppSettings Collect()
    {
        var schedule = WeekSchedule.Default;
        foreach (var day in WeekSchedule.Days)
        {
            schedule = schedule.With(day, _dayOff[day].Checked ? null : _dayHours[day].Value);
        }

        return _service.Settings with
        {
            CountShortBreaks = _countShortBreaks.Checked,
            Lunch = new LunchRules(
                TimeOnly.FromDateTime(_windowStart.Value),
                TimeOnly.FromDateTime(_windowEnd.Value),
                _breakMinimum.Value,
                _lunchMinimum.Value),
            Schedule = schedule,
            Exceptions = _draftExceptions,
            Theme = (AppTheme)Math.Max(_theme.SelectedIndex, 0),
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

        // Предпочтение, а не правило: применяется немедленно, без перезапуска.
        WindowTheme.Apply(settings.Theme);

        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void ResetToDefaults()
    {
        var answer = MessageBox.Show(
            this,
            "Вернуть все настройки к значениям по умолчанию? Файлы дней это не затронет.",
            "GoHome",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer == DialogResult.Yes)
        {
            Fill(AppSettings.Default);
        }
    }

    /// <summary>Недопустимое значение не даёт сохранить и объясняет причину.</summary>
    private void Revalidate()
    {
        if (_filling)
        {
            return;
        }

        var problems = SettingsCheck.Validate(Collect());
        _problems.Text = string.Join(Environment.NewLine, problems.Select(problem => "• " + problem.Message));
        _save.Enabled = problems.Count == 0;
    }

    private void Watch()
    {
        _countShortBreaks.CheckedChanged += (_, _) =>
        {
            UpdateLunchEnabled();
            Revalidate();
        };

        _windowStart.ValueChanged += (_, _) => Revalidate();
        _windowEnd.ValueChanged += (_, _) => Revalidate();
        _breakMinimum.ValueChanged += (_, _) => Revalidate();
        _lunchMinimum.ValueChanged += (_, _) => Revalidate();
        _theme.SelectedIndexChanged += (_, _) => Revalidate();
    }

    /// <summary>
    /// При выключенном зачёте настройки обеда недоступны: они не влияют ни на что,
    /// а активные поля, ничего не делающие, — это обещание, которого форма не держит.
    /// </summary>
    private void UpdateLunchEnabled()
    {
        var enabled = _countShortBreaks.Checked;
        _lunchBox.Enabled = enabled;
        _lunchBox.Visible = enabled;
        _lunchDisabled.Visible = !enabled;
    }

    // ---- исключения ----------------------------------------------------------------

    private void ShowExceptions()
    {
        _draftExceptions.Sort((left, right) => left.Date.CompareTo(right.Date));

        _exceptions.BeginUpdate();
        _exceptions.Items.Clear();

        foreach (var exception in _draftExceptions)
        {
            var item = new ListViewItem(exception.Date.ToString("dd.MM.yyyy", Russian)) { Tag = exception };
            item.SubItems.Add(Capitalize(SettingsCheck.DayName(exception.Date.DayOfWeek)));
            item.SubItems.Add(exception.Hours is { } hours ? WorkTimeFormat.Duration(hours) : "нерабочий");
            item.SubItems.Add(exception.Note ?? string.Empty);

            if (exception.IsDayOff)
            {
                item.ForeColor = SystemColors.GrayText;
            }

            _exceptions.Items.Add(item);
        }

        _exceptions.EndUpdate();
        UpdateExceptionButtons();
    }

    private void UpdateExceptionButtons()
    {
        var selected = Selected() is not null;
        _editException.Enabled = selected;
        _removeException.Enabled = selected;
    }

    private DateException? Selected() =>
        _exceptions.SelectedItems.Count > 0 && _exceptions.SelectedItems[0].Tag is DateException exception
            ? exception
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
            _draftExceptions.RemoveAll(e => e.Date == exception.Date);
            Put(dialog.Result);
        }
    }

    private void RemoveException()
    {
        if (Selected() is { } exception)
        {
            _draftExceptions.RemoveAll(e => e.Date == exception.Date);
            ShowExceptions();
            Revalidate();
        }
    }

    private void Put(DateException exception)
    {
        _draftExceptions.RemoveAll(e => e.Date == exception.Date);
        _draftExceptions.Add(exception);
        ShowExceptions();
        Revalidate();
    }

    // ---- мелочи --------------------------------------------------------------------

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = new Padding(0, 6, 0, 0),
    };

    private static DateTimePicker TimePicker() => new()
    {
        Format = DateTimePickerFormat.Time,
        ShowUpDown = true,
        Width = 110,
    };

    private static DateTime Today(TimeOnly time) => DateTime.Today.Add(time.ToTimeSpan());

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Russian) + text[1..];

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
}