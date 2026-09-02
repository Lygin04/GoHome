using System.Diagnostics;
using System.Globalization;
using GoHome.App;
using GoHome.Core;
using GoHome.Ui.Design;

namespace GoHome.Ui;

/// <summary>
/// Один день целиком: полоса времени, отрезки списком и цифры дня.
/// </summary>
/// <remarks>
/// Заменяет открытие файла журнала в блокноте. Файл при этом остаётся обычным файлом,
/// и править его руками по-прежнему можно — форма это удобный путь, а не единственный.
/// <para>
/// Список и полоса строятся из одного <see cref="DayBand"/>: если бы они считали отрезки
/// по отдельности, разойтись им было бы негде, кроме как на глазах у человека.
/// </para>
/// </remarks>
internal sealed class DayForm : DesignForm
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;

    private readonly StepButton _previous;
    private readonly StepButton _next;
    private readonly Header _header;
    private readonly Figures _figures;
    private readonly DayTimeline _timeline;
    private readonly Legend _legend;
    private readonly SegmentList _list;
    private readonly DesignCard _numbers;
    private readonly Numbers _numbersBody;
    private readonly Notice _notice;
    private readonly DesignButton _reveal;

    private readonly DesignButton _toggle;
    private readonly DesignButton _edit;
    private readonly DesignButton _delete;
    private readonly BreakEditor _editor;
    private readonly Confirmation _confirm;

    private readonly FileSystemWatcher _watcher;
    private readonly System.Windows.Forms.Timer _settle;

    private DateOnly _date;
    private DayPage? _page;
    private DayBand? _band;

    /// <summary>Как отменить последнюю правку. Удаление сюда не попадает — его не вернуть.</summary>
    private Action? _undo;

    public DayForm(GoHomeService service, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;
        _date = WorkDay.DateOf(clock());

        // Разворота у формы дня нет: её содержимое не становится полезнее во весь экран,
        // а минимум задан содержимым, а не догадкой.
        MaximizeBox = false;
        SetMinimum(new Size(700, 520));
        SetInitialSize(new Size(1180, 720));

        _previous = new StepButton { Direction = Chevron.Left };
        _previous.Click += (_, _) => Step(-1);

        _next = new StepButton { Direction = Chevron.Right };
        _next.Click += (_, _) => Step(1);

        _header = new Header();
        _figures = new Figures();
        _timeline = new DayTimeline();
        _legend = new Legend();

        _list = new SegmentList();
        _list.SelectionChanged += (_, _) => ShowSelection();

        _numbersBody = new Numbers();
        _numbers = new DesignCard { Label = "День в цифрах" };
        _numbers.Controls.Add(_numbersBody);

        _notice = new Notice { Visible = false };

        _reveal = new DesignButton { Text = "Показать файл дня" };
        _reveal.Click += (_, _) => Reveal();

        _toggle = new DesignButton { Kind = ButtonKind.Primary, Text = "Засчитать как работу" };
        _toggle.Click += (_, _) => ToggleSelected();

        _edit = new DesignButton { Text = "Изменить время" };
        _edit.Click += (_, _) => OpenEditor();

        _delete = new DesignButton { Kind = ButtonKind.Danger, Text = "Удалить" };
        _delete.Click += (_, _) => AskToDelete();

        _editor = new BreakEditor { Visible = false };
        _editor.Edited += (_, _) => Revalidate();
        _editor.Accepted += (_, _) => SaveEditor();
        _editor.Cancelled += (_, _) => CloseSlot();

        _confirm = new Confirmation { Visible = false };
        _confirm.Accepted += (_, _) => DeleteSelected();
        _confirm.Cancelled += (_, _) => CloseSlot();

        Content.Controls.AddRange(
        [
            _previous,
            _next,
            _header,
            _figures,
            _timeline,
            _legend,
            _list,
            _numbers,
            _notice,
            _editor,
            _confirm,
            _toggle,
            _edit,
            _delete,
            _reveal,
        ]);

        // Служба пишет в тот же файл, пока окно открыто. Держать прочитанное и записывать
        // поверх нельзя — значит, надо замечать чужую запись и перечитывать.
        _settle = new System.Windows.Forms.Timer { Interval = 250 };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Reload();
        };

        _watcher = Watch();

        Reload();
    }

    /// <summary>Открывает форму на конкретном дне — из статистики.</summary>
    public void ShowDate(DateOnly date)
    {
        _date = Clamp(date);
        Reload();
    }

    /// <summary>Перечитывает день с диска: файл могли поправить руками или дописать службой.</summary>
    public void Reload()
    {
        var now = _clock();
        var page = _service.OpenDay(_date, now);
        var band = DayBand.For(page.Summary, now, TickStep);

        // Выбор держится за время, а не за номер строки: после правки строк может стать
        // меньше, и номер указал бы на чужой отрезок.
        var keep = _list.SelectedSegment?.Start;

        _page = page;
        _band = band;

        Text = "День — " + _date.ToString("d MMMM", Russian);

        _header.Show(page.Summary, _date, Narrow);
        _figures.Show(page.Summary, Narrow);
        _timeline.Narrow = Narrow;
        _timeline.Show(band);
        _legend.Show(band);
        _numbersBody.Show(page.Summary);
        _list.Show(band, page.Summary, page.Edited);
        _list.SelectBreakAt(keep);

        // Повреждённый файл не притворяется пустым днём: показывать в нём нечего,
        // и предлагать правку того, что не прочиталось, тем более нельзя.
        _notice.Text = page.Unreadable
            ? "Файл дня не разбирается. Приложение его не перезаписывает — поправьте синтаксис JSON, "
                + "и день появится. До тех пор он показан пустым."
            : band.IsEmpty
                ? "В этот день событий не было."
                : string.Empty;

        _notice.Visible = _notice.Text.Length > 0;
        var visible = !page.Unreadable && !band.IsEmpty;
        _timeline.Visible = visible;
        _legend.Visible = visible;
        _list.Visible = visible;
        _numbers.Visible = visible && !Narrow && Width >= Sizes.WideBreakpoint;

        // Вперёд дальше сегодняшнего дня ходить некуда: того дня ещё не было.
        _next.Enabled = _date < WorkDay.DateOf(now);

        // Открытая правка переживает чужую запись в файл, если правимый перерыв на месте.
        if (_editor.Visible && _list.SelectedBreak is null)
        {
            CloseSlot();
        }

        ShowSelection();
        Relayout();
    }

    /// <inheritdoc/>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Reload();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Наблюдатель держит хендл каталога и поток пула. Закрытое окно не должно
            // ни того ни другого.
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _settle.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    /// <summary>Влево и вправо ходят по дням, когда фокус не в списке.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;

            case Keys.Control | Keys.Left when _previous.Enabled:
                Step(-1);
                return true;

            case Keys.Control | Keys.Right when _next.Enabled:
                Step(1);
                return true;

            case Keys.Control | Keys.Z when _undo is not null:
                var undo = _undo;
                _undo = null;
                undo();
                CloseSlot();
                Reload();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Узкое окно: строка сжимается, метки часов редеют, боковая колонка уходит.</summary>
    private bool Narrow => Width < Sizes.NarrowBreakpoint;

    /// <summary>Метки часов через два часа на широком окне и через три на узком.</summary>
    private int TickStep => Narrow ? 3 : 2;

    private void Step(int days)
    {
        _date = Clamp(_date.AddDays(days));
        _list.SelectedIndex = -1;
        Reload();
    }

    /// <summary>Дальше сегодняшнего дня не пускаем: того дня ещё не было.</summary>
    private DateOnly Clamp(DateOnly date)
    {
        var today = WorkDay.DateOf(_clock());
        return date > today ? today : date;
    }

    private void ShowSelection()
    {
        _timeline.Selected = _list.SelectedSegment?.Start;
        _timeline.Invalidate();

        // Править можно отлучку, но не работу: работа — это то, что осталось между
        // отлучками, и своих отметок в журнале у неё нет.
        var interval = _list.SelectedBreak;
        var editable = interval is not null && !(_page?.Unreadable ?? false);

        _toggle.Enabled = editable;
        _edit.Enabled = editable;
        _delete.Enabled = editable;

        // Без выбора кнопка носит своё обычное имя, а не имя обратного действия.
        _toggle.Text = interval is null || interval.IsUnpaid ? "Засчитать как работу" : "Не засчитывать";

        Relayout();
    }

    /// <summary>Меняет зачёт выбранной отлучки. Ручная правка сильнее догадки.</summary>
    private void ToggleSelected()
    {
        if (_list.SelectedBreak is not { } interval)
        {
            return;
        }

        var kind = interval.IsUnpaid ? BreakKind.Paid : BreakKind.Unpaid;
        var back = interval.IsUnpaid ? BreakKind.Unpaid : BreakKind.Paid;
        var at = interval.Start;
        var date = _date;

        if (_service.Reclassify(date, at, kind, "day-form"))
        {
            _undo = () => _service.Reclassify(date, at, back, "day-form");
            CloseSlot();
            Reload();
        }
    }

    private void OpenEditor()
    {
        if (_list.SelectedBreak is not { } interval)
        {
            return;
        }

        _confirm.Visible = false;
        _editor.Visible = true;
        _editor.Open(interval.Start, interval.End);
        Relayout();
        _editor.FocusStart();
        Revalidate();
    }

    /// <summary>Спрашивает у тех же правил, годится ли набранное, — до попытки сохранить.</summary>
    private void Revalidate()
    {
        if (_list.SelectedBreak is not { } interval || _editor.Value is not { } value)
        {
            _editor.Refuse(_editor.Value is null ? "Время набрано не полностью." : null);
            return;
        }

        var (start, end) = Rebase(interval, value);
        _editor.Refuse(_service.CheckBreak(_date, interval.Start, start, end));
    }

    private void SaveEditor()
    {
        if (_list.SelectedBreak is not { } interval || _editor.Value is not { } value)
        {
            return;
        }

        var (start, end) = Rebase(interval, value);
        var refusal = _service.MoveBreak(_date, interval.Start, start, end);

        if (refusal is not null)
        {
            _editor.Refuse(refusal);
            return;
        }

        // Отмена возвращает прежние границы той же правкой, а не записью старой копии:
        // журнал перечитывается под замком, и чужая запись при этом не теряется.
        var date = _date;
        var wasStart = interval.Start;
        var wasEnd = interval.End;
        _undo = () => _service.MoveBreak(date, start, wasStart, wasEnd);

        CloseSlot();
        _list.SelectBreakAt(start);
        Reload();
    }

    private void AskToDelete()
    {
        if (_list.SelectedBreak is not { } interval)
        {
            return;
        }

        _editor.Visible = false;
        _confirm.Visible = true;
        _confirm.Ask(
            "Удалить перерыв " + WorkTimeFormat.Clock(interval.Start) + "–" + WorkTimeFormat.Clock(interval.End)
                + "? Время до и после него сомкнётся в работу. Отменить это нельзя.");

        Relayout();
    }

    private void DeleteSelected()
    {
        if (_list.SelectedBreak is { } interval && _service.RemoveBreak(_date, interval.Start))
        {
            // Удаление не отменяется: вернуть отметки, которых больше нет, нечем.
            // Поэтому оно и спрашивает подтверждение, а отмена при этом очищается.
            _undo = null;
            _list.SelectedIndex = -1;
        }

        CloseSlot();
        Reload();
    }

    private void CloseSlot()
    {
        _editor.Visible = false;
        _confirm.Visible = false;
        Relayout();
    }

    /// <summary>Собирает границы дня из набранного времени, оставляя дату прежней.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End) Rebase(
        BreakInterval interval,
        (TimeOnly Start, TimeOnly End) value)
    {
        var start = Shift(interval.Start, value.Start);
        var end = Shift(interval.End, value.End);

        // Конец, оказавшийся раньше начала по часам, относится к следующим календарным
        // суткам: рабочий день сдвинут, и вечерний перерыв через полночь — обычное дело.
        if (end <= start)
        {
            end = end.AddDays(1);
        }

        return (start, end);

        static DateTimeOffset Shift(DateTimeOffset at, TimeOnly time) =>
            new(at.Date.Add(time.ToTimeSpan()), at.Offset);
    }

    /// <summary>Следит за чужой записью в файл дня.</summary>
    private FileSystemWatcher Watch()
    {
        var watcher = new FileSystemWatcher(_service.DataRoot, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        // Событий на одну запись приходит несколько, и приходят они с чужого потока.
        // Таймер сводит их в одно перечитывание на UI-потоке.
        void Touched(object? sender, FileSystemEventArgs e)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    _settle.Stop();
                    _settle.Start();
                });
            }
        }

        watcher.Changed += Touched;
        watcher.Created += Touched;
        watcher.Deleted += Touched;
        watcher.Renamed += (_, e) => Touched(null, e);

        return watcher;
    }

    private void Relayout()
    {
        if (_page is null || _band is null)
        {
            return;
        }

        var metrics = Sizes;
        var pad = Narrow ? metrics.Space(4) : metrics.Space(5);
        var gap = metrics.Space(2);
        var wide = Width >= metrics.WideBreakpoint && !_band.IsEmpty && !_page.Unreadable;

        _numbers.Visible = wide;

        var side = wide ? metrics.Scale(320) : 0;
        var right = Content.ClientSize.Width - pad;
        var left = pad;
        var main = Math.Max(metrics.Scale(200), right - left - (wide ? side + metrics.Space(5) : 0));
        var y = pad;

        var step = metrics.StepperSize;
        var headerHeight = Math.Max(step, _header.PreferredHeight(Narrow));
        var stepTop = y + ((headerHeight - step) / 2);

        _previous.Location = new Point(left, stepTop);
        _next.Location = new Point(left + main - step, stepTop);

        _header.SetBounds(left + step + gap, y, Math.Max(0, main - (step * 2) - (gap * 2)), headerHeight);
        y += headerHeight + metrics.Space(4);

        _figures.SetBounds(left, y, main, _figures.PreferredHeight(Narrow));
        y += _figures.Height + metrics.Space(1);

        if (_notice.Visible)
        {
            _notice.SetBounds(left, y, main, metrics.Scale(64));
            y += _notice.Height + gap;
        }

        if (_timeline.Visible)
        {
            _timeline.SetBounds(left, y, main, _timeline.PreferredHeight);
            y += _timeline.Height + metrics.Space(3);

            _legend.SetBounds(left, y, main, _legend.PreferredHeight);
            y += _legend.Height + gap;
        }

        var bottom = Content.ClientSize.Height - pad;
        _reveal.FitToText();
        _reveal.Location = new Point(left, bottom - _reveal.Height);
        bottom -= _reveal.Height + metrics.Space(4);

        // Панель действий и слот правки растут снизу вверх: список отдаёт им место,
        // а не наоборот — иначе открытая правка выталкивала бы кнопки за край окна.
        foreach (var button in new[] { _toggle, _edit, _delete })
        {
            button.Visible = _list.Visible;
            button.FitToText();
        }

        if (_list.Visible)
        {
            var buttonLeft = left;
            foreach (var button in new[] { _toggle, _edit, _delete })
            {
                button.Location = new Point(buttonLeft, bottom - button.Height);
                buttonLeft += button.Width + gap;
            }

            bottom -= _toggle.Height + gap;
        }

        var slot = _editor.Visible ? _editor : _confirm.Visible ? (Control)_confirm : null;
        if (slot is not null)
        {
            var height = slot == _editor ? _editor.PreferredHeight : _confirm.PreferredHeight(main);
            slot.SetBounds(left, bottom - height, main, height);
            bottom -= height + gap;
        }

        if (_list.Visible)
        {
            _list.SetBounds(left, y, main, Math.Max(0, bottom - y));
        }

        if (wide)
        {
            var sideLeft = left + main + metrics.Space(5);

            // Высота карточки — заголовок плюс строки: карточка обнимает содержимое,
            // а не тянется на всю колонку с пустотой внизу.
            _numbers.SetBounds(sideLeft, pad, side, metrics.Scale(1));
            var header = _numbers.ContentBounds.Top;
            _numbers.SetBounds(sideLeft, pad, side, header + _numbersBody.RowsHeight + metrics.Space(4));

            // Тело кладётся под заголовок карточки, а не поверх него: заполняющая
            // стыковка накрыла бы метку секции.
            _numbersBody.Bounds = _numbers.ContentBounds;
        }
    }

    /// <summary>Открывает файл дня в проводнике. Нечем открыть — молча ничего.</summary>
    private void Reveal()
    {
        if (_page is not { } page)
        {
            return;
        }

        try
        {
            Process.Start(File.Exists(page.Path)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{page.Path}\"")
                : new ProcessStartInfo(_service.DataRoot) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or ArgumentException)
        {
            // Показывать нечего — молча пропускаем.
        }
    }

    // ---- нарисованные части окна ----------------------------------------------------

    /// <summary>Дата дня и строка под ней: приход, цель, состояние.</summary>
    private sealed class Header : DrawnPanel
    {
        private string _title = string.Empty;
        private string _note = string.Empty;
        private string _state = string.Empty;
        private bool _narrow;

        /// <summary>Две строки: дата и под ней приход с целью.</summary>
        public int PreferredHeight(bool narrow)
        {
            var fonts = Typography.Of(this);
            var title = narrow ? fonts.TitleNarrow : fonts.Title;
            return title.Height + Metrics.Of(this).Scale(2) + fonts.Note.Height;
        }

        public void Show(DaySummary day, DateOnly date, bool narrow)
        {
            _narrow = narrow;
            _title = narrow
                ? date.ToString("ddd, d MMMM", Russian)
                : Capitalize(date.ToString("dddd, d MMMM", Russian));

            var parts = new List<string>();
            if (day.ArrivedAt is { } arrived)
            {
                parts.Add("пришёл в " + WorkTimeFormat.Clock(arrived));
            }

            parts.Add(day.IsDayOff ? "нерабочий день" : "цель " + WorkTimeFormat.Duration(day.Goal));

            _note = string.Join(" · ", parts);
            _state = day.IsRunning ? "идёт сейчас" : day.LeftAt is not null ? "день закрыт" : string.Empty;
            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
        {
            var title = _narrow ? fonts.TitleNarrow : fonts.Title;
            TextRenderer.DrawText(
                graphics,
                _title,
                title,
                new Rectangle(0, 0, Width, title.Height),
                palette.Ink,
                Flags);

            var noteTop = title.Height + metrics.Scale(2);
            var width = TextRenderer.MeasureText(_note, fonts.Note, Size.Empty, TextFormatFlags.NoPadding).Width;

            TextRenderer.DrawText(
                graphics,
                _note,
                fonts.Note,
                new Rectangle(0, noteTop, Width, fonts.Note.Height),
                palette.Muted,
                Flags);

            if (_state.Length > 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    " · " + _state,
                    fonts.Note,
                    new Rectangle(width, noteTop, Math.Max(0, Width - width), fonts.Note.Height),
                    _state == "идёт сейчас" ? palette.Accent : palette.Faint,
                    Flags);
            }
        }
    }

    /// <summary>Крупный счётчик, цель и прогноз ухода.</summary>
    private sealed class Figures : DrawnPanel
    {
        private DaySummary? _day;
        private bool _narrow;

        public int PreferredHeight(bool narrow) =>
            (narrow ? Typography.Of(this).CounterNarrow : Typography.Of(this).Counter).Height
            + Metrics.Of(this).Scale(4);

        public void Show(DaySummary day, bool narrow)
        {
            _day = day;
            _narrow = narrow;
            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
        {
            if (_day is not { } day)
            {
                return;
            }

            var counter = _narrow ? fonts.CounterNarrow : fonts.Counter;
            var worked = WorkTimeFormat.Duration(day.Worked);

            TextRenderer.DrawText(graphics, worked, counter, new Point(0, 0), palette.Ink, NoPad);
            var width = TextRenderer.MeasureText(worked, counter, Size.Empty, NoPad).Width;

            if (!day.IsDayOff)
            {
                var goal = " / " + WorkTimeFormat.Duration(day.Goal);
                var small = _narrow ? fonts.NumberNarrow : fonts.Number;

                TextRenderer.DrawText(
                    graphics,
                    goal,
                    small,
                    new Point(width + metrics.Scale(2), counter.Height - small.Height - metrics.Scale(4)),
                    palette.Faint,
                    NoPad);
            }

            // Прогноз ухода — только у идущего дня: у закрытого уходить уже некуда.
            if (day.ProjectedEnd is { } projected && day.IsRunning && !day.IsDayOff)
            {
                Corner(graphics, palette, metrics, fonts, "освобожусь", WorkTimeFormat.Clock(projected), palette.Accent);
            }
            else if (day.LeftAt is { } left)
            {
                Corner(graphics, palette, metrics, fonts, "ушёл", WorkTimeFormat.Clock(left), palette.Muted);
            }
        }

        private void Corner(
            Graphics graphics,
            Palette palette,
            Metrics metrics,
            Typography fonts,
            string caption,
            string value,
            Color colour)
        {
            var big = _narrow ? fonts.ProjectionNarrow : fonts.Projection;
            var captionSize = TextRenderer.MeasureText(caption, fonts.Note, Size.Empty, NoPad);
            var valueSize = TextRenderer.MeasureText(value, big, Size.Empty, NoPad);
            var right = Width;

            TextRenderer.DrawText(
                graphics,
                caption,
                fonts.Note,
                new Point(right - captionSize.Width, 0),
                palette.Muted,
                NoPad);

            TextRenderer.DrawText(
                graphics,
                value,
                big,
                new Point(right - valueSize.Width, captionSize.Height + metrics.Scale(2)),
                colour,
                NoPad);
        }
    }

    /// <summary>Что означают цвета на полосе.</summary>
    private sealed class Legend : DrawnPanel
    {
        private readonly List<(string Text, BandKind Kind)> _items = [];

        public int PreferredHeight => Typography.Of(this).Caption.Height + Metrics.Of(this).Scale(2);

        public void Show(DayBand band)
        {
            ArgumentNullException.ThrowIfNull(band);

            _items.Clear();
            var kinds = band.Segments.Select(segment => segment.Kind).ToHashSet();

            if (kinds.Contains(BandKind.Work))
            {
                _items.Add(("работа", BandKind.Work));
            }

            if (kinds.Contains(BandKind.PaidBreak))
            {
                _items.Add(("отлучка, засчитана", BandKind.PaidBreak));
            }

            if (kinds.Contains(BandKind.UnpaidBreak))
            {
                _items.Add(("не засчитано", BandKind.UnpaidBreak));
            }

            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
        {
            var x = 0;
            var marker = metrics.LegendMarkerSize;

            foreach (var (text, kind) in _items)
            {
                Draw.Marker(
                    graphics,
                    new Rectangle(x, (Height - marker) / 2, marker, marker),
                    SegmentColour(kind, palette, palette.Window),
                    metrics);

                x += marker + metrics.Scale(6);

                var size = TextRenderer.MeasureText(text, fonts.Caption, Size.Empty, NoPad);
                TextRenderer.DrawText(
                    graphics,
                    text,
                    fonts.Caption,
                    new Point(x, (Height - size.Height) / 2),
                    palette.Muted,
                    NoPad);

                x += size.Width + metrics.Space(4);
            }
        }
    }

    /// <summary>Цифры дня в боковой карточке.</summary>
    private sealed class Numbers : DrawnPanel
    {
        private readonly List<(string Caption, string Value, Color? Colour)> _rows = [];

        /// <summary>Сколько места занимают строки — по числу тех, что есть у этого дня.</summary>
        public int RowsHeight =>
            _rows.Count * (Typography.Of(this).Body.Height + Metrics.Of(this).Scale(9));

        public void Show(DaySummary day)
        {
            ArgumentNullException.ThrowIfNull(day);

            _rows.Clear();

            if (day.ArrivedAt is { } arrived)
            {
                _rows.Add(("Приход", WorkTimeFormat.Clock(arrived), null));
            }

            if (day.LeftAt is { } left)
            {
                _rows.Add(("Уход", WorkTimeFormat.Clock(left), null));
            }

            _rows.Add(("Отлучки", WorkTimeFormat.Duration(day.Breaks), null));

            if (day.Unpaid > TimeSpan.Zero)
            {
                _rows.Add(("Не засчитано", WorkTimeFormat.Duration(day.Unpaid), Palette.Current().Unpaid));
            }

            if (!day.IsDayOff)
            {
                _rows.Add(day.GoalReached
                    ? ("Сверх нормы", WorkTimeFormat.SignedDuration(day.Balance), Palette.Current().Goal)
                    : ("Осталось до цели", WorkTimeFormat.Duration(day.Remaining), Palette.Current().Accent));
            }

            Invalidate();
        }

        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
        {
            var y = 0;
            var lineHeight = fonts.Body.Height + metrics.Scale(9);

            foreach (var (caption, value, colour) in _rows)
            {
                TextRenderer.DrawText(
                    graphics,
                    caption,
                    fonts.Note,
                    new Rectangle(0, y, Width, fonts.Body.Height),
                    palette.Muted,
                    Flags);

                TextRenderer.DrawText(
                    graphics,
                    value,
                    fonts.Number,
                    new Rectangle(0, y, Width, fonts.Body.Height),
                    colour ?? palette.Ink,
                    Flags | TextFormatFlags.Right);

                y += lineHeight;
            }
        }
    }

    /// <summary>Сообщение вместо содержимого: пустой день или неразобравшийся файл.</summary>
    private sealed class Notice : DrawnPanel
    {
        protected override void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts)
        {
            Draw.Surface(
                graphics,
                new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)),
                new Face(palette.Card, palette.Line, palette.Ink),
                metrics.RadiusCard,
                Draw.LineWidth(metrics));

            var inset = metrics.Space(4);
            TextRenderer.DrawText(
                graphics,
                Text,
                fonts.Body,
                new Rectangle(inset, inset, Math.Max(0, Width - (inset * 2)), Math.Max(0, Height - (inset * 2))),
                palette.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Отрезки дня строками: и работа, и отлучки — как на полосе.</summary>
    private sealed class SegmentList : DesignList
    {
        private readonly List<BandSegment> _segments = [];
        private DaySummary? _day;
        private DateTimeOffset? _openEnd;
        private IReadOnlySet<DateTimeOffset> _edited = new HashSet<DateTimeOffset>();

        /// <summary>Выбранный отрезок. Работу править нечем, отлучку — есть чем.</summary>
        public BandSegment? SelectedSegment =>
            SelectedIndex >= 0 && SelectedIndex < _segments.Count ? _segments[SelectedIndex] : null;

        /// <summary>Выбранная отлучка. У работы своих отметок в журнале нет.</summary>
        public BreakInterval? SelectedBreak =>
            SelectedSegment is { Kind: not BandKind.Work } segment ? Interval(segment) : null;

        /// <summary>Возвращает выбор на отлучку с таким началом — после перечитывания дня.</summary>
        public void SelectBreakAt(DateTimeOffset? start)
        {
            if (start is not { } at)
            {
                return;
            }

            var index = _segments.FindIndex(segment => segment.Start == at);
            if (index >= 0)
            {
                SelectedIndex = index;
            }
        }

        public void Show(DayBand band, DaySummary day, IReadOnlySet<DateTimeOffset> edited)
        {
            ArgumentNullException.ThrowIfNull(band);
            ArgumentNullException.ThrowIfNull(day);
            ArgumentNullException.ThrowIfNull(edited);

            _segments.Clear();
            _segments.AddRange(band.Segments);
            _day = day;
            _edited = edited;

            // У идущего дня последний отрезок не кончился: подписывать его временем конца
            // было бы обещанием, что человек уже ушёл.
            _openEnd = day.LeftAt is null && day.IsRunning && _segments.Count > 0
                ? _segments[^1].End
                : null;

            Count = _segments.Count;
        }

        protected override void PaintRow(
            Graphics graphics,
            int index,
            Rectangle bounds,
            Palette palette,
            RowState state)
        {
            var segment = _segments[index];
            var metrics = Sizes;
            var fonts = Fonts;
            var narrow = Width < metrics.Scale(560);

            var marker = metrics.MarkerSize;
            Draw.Marker(
                graphics,
                new Rectangle(bounds.Left, bounds.Top + ((bounds.Height - marker) / 2), marker, marker),
                SegmentColour(segment.Kind, palette, state.Selected ? palette.Selection : palette.Window),
                metrics);

            var left = bounds.Left + marker + metrics.Space(2);
            var number = narrow ? fonts.NumberNarrow : fonts.Number;

            var end = _openEnd == segment.End ? "сейчас" : WorkTimeFormat.Clock(segment.End);
            var when = WorkTimeFormat.Clock(segment.Start) + (narrow ? "→" : " → ") + end;
            var whenWidth = metrics.Scale(narrow ? 96 : 118);

            TextRenderer.DrawText(
                graphics,
                when,
                number,
                new Rectangle(left, bounds.Top, whenWidth, bounds.Height),
                RowMuted(palette, state),
                Middle);

            left += whenWidth + metrics.Space(2);

            var title = Title(segment);
            var body = narrow ? fonts.Note : fonts.Body;
            var titleWidth = TextRenderer.MeasureText(title, body, Size.Empty, NoPad).Width;

            TextRenderer.DrawText(
                graphics,
                title,
                body,
                new Rectangle(left, bounds.Top, Math.Max(0, bounds.Right - left), bounds.Height),
                RowInk(palette, state),
                Middle);

            if (Badge(segment) is { } badge)
            {
                Chip(graphics, badge, left + titleWidth + metrics.Space(2), bounds, palette, metrics, fonts, state);
            }

            TextRenderer.DrawText(
                graphics,
                WorkTimeFormat.Duration(segment.Duration),
                number,
                new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
                RowMuted(palette, state),
                Middle | TextFormatFlags.Right);
        }

        private static void Chip(
            Graphics graphics,
            (string Text, bool Manual) badge,
            int x,
            Rectangle row,
            Palette palette,
            Metrics metrics,
            Typography fonts,
            RowState state)
        {
            var width = fonts.MeasureLabel(badge.Text) + (metrics.Scale(6) * 2);
            var height = fonts.Label.Height + (metrics.Scale(2) * 2);
            var box = new Rectangle(x, row.Top + ((row.Height - height) / 2), width, height);

            if (box.Right > row.Right)
            {
                return;
            }

            var face = badge.Manual
                ? palette.ManualBadge
                : new Face(
                    Color.Transparent,
                    state.Selected ? palette.SelectionLine : palette.Line,
                    RowMuted(palette, state));

            Draw.Surface(graphics, box, face, metrics.Scale(4), Draw.LineWidth(metrics));
            fonts.DrawLabel(graphics, badge.Text, new Point(box.Left + metrics.Scale(6), box.Top + metrics.Scale(2)), face.Ink);
        }

        private string Title(BandSegment segment) => segment.Kind switch
        {
            BandKind.Work => "Работа",
            BandKind.UnpaidBreak when IsLunch(segment) => "Обед",
            _ => "Отлучка",
        };

        /// <summary>Бейдж справа от названия: засчитана, не засчитана, правлено вручную.</summary>
        private (string Text, bool Manual)? Badge(BandSegment segment)
        {
            if (segment.Kind == BandKind.Work)
            {
                return null;
            }

            // «Правлено вручную» — про две разные вещи сразу: сдвинутые отметки журнала
            // и снятую вручную пометку зачёта. Обе означают одно: это решил человек.
            if (_edited.Contains(segment.Start) || IsManual(segment))
            {
                return ("правлено вручную", true);
            }

            return segment.Kind == BandKind.UnpaidBreak
                ? ("не засчитано", false)
                : ("засчитана", false);
        }

        private bool IsLunch(BandSegment segment) =>
            Interval(segment) is { Guessed: true };

        /// <summary>
        /// Отлучку пометил человек, а не правило.
        /// </summary>
        /// <remarks>
        /// При выключенном зачёте коротких отлучек вне зачёта оказываются все подряд — это
        /// правило дня, а не чья-то правка, и бейджа «правлено вручную» такая отлучка не носит.
        /// </remarks>
        private bool IsManual(BandSegment segment) =>
            _day is { CountsShortBreaks: true }
            && Interval(segment) is { IsUnpaid: true, Guessed: false };

        private BreakInterval? Interval(BandSegment segment) =>
            _day?.Intervals.FirstOrDefault(interval => interval.Start == segment.Start);
    }

    // ---- мелочи --------------------------------------------------------------------

    private const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
    private const TextFormatFlags NoPad = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    private const TextFormatFlags Middle =
        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    /// <summary>
    /// Цвет отрезка. Засчитанная отлучка — акцент при 0.42, и смешивать его надо с тем
    /// фоном, на котором он лежит: на полосе это жёлоб, в строке — карточка, в легенде —
    /// окно. Смешанный не с тем фоном, он сливается с «не засчитано».
    /// </summary>
    private static Color SegmentColour(BandKind kind, Palette palette, Color under) => kind switch
    {
        BandKind.Work => palette.Accent,
        BandKind.PaidBreak => Palette.Blend(palette.Accent, under, 0.42),
        _ => palette.Unpaid,
    };

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Russian) + text[1..];

    /// <summary>
    /// Подтверждение удаления.
    /// </summary>
    /// <remarks>
    /// Внутри окна, а не системным диалогом: системное окно посреди целиком нарисованного
    /// приложения читается как чужое. Дизайн (1h) показывает подтверждение ровно так же —
    /// карточкой с красной рамкой на месте содержимого.
    /// </remarks>
    private sealed class Confirmation : Panel, IPaletteAware
    {
        private readonly DesignButton _yes;
        private readonly DesignButton _no;

        private string _question = string.Empty;

        public Confirmation()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw,
                true);

            _yes = new DesignButton { Kind = ButtonKind.DangerFilled, Text = "Удалить" };
            _yes.Click += (_, _) => Accepted?.Invoke(this, EventArgs.Empty);

            _no = new DesignButton { Text = "Отмена" };
            _no.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);

            Controls.AddRange([_yes, _no]);
        }

        public event EventHandler? Accepted;

        public event EventHandler? Cancelled;

        public void Ask(string question)
        {
            _question = question;
            PerformLayout();
            Invalidate();
            _no.Focus();
        }

        public int PreferredHeight(int width)
        {
            var metrics = Metrics.Of(this);
            var fonts = Typography.Of(metrics);
            var inset = metrics.Space(4);

            var text = TextRenderer.MeasureText(
                _question,
                fonts.Body,
                new Size(Math.Max(1, width - (inset * 2)), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            return inset + text.Height + metrics.Space(3) + metrics.ControlHeight + inset;
        }

        public void RefreshPalette()
        {
            BackColor = Palette.Current().Card;
            Invalidate(invalidateChildren: true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshPalette();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            var metrics = Metrics.Of(this);
            var inset = metrics.Space(4);

            _yes.FitToText();
            _no.FitToText();

            var top = Height - inset - metrics.ControlHeight;
            _yes.Location = new Point(inset, top);
            _no.Location = new Point(_yes.Right + metrics.Space(2), top);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            var palette = Palette.Current();
            var metrics = Metrics.Of(this);
            var fonts = Typography.Of(metrics);

            using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
            {
                e.Graphics.FillRectangle(back, ClientRectangle);
            }

            Draw.Surface(
                e.Graphics,
                new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)),
                new Face(palette.Card, palette.Danger, palette.Ink),
                metrics.RadiusCard,
                Draw.LineWidth(metrics));

            var inset = metrics.Space(4);
            TextRenderer.DrawText(
                e.Graphics,
                _question,
                fonts.Body,
                new Rectangle(inset, inset, Math.Max(0, Width - (inset * 2)), Math.Max(0, Height - (inset * 2))),
                palette.Ink,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Полотно, которое рисует себя само и берёт фон у родителя.</summary>
    private abstract class DrawnPanel : Control
    {
        protected DrawnPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw,
                true);

            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            var palette = Palette.Current();
            using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
            {
                e.Graphics.FillRectangle(back, ClientRectangle);
            }

            var metrics = Metrics.Of(this);
            Render(e.Graphics, palette, metrics, Typography.Of(metrics));
        }

        protected abstract void Render(Graphics graphics, Palette palette, Metrics metrics, Typography fonts);
    }
}
