using System.Text;
using GoHome.App;
using GoHome.Core;
using GoHome.Diagnostics;
using GoHome.Ui.Design;

namespace GoHome.Ui;

/// <summary>
/// Статистика за период: график, числа рядом с ним и выгрузка.
/// </summary>
/// <remarks>
/// Графики без чисел дают ощущение, а не ответ, поэтому сводка всегда на виду.
/// <para>
/// Год — это около двухсот пятидесяти файлов, и читаются они в фоне: окно не должно
/// подвисать на переключении периода. Прочитанное складывается в <see cref="_cache"/>
/// и на перерисовках больше не перечитывается.
/// </para>
/// </remarks>
internal sealed class StatsPanel : UserControl
{
    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<PeriodRange, PeriodStats> _cache = [];

    private readonly ComboBox _period;
    private readonly Label _title;
    private readonly Button _export;
    private readonly Label _totals;
    private readonly Label _averages;
    private readonly Label _note;
    private readonly DayBarChart _bars;
    private readonly YearHeatGrid _grid;

    private PeriodRange _range;
    private PeriodStats? _shown;

    /// <summary>Номер запроса: поздний ответ не должен перекрыть период, выбранный позже.</summary>
    private int _generation;

    public StatsPanel(GoHomeService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;
        _range = PeriodRange.Of(StatsPeriod.Week, WorkDay.DateOf(clock()));

        _period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _period.Items.AddRange(["Неделя", "Месяц", "Год"]);
        _period.SelectedIndex = 0;
        _period.SelectedIndexChanged += (_, _) => SwitchPeriod();

        var previous = new Button { Text = "‹", Width = 36 };
        previous.Click += (_, _) => Step(-1);

        var next = new Button { Text = "›", Width = 36 };
        next.Click += (_, _) => Step(1);

        var today = new Button { Text = "Сегодня", Width = 90 };
        today.Click += (_, _) => GoToToday();

        _title = new Label
        {
            AutoSize = true,
            Padding = new Padding(10, 6, 0, 0),
            Font = new Font(Font, FontStyle.Bold),
        };

        _export = new Button { Text = "Выгрузить в CSV", Width = 150, Dock = DockStyle.Right };
        _export.Click += (_, _) => Export();

        _bars = new DayBarChart { Dock = DockStyle.Fill };
        _grid = new YearHeatGrid { Dock = DockStyle.Fill, Visible = false };

        _totals = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(10, 2, 10, 0) };
        _averages = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(10, 2, 10, 0) };
        _note = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(10, 0, 10, 0),
            ForeColor = SystemColors.GrayText,
        };

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
        };

        controls.Controls.AddRange([_period, previous, next, today, _title]);

        var header = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 4, 6, 4) };
        header.Controls.Add(controls);
        header.Controls.Add(_export);

        var summary = new Panel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(0, 4, 0, 4) };
        summary.Controls.Add(_note);
        summary.Controls.Add(_averages);
        summary.Controls.Add(_totals);

        var canvas = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        canvas.Controls.Add(_bars);
        canvas.Controls.Add(_grid);

        Controls.Add(canvas);
        Controls.Add(summary);
        Controls.Add(header);

        Refill();
    }

    /// <summary>Период посчитан и показан. Нужно тесту: чтение асинхронное.</summary>
    internal bool Ready => _shown is not null;

    /// <summary>
    /// Ответ возвращается в UI-поток, а до создания хендла возвращать его некуда — такой
    /// расчёт пропадает молча. Панель создаётся раньше, чем окно показано, поэтому первый
    /// расчёт повторяется здесь.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_shown is null)
        {
            Refill();
        }
    }

    /// <summary>
    /// Перечитывает то, что могло измениться: сегодняшний день дописывается прямо сейчас,
    /// а правки настроек меняют цели. Периоды, целиком лежащие в прошлом, остаются в памяти.
    /// </summary>
    public void Reload()
    {
        var today = WorkDay.DateOf(_clock());
        foreach (var range in _cache.Keys.Where(range => range.Contains(today)).ToList())
        {
            _cache.Remove(range);
        }

        Refill();
    }

    private void SwitchPeriod()
    {
        var period = (StatsPeriod)Math.Max(_period.SelectedIndex, 0);
        var today = WorkDay.DateOf(_clock());

        // Держимся там, куда человек уже долистал: из недели в месяц — в тот месяц,
        // который эту неделю содержит, а не в нынешний.
        var anchor = _range.Contains(today) ? today : _range.Start;

        _range = PeriodRange.Of(period, anchor);
        Refill();
    }

    private void Step(int steps)
    {
        _range = _range.Shift(steps);
        Refill();
    }

    private void GoToToday()
    {
        _range = PeriodRange.Of(_range.Period, WorkDay.DateOf(_clock()));
        Refill();
    }

    private void Refill()
    {
        var range = _range;
        _title.Text = range.Title;

        _bars.Visible = range.Period != StatsPeriod.Year;
        _grid.Visible = range.Period == StatsPeriod.Year;

        if (_cache.TryGetValue(range, out var ready))
        {
            Apply(ready);
            return;
        }

        var generation = ++_generation;
        var now = _clock();

        _export.Enabled = false;
        _note.Text = "Читаю файлы дней…";

        Task.Run(() => _service.Stats(range, now))
            .ContinueWith(task => Finish(range, generation, task), TaskScheduler.Default);
    }

    /// <summary>Ответ пришёл на своём потоке — вернуть его в UI-поток и не упасть, если окно уже закрыли.</summary>
    private void Finish(PeriodRange range, int generation, Task<PeriodStats> task)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(() =>
            {
                if (generation != _generation)
                {
                    // Пока читали, человек уже листнул дальше.
                    return;
                }

                if (task.IsFaulted)
                {
                    ErrorLog.Default.Write($"не удалось посчитать статистику за {range.Title}", task.Exception);
                    _note.Text = "Не удалось прочитать файлы дней. Подробности — в журнале ошибок.";
                    return;
                }

                _cache[range] = task.Result;
                Apply(task.Result);
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Окно закрыли, пока читали: показывать результат уже некому.
        }
    }

    private void Apply(PeriodStats stats)
    {
        _shown = stats;
        _export.Enabled = true;

        var palette = Palette.Current();
        if (stats.Range.Period == StatsPeriod.Year)
        {
            _grid.Display(stats, palette);
        }
        else
        {
            _bars.Display(stats, palette);
        }

        _totals.Text = Totals(stats);
        _averages.Text = Averages(stats);
        _note.Text = stats.HasGaps
            ? $"Не прочиталось файлов дней: {stats.Unreadable}. Числа за период неполные — поправьте синтаксис JSON."
            : string.Empty;
    }

    private static string Totals(PeriodStats stats)
    {
        var text = new StringBuilder($"Отработано {WorkTimeFormat.Duration(stats.Worked)}");
        text.Append($" · норма периода {WorkTimeFormat.Duration(stats.Norm)}");

        // Незакончившийся период: полная норма ещё впереди, и сравнивать с ней бессмысленно.
        if (stats.NormSoFar != stats.Norm)
        {
            text.Append($" · к сегодняшнему дню {WorkTimeFormat.Duration(stats.NormSoFar)}");
        }

        text.Append($" · баланс {WorkTimeFormat.SignedDuration(stats.Balance)}");
        return text.ToString();
    }

    private static string Averages(PeriodStats stats)
    {
        if (stats.IsEmpty)
        {
            return "Средних пока нет: в этом периоде не работали ни дня.";
        }

        var parts = new List<string> { $"Рабочих дней {stats.WorkedDays}" };

        if (stats.Arrival is { } arrival)
        {
            parts.Add($"обычно приходите в {WorkTimeFormat.TimeOfDay(arrival)}");
        }

        if (stats.Departure is { } departure)
        {
            parts.Add($"уходите в {WorkTimeFormat.TimeOfDay(departure)}");
        }

        if (stats.DayLength is { } length)
        {
            parts.Add($"день {WorkTimeFormat.Duration(length)}");
        }

        parts.Add(stats.Unpaid is { } unpaid
            ? $"обед {WorkTimeFormat.Minutes(unpaid)}"
            : "обед не отмечался");

        return string.Join(" · ", parts);
    }

    private void Export()
    {
        if (_shown is not { } stats)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Выгрузить статистику",
            FileName = CsvExport.FileName(stats.Range),
            Filter = "CSV для Excel (*.csv)|*.csv|Все файлы (*.*)|*.*",
            DefaultExt = "csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // BOM обязателен: без него Excel читает файл в системной кодировке и портит кириллицу.
            File.WriteAllText(dialog.FileName, CsvExport.Build(stats), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorLog.Default.Write($"не удалось выгрузить статистику в {dialog.FileName}", ex);
            MessageBox.Show(
                this,
                "Не удалось записать файл: " + ex.Message,
                "GoHome",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
