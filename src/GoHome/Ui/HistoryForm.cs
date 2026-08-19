using System.Globalization;
using GoHome.App;
using GoHome.Core;

namespace GoHome.Ui;

/// <summary>
/// История за неделю и статистика за период. Собирается кодом — дизайнер здесь
/// ничего не добавляет.
/// </summary>
/// <remarks>
/// Два взгляда на одни и те же файлы дней, поэтому одно окно с вкладками, а не два:
/// в историю заходят починить вчерашний день, в статистику — посмотреть, как шёл месяц,
/// и переключаться между этим приходится постоянно.
/// </remarks>
public sealed class HistoryForm : Form
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ListView _days;
    private readonly ListView _intervals;
    private readonly Label _intervalsHint;
    private readonly Button _toggle;
    private readonly Button _markDay;
    private readonly Button _clearDay;
    private readonly Label _total;
    private readonly StatsPanel _stats;

    /// <summary>Списки перестраиваются кодом, и выбор при этом меняется сам — это не правка человека.</summary>
    private bool _filling;

    public HistoryForm(GoHomeService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;

        Text = "GoHome — история и статистика";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        ClientSize = new Size(880, 620);
        Icon = AppIcon.ForWindows;
        ShowIcon = Icon is not null;
        MinimizeBox = false;

        _days = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            HideSelection = false,
            UseCompatibleStateImageBehavior = false,
        };

        _days.Columns.Add("День", 130);
        _days.Columns.Add("Цель", 70, HorizontalAlignment.Center);
        _days.Columns.Add("Приход", 70, HorizontalAlignment.Center);
        _days.Columns.Add("Уход", 70, HorizontalAlignment.Center);
        _days.Columns.Add("Перерывы", 80, HorizontalAlignment.Center);
        _days.Columns.Add("Не в зачёт", 80, HorizontalAlignment.Center);
        _days.Columns.Add("Отработано", 90, HorizontalAlignment.Center);
        _days.Columns.Add("Баланс", 75, HorizontalAlignment.Center);
        _days.Columns.Add("Отлучки", 85, HorizontalAlignment.Center);
        _days.Columns.Add("", 55);
        _days.SelectedIndexChanged += (_, _) => ShowIntervals();

        // Галочек здесь намеренно нет. ListView присылает уведомление о смене галочки
        // и тогда, когда список перестраивают кодом, причём с задержкой — отличить такое
        // уведомление от щелчка человека по состоянию невозможно, и только что угаданный
        // обед молча возвращался в зачёт. Явное действие по выбранной строке однозначно.
        _intervals = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            HideSelection = false,
            UseCompatibleStateImageBehavior = false,
        };

        _intervals.Columns.Add("Перерыв", 130);
        _intervals.Columns.Add("Длительность", 115, HorizontalAlignment.Center);
        _intervals.Columns.Add("Зачёт", 90, HorizontalAlignment.Center);
        _intervals.Columns.Add("", 400);
        _intervals.SelectedIndexChanged += (_, _) => UpdateToggle();
        _intervals.DoubleClick += (_, _) => ToggleSelected();
        _intervals.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                e.Handled = true;
                ToggleSelected();
            }
        };

        _intervalsHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(10, 6, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _toggle = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            Enabled = false,
            Text = "Переклассифицировать перерыв",
        };
        _toggle.Click += (_, _) => ToggleSelected();

        var breaks = new Panel { Dock = DockStyle.Bottom, Height = 200 };
        breaks.Controls.Add(_intervals);
        breaks.Controls.Add(_toggle);
        breaks.Controls.Add(_intervalsHint);

        // Пометить день особым задним числом нужно чаще, чем заранее: про отгул вспоминают,
        // когда смотрят на минус в балансе, а не когда его планируют.
        _markDay = new Button { Text = "Пометить день…", Width = 150, Enabled = false };
        _markDay.Click += (_, _) => MarkSelectedDay();

        _clearDay = new Button { Text = "Вернуть в график", Width = 150, Enabled = false };
        _clearDay.Click += (_, _) => ClearSelectedDay();

        var dayActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Height = 38,
            Padding = new Padding(8, 4, 8, 4),
        };

        dayActions.Controls.AddRange([_markDay, _clearDay]);

        _total = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(10, 6, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var historyPage = new TabPage("История за неделю") { UseVisualStyleBackColor = true };
        historyPage.Controls.Add(_days);
        historyPage.Controls.Add(dayActions);
        historyPage.Controls.Add(breaks);
        historyPage.Controls.Add(_total);

        _stats = new StatsPanel(_service, _clock) { Dock = DockStyle.Fill };

        var statsPage = new TabPage("Статистика") { UseVisualStyleBackColor = true };
        statsPage.Controls.Add(_stats);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(historyPage);
        tabs.TabPages.Add(statsPage);

        Controls.Add(tabs);

        Reload();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Reload();
    }

    /// <summary>Перечитывает журналы с диска: файлы могли поправить руками.</summary>
    public void Reload()
    {
        var keepDay = SelectedDay()?.Date;
        var keepInterval = SelectedInterval()?.Start;
        var now = _clock();
        var history = _service.History(now, 7);

        _filling = true;
        _days.BeginUpdate();
        _days.Items.Clear();

        foreach (var day in history)
        {
            var started = day.State != WorkState.NotStarted;
            var item = new ListViewItem(DayTitle(day.Date)) { Tag = day };
            item.SubItems.Add(GoalTitle(day));
            item.SubItems.Add(WorkTimeFormat.Clock(day.ArrivedAt));
            item.SubItems.Add(WorkTimeFormat.Clock(day.LeftAt));
            item.SubItems.Add(started ? WorkTimeFormat.Duration(day.Breaks) : "—");
            item.SubItems.Add(day.Unpaid > TimeSpan.Zero ? WorkTimeFormat.Duration(day.Unpaid) : "—");
            item.SubItems.Add(started ? WorkTimeFormat.Duration(day.Worked) : "—");
            item.SubItems.Add(started ? WorkTimeFormat.SignedDuration(day.Balance) : "—");
            item.SubItems.Add(started ? RulesTitle(day) : string.Empty);
            item.SubItems.Add(StateTitle(day.State));

            if (!started)
            {
                item.ForeColor = SystemColors.GrayText;
            }

            _days.Items.Add(item);
            if (day.Date == keepDay)
            {
                item.Selected = true;
            }
        }

        if (_days.SelectedItems.Count == 0 && _days.Items.Count > 0)
        {
            _days.Items[0].Selected = true;
        }

        _days.EndUpdate();
        _filling = false;

        // Недельный баланс — про календарную неделю с понедельника, а не про последние
        // семь дней в списке выше. Это разные числа, и подписаны они по-разному.
        var week = _service.Week(now);
        var total = $"Неделя {DayTitle(week.Start)} — {DayTitle(week.End)}: "
            + $"{WorkTimeFormat.Duration(week.Worked)} из {WorkTimeFormat.Duration(week.Norm)}"
            + $"   ·   баланс {WorkTimeFormat.SignedDuration(week.Balance)}";

        if (HistoryCalculator.HasMixedRules(history))
        {
            total += Environment.NewLine
                + "Дни считались по разным правилам: где-то из зачёта выпадала любая блокировка, где-то только обед.";
        }

        _total.Text = total;
        ShowIntervals(keepInterval);
        UpdateDayActions();

        // Статистика читает те же файлы: сегодняшний день дописывается прямо сейчас,
        // а прошлые периоды перечитывать незачем — они уже прочитаны.
        _stats.Reload();
    }

    /// <summary>Перерывы выбранного дня.</summary>
    private void ShowIntervals(DateTimeOffset? keep = null)
    {
        if (_filling)
        {
            return;
        }

        var day = SelectedDay();

        _filling = true;
        _intervals.BeginUpdate();
        _intervals.Items.Clear();

        if (day is null)
        {
            _intervalsHint.Text = "Выберите день, чтобы увидеть его перерывы.";
        }
        else if (day.Intervals.Count == 0)
        {
            _intervalsHint.Text = $"{DayTitle(day.Date)} — перерывов, о которых стоит говорить, не было.";
        }
        else
        {
            _intervalsHint.Text = $"{DayTitle(day.Date)} — выберите перерыв и нажмите кнопку внизу.";

            foreach (var interval in day.Intervals)
            {
                var item = new ListViewItem(
                    $"{WorkTimeFormat.Clock(interval.Start)}–{WorkTimeFormat.Clock(interval.End)}")
                {
                    Tag = interval,
                };

                item.SubItems.Add(WorkTimeFormat.Minutes(interval.Duration));
                item.SubItems.Add(interval.IsUnpaid ? "не идёт" : "идёт");
                item.SubItems.Add(IntervalNote(interval, day));

                if (interval.IsUnpaid)
                {
                    item.ForeColor = SystemColors.GrayText;
                }

                _intervals.Items.Add(item);
                if (keep is { } start && interval.Start == start)
                {
                    item.Selected = true;
                }
            }

            // Без выбранной строки кнопка внизу мертва, и непонятно, что с ней делать.
            if (_intervals.SelectedItems.Count == 0)
            {
                _intervals.Items[0].Selected = true;
            }
        }

        _intervals.EndUpdate();
        _filling = false;
        UpdateToggle();
    }

    private void UpdateToggle()
    {
        var interval = SelectedInterval();
        _toggle.Enabled = interval is not null;
        _toggle.Text = interval switch
        {
            null => "Выберите перерыв",
            { IsUnpaid: true } => "Вернуть перерыв в рабочее время",
            _ => "Не засчитывать этот перерыв",
        };
    }

    private void ToggleSelected()
    {
        if (SelectedInterval() is not { } interval || SelectedDay() is not { } day)
        {
            return;
        }

        var kind = interval.IsUnpaid ? BreakKind.Paid : BreakKind.Unpaid;
        _service.Reclassify(day.Date, interval.Start, kind, "history");
        Reload();
    }

    /// <summary>Пометить выбранный день нерабочим или задать ему свою продолжительность.</summary>
    private void MarkSelectedDay()
    {
        if (SelectedDay() is not { } day)
        {
            return;
        }

        var settings = _service.Settings;
        using var dialog = new DayExceptionDialog(
            day.Date,
            settings.ExceptionFor(day.Date),
            settings.Schedule[day.Date.DayOfWeek],
            lockDate: true);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _service.SaveSettings(settings.WithException(day.Date, dialog.Result), _clock());

            // День уже прожит, и его снимок пора обновить: цель — единственное, что можно
            // поправить задним числом, не пересчитывая уже засчитанные минуты по-другому.
            _service.RefreshGoal(day.Date);
            Reload();
        }
    }

    private void ClearSelectedDay()
    {
        if (SelectedDay() is not { } day)
        {
            return;
        }

        _service.SaveSettings(_service.Settings.WithException(day.Date, null), _clock());
        _service.RefreshGoal(day.Date);
        Reload();
    }

    private void UpdateDayActions()
    {
        var day = SelectedDay();
        _markDay.Enabled = day is not null;
        _clearDay.Enabled = day is not null && _service.Settings.ExceptionFor(day.Date) is not null;
    }

    private static string IntervalNote(BreakInterval interval, DaySummary day)
    {
        if (!day.CountsShortBreaks)
        {
            return interval.IsUnpaid
                ? "короткие отлучки не засчитываются, перерыв вне зачёта"
                : "возвращён в зачёт вручную";
        }

        return interval switch
        {
            { Guessed: true } => "определён обедом автоматически",
            { IsUnpaid: true } => "помечен вручную",
            _ => "идёт в рабочее время",
        };
    }

    private DaySummary? SelectedDay() =>
        _days.SelectedItems.Count > 0 && _days.SelectedItems[0].Tag is DaySummary day ? day : null;

    private BreakInterval? SelectedInterval() =>
        _intervals.SelectedItems.Count > 0 && _intervals.SelectedItems[0].Tag is BreakInterval interval
            ? interval
            : null;

    private static string DayTitle(DateOnly date) =>
        date.ToString("dd.MM", Russian) + ", " + date.ToDateTime(TimeOnly.MinValue).ToString("dddd", Russian);

    /// <summary>Цель дня. У нерабочего её нет, и это не ноль часов.</summary>
    private static string GoalTitle(DaySummary day) =>
        day.IsDayOff ? "нерабочий" : WorkTimeFormat.Duration(day.Goal);

    private static string RulesTitle(DaySummary day) => day.CountsShortBreaks ? "в зачёт" : "режутся";

    private static string StateTitle(WorkState state) => state switch
    {
        WorkState.Working => "идёт",
        WorkState.OnBreak => "пауза",
        WorkState.NotStarted => "",
        _ => "",
    };
}
