using System.Globalization;
using WorkClock.App;
using WorkClock.Core;

namespace WorkClock.Ui;

/// <summary>История за неделю. Собирается кодом — дизайнер здесь ничего не добавляет.</summary>
public sealed class HistoryForm : Form
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly WorkClockService _service;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ListView _days;
    private readonly Label _total;

    public HistoryForm(WorkClockService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;

        Text = "WorkClock — история за неделю";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(620, 320);
        ClientSize = new Size(680, 360);
        ShowIcon = false;
        MinimizeBox = false;

        _days = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            UseCompatibleStateImageBehavior = false,
        };

        _days.Columns.Add("День", 130);
        _days.Columns.Add("Приход", 80, HorizontalAlignment.Center);
        _days.Columns.Add("Уход", 80, HorizontalAlignment.Center);
        _days.Columns.Add("Перерывы", 90, HorizontalAlignment.Center);
        _days.Columns.Add("Отработано", 100, HorizontalAlignment.Center);
        _days.Columns.Add("Баланс", 90, HorizontalAlignment.Center);
        _days.Columns.Add("", 60);

        _total = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            Padding = new Padding(10, 8, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(_days);
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
        var now = _clock();
        var history = _service.History(now, 7);

        _days.BeginUpdate();
        _days.Items.Clear();

        foreach (var day in history)
        {
            var item = new ListViewItem(DayTitle(day.Date));
            item.SubItems.Add(WorkTimeFormat.Clock(day.ArrivedAt));
            item.SubItems.Add(WorkTimeFormat.Clock(day.LeftAt));
            item.SubItems.Add(day.State == WorkState.NotStarted ? "—" : WorkTimeFormat.Duration(day.Breaks));
            item.SubItems.Add(day.State == WorkState.NotStarted ? "—" : WorkTimeFormat.Duration(day.Worked));
            item.SubItems.Add(day.State == WorkState.NotStarted ? "—" : WorkTimeFormat.SignedDuration(day.Balance));
            item.SubItems.Add(StateTitle(day.State));

            if (day.State == WorkState.NotStarted)
            {
                item.ForeColor = SystemColors.GrayText;
            }

            _days.Items.Add(item);
        }

        _days.EndUpdate();

        var worked = HistoryCalculator.TotalWorked(history);
        var balance = HistoryCalculator.TotalBalance(history);
        _total.Text = $"За неделю: {WorkTimeFormat.Duration(worked)}   ·   баланс {WorkTimeFormat.SignedDuration(balance)}";
    }

    private static string DayTitle(DateOnly date) =>
        date.ToString("dd.MM", Russian) + ", " + date.ToDateTime(TimeOnly.MinValue).ToString("dddd", Russian);

    private static string StateTitle(WorkState state) => state switch
    {
        WorkState.Working => "идёт",
        WorkState.OnBreak => "пауза",
        WorkState.NotStarted => "",
        _ => "",
    };
}