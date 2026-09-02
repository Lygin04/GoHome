using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using GoHome.Interop;

namespace GoHome.Ui.Design;

/// <summary>
/// Окно с заголовком, нарисованным приложением.
/// </summary>
/// <remarks>
/// Системная рамка при этом не снимается: <see cref="Form.FormBorderStyle"/> остаётся
/// обычным, и стили окна тоже. Всё, что Windows делает бесплатно, продолжает работать —
/// тень, скругление углов, прилипание к краям, сочетания с клавишей Windows, разворот
/// двойным щелчком, системное меню по Alt+Пробел и по правому щелчку. Забираются ровно
/// две вещи: клиентская область растягивается на всё окно (<c>WM_NCCALCSIZE</c>) и
/// приложение само отвечает, что у него под курсором (<c>WM_NCHITTEST</c>).
/// <para>
/// Соблазн поставить <see cref="FormBorderStyle.None"/> и двигать окно руками велик, но
/// так теряется всё перечисленное сразу, и вернуть это самому нельзя: прилипание к краям
/// живёт в системе, а не в сообщении о перетаскивании.
/// </para>
/// <para>
/// Клиентская область после этого совпадает с окном целиком, и <see cref="Form.ClientSize"/>
/// перестаёт быть тем, чем кажется: он по-прежнему прибавляет к запрошенному толщину рамки,
/// которой уже нет. Размер задаётся через <see cref="SetInitialSize"/>, минимум — через
/// <see cref="SetMinimum"/>, и оба считают в единицах дизайна.
/// </para>
/// <para>
/// Развёрнутое окно отдельно возвращается в рабочую область экрана. Без этого оно уходит
/// под панель задач на толщину съеденной рамки — панель остаётся видна, а вот нижняя
/// строка содержимого прячется под ней.
/// </para>
/// </remarks>
internal class DesignForm : Form, IPaletteAware
{
    private readonly CaptionBar _caption = new();
    private readonly Surface _content = new();

    /// <summary>Минимум окна в единицах дизайна: пересчитывается при переносе на другой монитор.</summary>
    private Size _minimum = Size.Empty;

    protected DesignForm()
    {
        // Именно Sizable, а не None: стиль окна — это и есть источник тени, скругления
        // и прилипания. Рамку убирает обработка сообщений ниже, а не смена стиля.
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        Icon = AppIcon.ForWindows;
        ShowIcon = Icon is not null;

        // Заполняющий контрол добавляется первым, полоса заголовка — после него:
        // при стыковке WinForms отдаёт край тому, кто добавлен позже.
        Controls.Add(_content);
        Controls.Add(_caption);
    }

    /// <summary>Куда складывать содержимое окна. Полоса заголовка сюда не входит.</summary>
    protected Control Content => _content;

