using GoHome.Core;

namespace GoHome.Ui;

/// <summary>
/// Исключение по дате: отпуск, праздник, предпраздничный день. Открывается и из настроек,
/// и из окна истории — задним числом такое понадобится чаще, чем заранее.
/// </summary>
internal sealed class DayExceptionDialog : Form
{
    private readonly DateTimePicker _date;
    private readonly CheckBox _dayOff;
    private readonly DurationBox _hours;
    private readonly TextBox _note;
    private readonly Label _hint;

    /// <param name="date">Какая дата правится.</param>
    /// <param name="existing">Уже заданное исключение, если оно есть.</param>
    /// <param name="fromSchedule">Что говорит недельный график — показать как отправную точку.</param>
    /// <param name="lockDate">Дата задана снаружи и менять её здесь нельзя.</param>
    public DayExceptionDialog(DateOnly date, DateException? existing, TimeSpan? fromSchedule, bool lockDate)
    {
        Text = "GoHome — день по особому расписанию";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(420, 210);
        Icon = AppIcon.ForWindows;
        ShowIcon = Icon is not null;

        _date = new DateTimePicker
        {
            Format = DateTimePickerFormat.Long,
            Value = date.ToDateTime(TimeOnly.MinValue),
            Enabled = !lockDate,
            Width = 250,
        };

        _dayOff = new CheckBox
        {
            Text = "Нерабочий день",
            AutoSize = true,
            Checked = existing?.IsDayOff ?? fromSchedule is null,
        };

        _hours = new DurationBox
        {
            Value = existing?.Hours ?? fromSchedule ?? WorkTimeCalculator.DefaultGoal,
        };

        _note = new TextBox { Width = 250, Text = existing?.Note ?? string.Empty };

        _hint = new Label
        {
            AutoSize = false,
            Width = 380,
            Height = 32,
            ForeColor = SystemColors.GrayText,
        };

        _dayOff.CheckedChanged += (_, _) => UpdateEnabled();

        var ok = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, Width = 100 };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 100 };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Дата", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_date, 1, 0);
        layout.Controls.Add(new Label(), 0, 1);
        layout.Controls.Add(_dayOff, 1, 1);
        layout.Controls.Add(new Label { Text = "Продолжительность", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_hours, 1, 2);
        layout.Controls.Add(new Label { Text = "Пометка", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_note, 1, 3);
        layout.Controls.Add(_hint, 1, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(12, 6, 12, 6),
        };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(layout);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;

        UpdateEnabled();
    }

    /// <summary>Что получилось. Читать после <see cref="DialogResult.OK"/>.</summary>
    public DateException Result => new()
    {
        Date = DateOnly.FromDateTime(_date.Value.Date),
        Hours = _dayOff.Checked ? null : _hours.Value,
        Note = string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim(),
    };

    private void UpdateEnabled()
    {
        _hours.Enabled = !_dayOff.Checked;
        _hint.Text = _dayOff.Checked
            ? "Время в такой день считается и показывается, но уведомление о норме не приходит, "
                + "а отработанное идёт в недельный баланс со знаком плюс."
            : "Исключение сильнее недельного графика.";
    }
}