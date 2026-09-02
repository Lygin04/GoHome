using System.ComponentModel;

namespace GoHome.Ui.Design;

/// <summary>Что сейчас со строкой списка.</summary>
/// <param name="Hovered">Курсор над строкой.</param>
/// <param name="Selected">Строка выбрана.</param>
/// <param name="Focused">Строка выбрана и список держит фокус ввода.</param>
internal readonly record struct RowState(bool Hovered, bool Selected, bool Focused);

/// <summary>
/// Список строк по 38: подложка, выделение, наведение, фокус и клавиатура.
/// </summary>
/// <remarks>
/// Не <see cref="ListView"/>: он не принимает эту палитру — шапка в тёмной теме остаётся
/// светлой, выделение системного цвета, а собственная отрисовка строк всё равно требуется.
/// <para>
/// Содержимое строки список не рисует: что именно стоит в колонках, знает экран, а не
/// примитив. Здесь только то, что одинаково везде, — иначе пришлось бы выдумывать модель
/// колонок и переделывать её на каждом новом экране.
/// </para>
/// <para>
/// Высота строки держится на 38 при любом размере окна. Дизайн настаивает: ниже мышь
/// начинает промахиваться.
/// </para>
/// </remarks>
internal abstract class DesignList : DesignControl
{
    /// <summary>За сколько строк прокручивается одно движение колеса.</summary>
    private const int WheelRows = 3;

    private int _count;
    private int _hover = -1;
    private int _offset;

    /// <summary>Выбранная строка изменилась.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Строку открыли: двойным щелчком или Enter.</summary>
    public event EventHandler? RowActivated;

    /// <summary>Сколько строк в списке.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Count
    {
        get => _count;
        set
        {
            _count = Math.Max(0, value);
            SelectedIndex = _count == 0 ? -1 : Math.Min(SelectedIndex, _count - 1);
            ClampOffset();
            Invalidate();
        }
    }

