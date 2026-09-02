using GoHome.Core;
using GoHome.Ui.Design;

namespace GoHome.Ui;

/// <summary>
/// Исключение по дате: отпуск, праздник, предпраздничный день.
/// </summary>
/// <remarks>
/// Открывается и из настроек, и из окна дня — задним числом такое понадобится чаще,
/// чем заранее.
/// <para>
/// Дата набирается, а не выбирается в календаре: поле выбора даты перерисовать нельзя
/// вовсе, а полноценный календарь ради одной даты в году — несоразмерная работа. Набрать
/// «31.12.2026» быстрее, чем долистать до декабря.
/// </para>
/// </remarks>
internal sealed class DayExceptionDialog : DesignForm
{
    private readonly SettingsSection _section;
    private readonly DesignField _date;
    private readonly DesignSwitch _dayOff;
    private readonly DesignField _hours;
    private readonly DesignField _note;
    private readonly DesignButton _save;
    private readonly Hint _hint;

    /// <param name="date">Какая дата правится.</param>
    /// <param name="existing">Уже заданное исключение, если оно есть.</param>
    /// <param name="fromSchedule">Что говорит недельный график — показать как отправную точку.</param>
    /// <param name="lockDate">Дата задана снаружи и менять её здесь нельзя.</param>
    public DayExceptionDialog(DateOnly date, DateException? existing, TimeSpan? fromSchedule, bool lockDate)
    {
        SetTitle("GoHome — день по особому расписанию", "День по особому расписанию");
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        SetMinimum(new Size(480, 430));
        SetInitialSize(new Size(520, 450));

        _date = new DesignField { Kind = FieldKind.Date, Date = date, Enabled = !lockDate };
        _date.Width = Sizes.Scale(120);

        _dayOff = new DesignSwitch { Checked = existing?.IsDayOff ?? fromSchedule is null };
        _dayOff.CheckedChanged += (_, _) => UpdateEnabled();

        _hours = new DesignField
        {
            Kind = FieldKind.Duration,
            Duration = existing?.Hours ?? fromSchedule ?? WorkTimeCalculator.DefaultGoal,
        };

        _note = new DesignField { Kind = FieldKind.Free, Text = existing?.Note ?? string.Empty };
        _note.Width = Sizes.Scale(200);

        _date.ValueChanged += (_, _) => Revalidate();
        _hours.ValueChanged += (_, _) => Revalidate();

        _section = new SettingsSection();
        _section.Add("Дата", "День, для которого действует исключение", _date);
        _section.Add("Нерабочий день", "Цели у него нет, и уведомление о норме не приходит", _dayOff);
        _section.Add("Продолжительность", "Сколько часов считать нормой в этот день", _hours);
        _section.Add("Пометка", "Для себя: зачем он особый", _note);

        _hint = new Hint();

        _save = new DesignButton { Kind = ButtonKind.Primary, Text = "Сохранить" };
        _save.Click += (_, _) => Accept();

        var cancel = new DesignButton { Text = "Отмена" };
        cancel.Click += (_, _) => Close();

        Content.Controls.AddRange([_section, _hint, _save, cancel]);
        Cancel = cancel;

        UpdateEnabled();
    }

    /// <summary>Что получилось. Читать после <see cref="DialogResult.OK"/>.</summary>
    public DateException Result => new()
    {
        Date = _date.Date ?? DateOnly.FromDateTime(DateTime.Today),
        Hours = _dayOff.Checked ? null : _hours.Duration,
        Note = string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim(),
    };

    private DesignButton? Cancel { get; init; }

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

    /// <inheritdoc/>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    private void Relayout()
    {
        if (_section is null)
        {
            return;
        }

        var metrics = Sizes;
        var pad = metrics.Space(5);
        var width = Math.Max(metrics.Scale(200), Content.ClientSize.Width - (pad * 2));

        _section.SetBounds(pad, pad, width, _section.PreferredHeight);

        var bottom = Content.ClientSize.Height - pad;
        _save.FitToText();
        Cancel!.FitToText();

        Cancel.Location = new Point(pad + width - Cancel.Width, bottom - Cancel.Height);
        _save.Location = new Point(Cancel.Left - _save.Width - metrics.Space(2), bottom - _save.Height);

        _hint.SetBounds(pad, _section.Bottom + metrics.Space(3), width, metrics.Scale(44));
    }

    private void UpdateEnabled()
    {
        _hours.Enabled = !_dayOff.Checked;

        _hint.Show(_dayOff.Checked
            ? "Время в такой день считается и показывается, но уведомление о норме не приходит, "
                + "а отработанное идёт в недельный баланс со знаком плюс."
            : "Исключение сильнее недельного графика.");

        Revalidate();
        Relayout();
    }

    private void Revalidate()
    {
        var ready = _date.Date is not null && (_dayOff.Checked || _hours.Duration > TimeSpan.Zero);

        _date.Rejected = _date.Date is null;
        _hours.Rejected = !_dayOff.Checked && _hours.Duration is null or { Ticks: 0 };
        _save.Enabled = ready;
    }

    private void Accept()
    {
        if (_save.Enabled)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Пояснение под строками: что означает выбранное сочетание.</summary>
    private sealed class Hint : DrawnPanel
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
                fonts.Caption,
                ClientRectangle,
                palette.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