    /// <inheritdoc/>
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            _caption.Caption = value ?? string.Empty;
        }
    }

    /// <summary>
    /// Задаёт минимальный размер в единицах дизайна.
    /// </summary>
    /// <remarks>
    /// Не <see cref="Form.MinimumSize"/> напрямую: на 175 % окно с минимумом в пикселях
    /// откроется, а содержимое в него не поместится. Минимум обязан расти вместе с масштабом.
    /// </remarks>
    protected void SetMinimum(Size designUnits)
    {
        _minimum = designUnits;
        ApplyMinimum();
    }

    /// <summary>
    /// Задаёт начальный размер окна в единицах дизайна.
    /// </summary>
    /// <remarks>
    /// Не <see cref="Form.ClientSize"/>: он прибавляет к запрошенному толщину рамки, а рамки
    /// у окна больше нет — клиентская область и есть всё окно. Просьба о клиентских 880
    /// давала бы окно в 898, и раскладка расходилась бы с дизайном на постоянную величину.
    /// </remarks>
    protected void SetInitialSize(Size designUnits)
    {
        Size = Metrics.Of(this).Scale(designUnits);
    }

    /// <summary>Перечитывает палитру. Тема меняется мгновенно, без перезапуска окна.</summary>
    public void RefreshPalette()
    {
        var palette = Palette.Current();
        BackColor = palette.Window;
        _content.BackColor = palette.Window;
        Invalidate(invalidateChildren: true);
    }


    /// <inheritdoc/>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Радиус выбирает Windows: DWMWA_WINDOW_CORNER_PREFERENCE знает «круглые» и
        // «чуть круглые», но не число. Дизайн просит 13 — расхождение принято осознанно,
        // потому что резать окно регионом даёт зубчатый край и ломает тень.
        var round = NativeMethods.DwmCornerRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmWindowCornerPreference,
            ref round,
            sizeof(int));

        ApplyMinimum();
        RefreshPalette();
    }

    /// <inheritdoc/>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyMinimum();
    }

    /// <inheritdoc/>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _caption.IsActiveWindow = true;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _caption.IsActiveWindow = false;
    }

    /// <inheritdoc/>
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WmNcCalcSize when m.WParam != 0:
                TakeOverFrame(ref m);
                return;

            case NativeMethods.WmNcHitTest:
                m.Result = HitTest(new Point(m.LParam.ToInt32()));
                return;
        }

        base.WndProc(ref m);
    }

    private void ApplyMinimum()
    {
        if (_minimum != Size.Empty)
        {
            MinimumSize = Metrics.Of(this).Scale(_minimum);
        }
    }

    /// <summary>Растягивает клиентскую область на всё окно, оставив системе саму рамку.</summary>
    private void TakeOverFrame(ref Message m)
    {
        // Развёрнутое окно система кладёт на рабочую область плюс рамку. Рамки у нас нет,
        // поэтому содержимое уехало бы под панель задач ровно на её толщину.
        //
        // Развёрнутость спрашивается у системы: WindowState во время самого разворота ещё
        // говорит «обычное», и по нему поправка не срабатывала бы вовсе.
        if (NativeMethods.IsZoomed(Handle) && WorkArea() is { } work)
        {
            var parameters = Marshal.PtrToStructure<NativeMethods.NcCalcSizeParams>(m.LParam);
            parameters.Proposed = work;
            Marshal.StructureToPtr(parameters, m.LParam, fDeleteOld: false);
        }

        m.Result = 0;
    }

    /// <summary>Рабочая область монитора, на котором сейчас окно.</summary>
    private NativeMethods.Rect? WorkArea()
    {
        var monitor = NativeMethods.MonitorFromWindow(Handle, NativeMethods.MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return null;
        }

        var info = new NativeMethods.MonitorInfo { CbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var work = info.Work;

        // Автоскрывающейся панели нужен хотя бы один пиксель, за который её можно задеть.
        // Окно, занявшее рабочую область целиком, эту панель больше не выпускает.
        if (HasAutoHideBar(NativeMethods.AbeBottom, info.Monitor))
        {
            work.Bottom -= 1;
        }

        if (HasAutoHideBar(NativeMethods.AbeTop, info.Monitor))
        {
            work.Top += 1;
        }

        if (HasAutoHideBar(NativeMethods.AbeLeft, info.Monitor))
        {
            work.Left += 1;
        }

        if (HasAutoHideBar(NativeMethods.AbeRight, info.Monitor))
        {
            work.Right -= 1;
        }

        return work;
    }

    private static bool HasAutoHideBar(uint edge, NativeMethods.Rect monitor)
    {
        var data = new NativeMethods.AppBarData
        {
            CbSize = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(),
            Edge = edge,
            Bounds = monitor,
        };

        return NativeMethods.SHAppBarMessage(NativeMethods.AbmGetAutoHideBarEx, ref data) != 0;
    }

    /// <summary>Что у окна под курсором. Вся геометрия — в <see cref="FrameHitTest"/>.</summary>
    private nint HitTest(Point screen)
    {
        var metrics = Metrics.Of(this);

        return FrameHitTest.At(
            PointToClient(screen),
            ClientSize,
            _caption.Height,
            metrics.ResizeBorder,
            metrics.ResizeCorner,
            sizable: WindowState == FormWindowState.Normal
                && FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow);
    }

    /// <summary>Полотно под содержимое: фон берётся из палитры, а не из системного цвета.</summary>
    private sealed class Surface : Panel
    {
        public Surface()
        {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
        }
    }
}