    /// <summary>Номер выбранной строки. Минус один — не выбрано ничего.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get;
        set
        {
            var clamped = _count == 0 ? -1 : Math.Clamp(value, -1, _count - 1);
            if (field != clamped)
            {
                field = clamped;
                ScrollIntoView(clamped);
                Invalidate();
            }
        }
    } = -1;

    /// <summary>Рисовать ли разделители между строками.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Separators
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Invalidate();
            }
        }
    } = true;

    /// <summary>Высота одной строки. Из дизайна и не меняется.</summary>
    protected int RowHeight => Sizes.RowHeight;

    /// <summary>Отступ содержимого строки от края списка.</summary>
    protected int RowPadding => Sizes.Space(3);

    /// <summary>Весь список помещается целиком, прокручивать нечего.</summary>
    protected bool FitsWhole => _count * RowHeight <= Height;

    /// <summary>Цвет обычного текста строки: на выделенной он другой.</summary>
    protected static Color RowInk(Palette palette, RowState state) =>
        state.Selected ? palette.SelectionInk : palette.Ink;

    /// <summary>Цвет тихого текста строки — чисел и времени.</summary>
    protected static Color RowMuted(Palette palette, RowState state) =>
        state.Selected ? palette.SelectionInk : palette.Muted;

    /// <summary>Рисует содержимое одной строки. Подложку и разделитель список рисует сам.</summary>
    /// <param name="graphics">Куда рисовать.</param>
    /// <param name="index">Номер строки.</param>
    /// <param name="bounds">Место под содержимое — уже с отступами.</param>
    /// <param name="palette">Палитра, действующая сейчас.</param>
    /// <param name="state">Наведение, выделение и фокус.</param>
    protected abstract void PaintRow(
        Graphics graphics,
        int index,
        Rectangle bounds,
        Palette palette,
        RowState state);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Colors;
        var metrics = Sizes;
        var row = RowHeight;
        var padding = RowPadding;

        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        var first = Math.Max(0, _offset / row);
        var last = Math.Min(_count - 1, (_offset + Height) / row);

        for (var index = first; index <= last; index++)
        {
            var bounds = new Rectangle(0, (index * row) - _offset, Width, row);
            var state = new RowState(
                Hovered: index == _hover && Enabled,
                Selected: index == SelectedIndex,
                Focused: index == SelectedIndex && ShowsFocus);

            if (state.Selected)
            {
                Draw.Surface(
                    e.Graphics,
                    bounds,
                    new Face(palette.Selection, Color.Transparent, palette.SelectionInk),
                    metrics.RadiusControl,
                    Draw.LineWidth(metrics));
            }
            else if (state.Hovered)
            {
                Draw.Surface(
                    e.Graphics,
                    bounds,
                    new Face(palette.Field, Color.Transparent, palette.Ink),
                    metrics.RadiusControl,
                    Draw.LineWidth(metrics));
            }

            if (Separators && index < _count - 1 && !state.Selected)
            {
                Draw.Separator(e.Graphics, padding, Width - padding, bounds.Bottom - 1, palette.LineSoft, metrics);
            }

            PaintRow(
                e.Graphics,
                index,
                Rectangle.FromLTRB(padding, bounds.Top, Math.Max(padding, Width - padding), bounds.Bottom),
                palette,
                state);

            if (state.Focused)
            {
                // Внутрь: наружный контур перекрыла бы соседняя строка.
                Draw.Focus(e.Graphics, bounds, palette.Accent, metrics, inside: true);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ArgumentNullException.ThrowIfNull(e);

        var index = IndexAt(e.Location);
        if (_hover != index)
        {
            _hover = index;
            Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hover != -1)
        {
            _hover = -1;
            Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button == MouseButtons.Left && IndexAt(e.Location) is var index and >= 0)
        {
            Choose(index);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button == MouseButtons.Left && IndexAt(e.Location) >= 0)
        {
            RowActivated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ArgumentNullException.ThrowIfNull(e);

        if (!FitsWhole)
        {
            _offset -= Math.Sign(e.Delta) * RowHeight * WheelRows;
            ClampOffset();
            Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        switch (e.KeyCode)
        {
            case Keys.Down:
                e.Handled = true;
                Choose(SelectedIndex + 1);
                return;

            case Keys.Up:
                e.Handled = true;
                Choose(SelectedIndex <= 0 ? 0 : SelectedIndex - 1);
                return;

            case Keys.Home:
                e.Handled = true;
                Choose(0);
                return;

            case Keys.End:
                e.Handled = true;
                Choose(_count - 1);
                return;

            case Keys.PageDown:
                e.Handled = true;
                Choose(SelectedIndex + Math.Max(1, Height / RowHeight));
                return;

            case Keys.PageUp:
                e.Handled = true;
                Choose(SelectedIndex - Math.Max(1, Height / RowHeight));
                return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc/>
    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
        || base.IsInputKey(keyData);

    /// <inheritdoc/>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ClampOffset();
    }

    /// <summary>Enter на выбранной строке открывает её.</summary>
    protected override void Activate()
    {
        if (SelectedIndex >= 0)
        {
            RowActivated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Номер строки под точкой. Минус один — мимо всех.</summary>
    private int IndexAt(Point point)
    {
        if (_count == 0 || point.Y < 0)
        {
            return -1;
        }

        var index = (point.Y + _offset) / RowHeight;
        return index >= 0 && index < _count ? index : -1;
    }

    private void Choose(int index)
    {
        if (_count == 0)
        {
            return;
        }

        var clamped = Math.Clamp(index, 0, _count - 1);
        if (clamped == SelectedIndex)
        {
            return;
        }

        SelectedIndex = clamped;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScrollIntoView(int index)
    {
        if (index < 0)
        {
            return;
        }

        var top = index * RowHeight;
        var bottom = top + RowHeight;

        if (top < _offset)
        {
            _offset = top;
        }
        else if (bottom > _offset + Height)
        {
            _offset = bottom - Height;
        }

        ClampOffset();
    }

    private void ClampOffset() =>
        _offset = Math.Max(0, Math.Min(_offset, Math.Max(0, (_count * RowHeight) - Height)));
}
