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

    private DateOnly _date;
    private DayPage? _page;
    private DayBand? _band;

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
            _reveal,
        ]);

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

        _page = page;
        _band = band;

        Text = "День — " + _date.ToString("d MMMM", Russian);

        _header.Show(page.Summary, _date, Narrow);
        _figures.Show(page.Summary, Narrow);
        _timeline.Narrow = Narrow;
        _timeline.Show(band);
        _legend.Show(band);
        _numbersBody.Show(page.Summary);
        _list.Show(band, page.Summary);

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

        if (_list.Visible)
        {
            _list.SetBounds(left, y, main, Math.Max(0, bottom - _reveal.Height - gap - y));
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

        /// <summary>Выбранный отрезок. Работу править нечем, отлучку — есть чем.</summary>
        public BandSegment? SelectedSegment =>
            SelectedIndex >= 0 && SelectedIndex < _segments.Count ? _segments[SelectedIndex] : null;

        public void Show(DayBand band, DaySummary day)
        {
            ArgumentNullException.ThrowIfNull(band);
            ArgumentNullException.ThrowIfNull(day);

            _segments.Clear();
            _segments.AddRange(band.Segments);
            _day = day;

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

            if (IsManual(segment))
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
