using System.ComponentModel;

namespace GoHome.Ui.Design;

/// <summary>
/// Сегментированные вкладки: «Неделя · Месяц · Год».
/// </summary>
/// <remarks>
/// Не <see cref="TabControl"/>: его закладки рисует система, и в тёмной теме они остаются
/// светлыми. Да и по смыслу это не вкладки со страницами, а переключатель одного значения —
/// содержимое под ним одно и то же, меняется только период.
/// <para>
/// Стрелками влево-вправо переключается прямо с клавиатуры, не выходя из контрола, — как
/// в любой группе переключателей Windows.
/// </para>
/// </remarks>
internal sealed class SegmentedTabs : DesignControl
{
    private const int Gutter = 3;
    private const int ItemPaddingX = 16;
    private const int ItemPaddingY = 6;

    private string[] _items = [];

    /// <summary>Выбранный сегмент изменил человек.</summary>
    public event EventHandler? SelectedChanged;

    /// <summary>Надписи сегментов слева направо.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Items
    {
        get => _items;
        set
        {
            _items = [.. value ?? []];
            Selected = Math.Clamp(Selected, 0, Math.Max(0, _items.Length - 1));
            FitToItems();
            Invalidate();
        }
    }

    /// <summary>Номер выбранного сегмента.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get;
        set
        {
            var clamped = _items.Length == 0 ? 0 : Math.Clamp(value, 0, _items.Length - 1);
            if (field != clamped)
            {
                field = clamped;
                Invalidate();
            }
        }
    }

    /// <inheritdoc/>
    public override Size GetPreferredSize(Size proposed)
    {
        var metrics = Sizes;
        var padding = metrics.Scale(Gutter);
        var width = _items.Sum(ItemWidth) + (padding * 2);
        var height = metrics.ControlHeight + (padding * 2) - metrics.Scale(ItemPaddingY);

        return new Size(width, Math.Max(height, metrics.ControlHeight));
    }

    /// <summary>Подгоняет размер под надписи сегментов.</summary>
    public void FitToItems() => Size = GetPreferredSize(Size.Empty);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var palette = Colors;
        var metrics = Sizes;
        var fonts = Fonts;

        using (var back = new SolidBrush(Parent?.BackColor ?? palette.Window))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        Draw.Surface(
            e.Graphics,
            ClientRectangle,
            new Face(palette.Well, Color.Transparent, palette.Ink),
            metrics.RadiusControl,
            Draw.LineWidth(metrics));

        foreach (var (index, bounds) in Segments())
        {
            var active = index == Selected;

            if (active)
            {
                // Активный сегмент в дизайне отделён тенью. Тени в GDI+ нет, и её роль здесь
                // берёт на себя разница фонов: карточка на утопленном жёлобе.
                Draw.Surface(
                    e.Graphics,
                    bounds,
                    new Face(palette.Card, Color.Transparent, palette.Ink),
                    metrics.RadiusControl - metrics.Scale(1),
                    Draw.LineWidth(metrics));
            }

            TextRenderer.DrawText(
                e.Graphics,
                _items[index],
                fonts.Control,
                bounds,
                Enabled ? (active ? palette.Ink : palette.Muted) : palette.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (active && ShowsFocus)
            {
                Draw.Focus(e.Graphics, bounds, palette.Accent, metrics, inside: true);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        foreach (var (index, bounds) in Segments())
        {
            if (bounds.Contains(e.Location))
            {
                Choose(index);
                break;
            }
        }

        base.OnMouseUp(e);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                e.Handled = true;
                Choose(Selected - 1);
                return;

            case Keys.Right or Keys.Down:
                e.Handled = true;
                Choose(Selected + 1);
                return;

            case Keys.Home:
                e.Handled = true;
                Choose(0);
                return;

            case Keys.End:
                e.Handled = true;
                Choose(_items.Length - 1);
                return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc/>
    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End
        || base.IsInputKey(keyData);

    /// <inheritdoc/>
    protected override void Rescale() => FitToItems();

    /// <summary>Пробел на выбранном сегменте ничего не меняет — он уже выбран.</summary>
    protected override void Activate()
    {
    }

    private void Choose(int index)
    {
        if (_items.Length == 0)
        {
            return;
        }

        var clamped = Math.Clamp(index, 0, _items.Length - 1);
        if (clamped == Selected)
        {
            return;
        }

        Selected = clamped;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    private int ItemWidth(string text) =>
        TextRenderer.MeasureText(text, Fonts.Control, Size.Empty, TextFormatFlags.NoPadding).Width
        + (Sizes.Scale(ItemPaddingX) * 2);

    private IEnumerable<(int Index, Rectangle Bounds)> Segments()
    {
        var padding = Sizes.Scale(Gutter);
        var left = padding;

        for (var index = 0; index < _items.Length; index++)
        {
            var width = ItemWidth(_items[index]);
            yield return (index, new Rectangle(left, padding, width, Math.Max(0, Height - (padding * 2))));
            left += width;
        }
    }
}
