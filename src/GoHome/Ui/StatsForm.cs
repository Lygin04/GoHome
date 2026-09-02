using System.Globalization;
using System.Text;
using GoHome.App;
using GoHome.Core;
using GoHome.Diagnostics;
using GoHome.Ui.Design;

namespace GoHome.Ui;

/// <summary>
/// Статистика за период: цифры, график и дни списком.
/// </summary>
/// <remarks>
/// Недельный список дней живёт здесь, а не в отдельном окне истории: смотреть на неделю
/// и на её дни по отдельности незачем, а щелчок по дню открывает форму дня.
/// <para>
/// Год — это около двухсот пятидесяти файлов, и читаются они в фоне: окно не должно
/// подвисать на переключении периода. Прочитанное складывается в память и на перерисовках
/// больше не перечитывается.
/// </para>
/// </remarks>
internal sealed class StatsForm : DesignForm
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] Periods = ["Неделя", "Месяц", "Год"];

    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<PeriodRange, PeriodStats> _cache = [];

    private readonly SegmentedTabs _period;
    private readonly StepButton _previous;
    private readonly StepButton _next;
    private readonly DesignButton _today;
    private readonly DesignButton _export;
    private readonly Title _title;

    private readonly DesignCard[] _cards;
    private readonly Figure[] _figures;

    private readonly DesignCard _chart;
    private readonly DayBarChart _bars;
    private readonly YearHeatGrid _grid;

    private readonly DayList _days;
    private readonly Notice _notice;

    private PeriodRange _range;
    private PeriodStats? _shown;

    /// <summary>Контролы созданы. Размер окна меняется раньше этого, и раскладывать ещё нечего.</summary>
    private bool _built;

    /// <summary>Номер запроса: поздний ответ не должен перекрыть период, выбранный позже.</summary>
    private int _generation;

    public StatsForm(GoHomeService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;
        _range = PeriodRange.Of(StatsPeriod.Week, WorkDay.DateOf(clock()));

        // Разворот есть только у статистики: год во весь экран читается лучше, чем год
        // в окне, а форме дня и настройкам лишняя ширина ничего не добавляет.
        MaximizeBox = true;
        SetMinimum(new Size(760, 560));
        SetInitialSize(new Size(1180, 760));

        _period = new SegmentedTabs { Items = Periods };
        _period.SelectedChanged += (_, _) => SwitchPeriod();

        _previous = new StepButton { Direction = Chevron.Left };
        _previous.Click += (_, _) => Step(-1);

        _next = new StepButton { Direction = Chevron.Right };
        _next.Click += (_, _) => Step(1);

        _today = new DesignButton { Text = "Сегодня" };
        _today.Click += (_, _) => GoToToday();

        _export = new DesignButton { Text = "Выгрузить CSV", Enabled = false };
        _export.Click += (_, _) => Export();

        _title = new Title();

        _figures = [new Figure(), new Figure(), new Figure(), new Figure()];
        _cards = [.. _figures.Select(figure =>
        {
            var card = new DesignCard();
            card.Controls.Add(figure);
            return card;
        })];

        _bars = new DayBarChart();
        _bars.DayActivated += (_, date) => Open(date);

        _grid = new YearHeatGrid { Visible = false };
        _grid.DayActivated += (_, date) => Open(date);

        _chart = new DesignCard { Label = "По дням" };
        _chart.Controls.Add(_bars);
        _chart.Controls.Add(_grid);

        _days = new DayList();
        _days.RowActivated += (_, _) => Open(_days.SelectedDate);

        _notice = new Notice { Visible = false };

        Content.Controls.AddRange([_period, _previous, _next, _today, _export, _title, _chart, _days, _notice]);
        Content.Controls.AddRange(_cards);

        _built = true;
        Refill();
    }

    /// <summary>Открыть день в форме дня. Ставится трей: окно дня одно на приложение.</summary>
    public event EventHandler<DateOnly>? DayRequested;

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

    /// <inheritdoc/>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Reload();
    }

    /// <inheritdoc/>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    /// <inheritdoc/>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;

            case Keys.Control | Keys.Left:
                Step(-1);
                return true;

            case Keys.Control | Keys.Right:
                Step(1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Узкое окно: карточки в два ряда, столбики уступают тепловой карте.</summary>
    private bool Narrow => Width < Sizes.NarrowBreakpoint;

    private void SwitchPeriod()
    {
        var period = (StatsPeriod)Math.Clamp(_period.Selected, 0, Periods.Length - 1);
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

    private void Open(DateOnly? date)
    {
        if (date is { } day)
        {
            DayRequested?.Invoke(this, day);
        }
    }

    private void Refill()
    {
        var range = _range;
        _title.Show(range.Title);

        var year = range.Period == StatsPeriod.Year;
        _bars.Visible = !year;
        _grid.Visible = year;

        // Год днями не расписывается: двести пятьдесят строк списком не читаются,
        // и для этого есть карта.
        _days.Visible = !year;

        if (_cache.TryGetValue(range, out var ready))
        {
            Apply(ready);
            return;
        }

        var generation = ++_generation;
        var now = _clock();

        _export.Enabled = false;
        _notice.Show("Читаю файлы дней…");
        Relayout();

        Task.Run(() => _service.Stats(range, now))
            .ContinueWith(task => Finish(range, generation, task), TaskScheduler.Default);
    }

    /// <summary>Ответ пришёл на своём потоке — вернуть его в UI-поток и не упасть, если окно закрыли.</summary>
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
                    _notice.Show("Не удалось прочитать файлы дней. Подробности — в журнале ошибок.");
                    Relayout();
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

        ShowFigures(stats, palette);
        _days.Show(stats);

        _notice.Show(stats.HasGaps
            ? $"Не прочиталось файлов дней: {stats.Unreadable}. Числа за период неполные — поправьте синтаксис JSON."
            : string.Empty);

        SetTitle("GoHome — статистика", "Статистика — " + stats.Range.Title);
        Relayout();
    }

    private void ShowFigures(PeriodStats stats, Palette palette)
    {
        var period = stats.Range.Period switch
        {
            StatsPeriod.Week => "за неделю",
            StatsPeriod.Month => "за месяц",
            _ => "за год",
        };

        _figures[0].Show(
            period,
            WorkTimeFormat.Duration(stats.Worked),
            palette.Ink,
            $"из {WorkTimeFormat.Duration(stats.NormSoFar)} · {WorkTimeFormat.SignedDuration(stats.Balance)}");

        _figures[1].Show(
            "средний день",
            stats.DayLength is { } length ? WorkTimeFormat.Duration(length) : "—",
            palette.Ink,
            stats.Arrival is { } arrival ? $"приход в среднем {WorkTimeFormat.TimeOfDay(arrival)}" : "приходов не было");

        _figures[2].Show(
            "обед не засчитан",
            stats.Unpaid is { } unpaid ? WorkTimeFormat.Minutes(unpaid) : "—",
            palette.Unpaid,
            stats.Departure is { } departure ? $"уход в среднем {WorkTimeFormat.TimeOfDay(departure)}" : "уходов не было");

        _figures[3].Show(
            "рабочих дней",
            stats.WorkedDays.ToString(CultureInfo.InvariantCulture),
            stats.IsEmpty ? palette.Faint : palette.Goal,
            stats.DaysOff > 0 ? $"нерабочих {stats.DaysOff}" : "нерабочих нет");
    }

    private void Relayout()
    {
        if (!_built)
        {
            return;
        }

        var metrics = Sizes;
        var pad = Narrow ? metrics.Space(4) : metrics.Space(5);
        var gap = metrics.Space(2);

        var left = pad;
        var right = Content.ClientSize.Width - pad;
        var width = Math.Max(metrics.Scale(200), right - left);
        var y = pad;

        // ---- строка управления периодом ----------------------------------------------
        _period.FitToItems();
        _today.FitToText();
        _export.FitToText();

        var row = Math.Max(_period.Height, metrics.ControlHeight);
        var centre = y + (row / 2);

        _period.Location = new Point(left, centre - (_period.Height / 2));

        var x = _period.Right + metrics.Space(4);
        _previous.Location = new Point(x, centre - (_previous.Height / 2));
        x = _previous.Right + gap;

        _next.Location = new Point(x, centre - (_next.Height / 2));
        x = _next.Right + gap;

        _today.Location = new Point(x, centre - (_today.Height / 2));
        x = _today.Right + metrics.Space(4);

        _export.Location = new Point(right - _export.Width, centre - (_export.Height / 2));
        _title.SetBounds(x, y, Math.Max(0, _export.Left - x - gap), row);

        y += row + metrics.Space(4);

        // ---- карточки с цифрами --------------------------------------------------------
        var columns = Narrow ? 2 : 4;
        var cardWidth = (width - (gap * (columns - 1))) / columns;
        // Отступы карточки с двух сторон: без них последняя строка пояснения обрезается.
        var cardHeight = _figures[0].PreferredHeight + (metrics.Space(4) * 2);

        for (var index = 0; index < _cards.Length; index++)
        {
            var column = index % columns;
            var line = index / columns;

            _cards[index].SetBounds(
                left + (column * (cardWidth + gap)),
                y + (line * (cardHeight + gap)),
                cardWidth,
                cardHeight);

            _figures[index].Bounds = _cards[index].ContentBounds;
        }

        y += ((cardHeight + gap) * (4 / columns)) - gap + metrics.Space(4);

        // ---- сообщение -------------------------------------------------------------------
        if (_notice.Visible)
        {
            _notice.SetBounds(left, y, width, metrics.Scale(40));
            y += _notice.Height + gap;
        }

        // ---- график и список ---------------------------------------------------------------
        var bottom = Content.ClientSize.Height - pad;
        var rest = Math.Max(metrics.Scale(120), bottom - y);

        if (_days.Visible)
        {
            // Список получает столько, сколько занимают его строки, но не больше двух пятых
            // оставшегося: график без высоты перестаёт быть графиком, а подписи его оси
            // начинают налезать друг на друга.
            var listHeight = _days.FittingHeight(Math.Min(_days.PreferredHeight, rest * 2 / 5));
            var chartHeight = rest - listHeight - gap;

            _chart.SetBounds(left, y, width, Math.Max(metrics.Scale(80), chartHeight));
            _days.SetBounds(left, _chart.Bottom + gap, width, listHeight);
        }
        else
        {
            _chart.SetBounds(left, y, width, rest);
        }

        var canvas = _chart.ContentBounds;
        _bars.Bounds = canvas;
        _grid.Bounds = canvas;
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
            File.WriteAllText(
                dialog.FileName,
                CsvExport.Build(stats),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorLog.Default.Write($"не удалось выгрузить статистику в {dialog.FileName}", ex);
            _notice.Show("Не удалось записать файл: " + ex.Message);
            Relayout();
        }
    }

    // ---- нарисованные части окна --------------------------------------------------------

    /// <summary>Название периода рядом с шагами.</summary>
    private sealed class Title : DrawnPanel
    {
        private string _text = string.Empty;

        public void Show(string text)
        {
            _text = text;
            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts) =>
            TextRenderer.DrawText(
                graphics,
                _text,
                fonts.Title,
                ClientRectangle,
                palette.Ink,
                Middle);
    }

    /// <summary>Одна цифра дня с меткой сверху и пояснением снизу.</summary>
    private sealed class Figure : DrawnPanel
    {
        private string _label = string.Empty;
        private string _value = string.Empty;
        private string _note = string.Empty;
        private Color _colour = Color.Empty;

        public int PreferredHeight =>
            Fonts.Label.Height + Sizes.Space(2) + Fonts.CardNumber.Height + Sizes.Space(2) + Fonts.Caption.Height;

        public void Show(string label, string value, Color colour, string note)
        {
            _label = label;
            _value = value;
            _colour = colour;
            _note = note;
            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
        {
            fonts.DrawLabel(graphics, _label, new Point(0, 0), palette.Faint);

            var top = fonts.Label.Height + metrics.Space(2);
            var number = Width < metrics.Scale(220) ? fonts.CardNumberNarrow : fonts.CardNumber;

            TextRenderer.DrawText(
                graphics,
                _value,
                number,
                new Point(0, top),
                _colour == Color.Empty ? palette.Ink : _colour,
                Tight);

            TextRenderer.DrawText(
                graphics,
                _note,
                fonts.Caption,
                new Rectangle(0, top + fonts.CardNumber.Height + metrics.Space(2), Width, fonts.Caption.Height),
                palette.Muted,
                Flat);
        }
    }

    /// <summary>Строка сообщения над содержимым.</summary>
    private sealed class Notice : DrawnPanel
    {
        private string _text = string.Empty;

        public void Show(string text)
        {
            _text = text;
            Visible = text.Length > 0;
            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts) =>
            TextRenderer.DrawText(
                graphics,
                _text,
                fonts.Caption,
                ClientRectangle,
                palette.Muted,
                Middle);
    }

    /// <summary>Дни периода строками: приход, уход, работа, отлучки, отклонение от цели.</summary>
    private sealed class DayList : DesignList
    {
        private readonly List<DaySummary> _days = [];

        public DayList()
        {
            Separators = true;
        }

        /// <summary>Дата выбранной строки — её и открывает форма дня.</summary>
        public DateOnly? SelectedDate =>
            SelectedIndex >= 0 && SelectedIndex < _days.Count ? _days[SelectedIndex].Date : null;

        /// <summary>Сколько места хотят строки вместе с шапкой.</summary>
        public int PreferredHeight => Sizes.HeaderHeight + (_days.Count * Sizes.RowHeight);

        public void Show(PeriodStats stats)
        {
            ArgumentNullException.ThrowIfNull(stats);

            _days.Clear();

            // Дни без единой отметки в список не идут: строка из одних прочерков ничего
            // не сообщает. Пустые выходные видно и без неё — по дыре в графике.
            _days.AddRange(stats.Days.Where(day => day.ArrivedAt is not null));

            Count = _days.Count;
        }

        /// <inheritdoc/>
        protected override int HeaderHeight => Sizes.HeaderHeight;

        /// <inheritdoc/>
        protected override void PaintHeader(Graphics graphics, Rectangle bounds, Palette palette)
        {
            ArgumentNullException.ThrowIfNull(graphics);

            var metrics = Sizes;
            var fonts = Fonts;
            var narrow = Width < metrics.Scale(720);

            using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
            {
                graphics.FillRectangle(back, bounds);
            }

            var inner = new Rectangle(RowPadding, bounds.Top, Math.Max(0, Width - (RowPadding * 2)), bounds.Height);
            var columns = Columns(inner, metrics, narrow);
            var titles = narrow
                ? new[] { "день", "приход → уход", "работа", "к цели" }
                : ["день", "приход → уход", "работа", "отлучки", "к цели"];

            for (var index = 0; index < titles.Length && index < columns.Length; index++)
            {
                var box = columns[index];
                var width = fonts.MeasureLabel(titles[index]);
                var x = index < 2 ? box.Left : box.Right - width;

                fonts.DrawLabel(
                    graphics,
                    titles[index],
                    new Point(x, box.Top + ((box.Height - fonts.Label.Height) / 2)),
                    palette.Faint);
            }

            Draw.Separator(graphics, RowPadding, Width - RowPadding, bounds.Bottom - 1, palette.Line, metrics);
        }

        /// <inheritdoc/>
        protected override void PaintRow(
            Graphics graphics,
            int index,
            Rectangle bounds,
            Palette palette,
            RowState state)
        {
            var day = _days[index];
            var metrics = Sizes;
            var fonts = Fonts;
            var narrow = Width < metrics.Scale(720);
            var columns = Columns(bounds, metrics, narrow);

            TextRenderer.DrawText(
                graphics,
                day.Date.ToString(narrow ? "dd.MM" : "ddd d MMM", Russian),
                fonts.Body,
                columns[0],
                day.IsDayOff ? RowMuted(palette, state) : RowInk(palette, state),
                Middle);

            var span = day.ArrivedAt is { } arrived
                ? WorkTimeFormat.Clock(arrived) + " → " + (day.LeftAt is { } left ? WorkTimeFormat.Clock(left) : "идёт")
                : "—";

            TextRenderer.DrawText(graphics, span, fonts.Number, columns[1], RowMuted(palette, state), Middle);

            TextRenderer.DrawText(
                graphics,
                day.ArrivedAt is null ? "—" : WorkTimeFormat.Duration(day.Worked),
                fonts.Number,
                columns[2],
                RowInk(palette, state),
                Middle | TextFormatFlags.Right);

            if (!narrow)
            {
                TextRenderer.DrawText(
                    graphics,
                    WorkTimeFormat.Duration(day.Breaks),
                    fonts.Number,
                    columns[3],
                    RowMuted(palette, state),
                    Middle | TextFormatFlags.Right);
            }

            var (goal, colour) = Goal(day, palette, state);
            TextRenderer.DrawText(graphics, goal, fonts.Number, columns[^1], colour, Middle | TextFormatFlags.Right);
        }

        /// <summary>
        /// Отклонение от цели.
        /// </summary>
        /// <remarks>
        /// У нерабочего дня цели нет, поэтому минуса у него не бывает: всё отработанное
        /// в нём идёт в плюс. Это и показывается — знаком, а не словами про график.
        /// </remarks>
        private static (string Text, Color Colour) Goal(DaySummary day, Palette palette, RowState state)
        {
            if (day.ArrivedAt is null)
            {
                return ("—", RowMuted(palette, state));
            }

            var balance = day.Balance;
            var colour = state.Selected
                ? palette.SelectionInk
                : balance >= TimeSpan.Zero ? palette.Goal : palette.Danger;

            return (WorkTimeFormat.SignedDuration(balance), colour);
        }

        /// <summary>Колонки строки. В узком окне отлучки уходят: они здесь наименее нужны.</summary>
        private static Rectangle[] Columns(Rectangle bounds, Metrics metrics, bool narrow)
        {
            var number = metrics.Scale(narrow ? 64 : 86);
            var date = metrics.Scale(narrow ? 60 : 110);
            var count = narrow ? 4 : 5;

            var columns = new Rectangle[count];
            var right = bounds.Right;

            for (var index = count - 1; index >= 2; index--)
            {
                columns[index] = new Rectangle(right - number, bounds.Top, number, bounds.Height);
                right -= number + metrics.Space(2);
            }

            columns[0] = new Rectangle(bounds.Left, bounds.Top, date, bounds.Height);
            columns[1] = Rectangle.FromLTRB(
                bounds.Left + date + metrics.Space(2),
                bounds.Top,
                Math.Max(bounds.Left + date, right),
                bounds.Bottom);

            return columns;
        }
    }
}
