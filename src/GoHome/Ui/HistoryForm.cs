using System.Globalization;
using GoHome.App;
using GoHome.Core;

namespace GoHome.Ui;

/// <summary>История за неделю. Собирается кодом — дизайнер здесь ничего не добавляет.</summary>
public sealed class HistoryForm : Form
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ListView _days;
    private readonly ListView _intervals;
    private readonly Label _intervalsHint;
    private readonly Button _toggle;
    private readonly Label _total;

    /// <summary>Списки перестраиваются кодом, и выбор при этом меняется сам — это не правка человека.</summary>
    private bool _filling;

    public HistoryForm(GoHomeService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;

        Text = "GoHome — история за неделю";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 500);
        ClientSize = new Size(800, 560);
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
        _days.Columns.Add("Приход", 75, HorizontalAlignment.Center);
        _days.Columns.Add("Уход", 75, HorizontalAlignment.Center);
        _days.Columns.Add("Перерывы", 85, HorizontalAlignment.Center);
        _days.Columns.Add("Не в зачёт", 85, HorizontalAlignment.Center);
        _days.Columns.Add("Отработано", 95, HorizontalAlignment.Center);
        _days.Columns.Add("Баланс", 80, HorizontalAlignment.Center);
        _days.Columns.Add("Правила", 70, HorizontalAlignment.Center);
        _days.Columns.Add("", 60);
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

        _total = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(10, 6, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(_days);
        Controls.Add(breaks);
        Controls.Add(_total);

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

        var worked = HistoryCalculator.TotalWorked(history);
        var balance = HistoryCalculator.TotalBalance(history);
        var total = $"За неделю: {WorkTimeFormat.Duration(worked)}   ·   баланс {WorkTimeFormat.SignedDuration(balance)}";
        if (HistoryCalculator.HasMixedRules(history))
        {
            total += Environment.NewLine
                + "Дни по старым правилам считались иначе: из зачёта выпадала любая блокировка.";
        }

        _total.Text = total;
        ShowIntervals(keepInterval);
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

    private static string IntervalNote(BreakInterval interval, DaySummary day)
    {
        if (!day.OnlyLunchUnpaid)
        {
            return interval.IsUnpaid
                ? "по старым правилам любой перерыв вне зачёта"
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

    private static string RulesTitle(DaySummary day) => day.OnlyLunchUnpaid ? "новые" : "старые";

    private static string StateTitle(WorkState state) => state switch
    {
        WorkState.Working => "идёт",
        WorkState.OnBreak => "пауза",
        WorkState.NotStarted => "",
        _ => "",
    };
}
