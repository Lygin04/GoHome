namespace GoHome.Ui.Design;

/// <summary>
/// Общее для всего, что приложение рисует само: двойная буферизация, состояние под курсором,
/// работа с клавиатуры и видимый фокус.
/// </summary>
/// <remarks>
/// Нарисованный вручную элемент по умолчанию не умеет ни клавиатурного обхода, ни видимого
/// фокуса: и то и другое системный контрол получает даром, а свой — только если написать.
/// Поэтому это база, а не набор одинаковых кусков в каждом контроле.
/// <para>
/// Двойная буферизация обязательна везде, где рисуем сами, — иначе всё мерцает при изменении
/// размера окна.
/// </para>
/// </remarks>
internal abstract class DesignControl : Control
{
    private bool _hovered;
    private bool _pressed;

    protected DesignControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable,
            true);

        TabStop = true;
    }

    /// <summary>Палитра, действующая прямо сейчас.</summary>
    protected static Palette Colors => Palette.Current();

    /// <summary>Размеры под текущий масштаб экрана.</summary>
    protected Metrics Sizes => Metrics.Of(this);

    /// <summary>Шрифты под текущий масштаб экрана.</summary>
    protected Typography Fonts => Typography.Of(Sizes);

    /// <summary>Состояние элемента: покой, под курсором, нажат, недоступен.</summary>
    protected Mood CurrentMood => !Enabled
        ? Mood.Disabled
        : _pressed && _hovered ? Mood.Pressed
        : _hovered ? Mood.Hover
        : Mood.Rest;

    /// <summary>
    /// Показывать контур фокуса.
    /// </summary>
    /// <remarks>
    /// Не просто «есть фокус»: Windows прячет контур, пока человек работает мышью, и
    /// показывает, как только он берётся за клавиатуру. Это решение системы, а не наше —
    /// <see cref="Control.ShowFocusCues"/> уже знает ответ. Рисовать контур всё равно
    /// приходится самому: нарисованный вручную элемент этого не умеет.
    /// </remarks>
    protected bool ShowsFocus => Focused && ShowFocusCues;

    /// <inheritdoc/>
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Restate(ref _hovered, true);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Restate(ref _hovered, false);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button == MouseButtons.Left)
        {
            // Щелчок по нарисованному элементу должен ещё и переводить на него фокус:
            // системный контрол делает это сам, свой — нет.
            Focus();
            Restate(ref _pressed, true);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        ArgumentNullException.ThrowIfNull(e);

        var wasPressed = _pressed;
        Restate(ref _pressed, false);

        // Уведённый за пределы курсор отменяет нажатие — как у любой кнопки Windows.
        if (Enabled && e.Button == MouseButtons.Left && wasPressed && ClientRectangle.Contains(e.Location))
        {
            Activate();
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        ArgumentNullException.ThrowIfNull(e);

        // Проверка доступности здесь не лишняя, хотя очередь сообщений в недоступный
        // контрол клавиш и не носит: элемент могут выключить, пока клавиша нажата,
        // и тогда отпускание пришло бы уже в выключенный.
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            Activate();
        }
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _pressed = false;
        Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Rescale();
        Invalidate();
    }

    /// <summary>Пробел или Enter на элементе, либо доведённый до конца щелчок.</summary>
    protected virtual void Activate() => OnClick(EventArgs.Empty);

    /// <summary>Пересчитать собственные размеры под новый масштаб экрана.</summary>
    protected virtual void Rescale()
    {
    }

    private void Restate(ref bool flag, bool value)
    {
        if (flag != value)
        {
            flag = value;
            Invalidate();
        }
    }
}
