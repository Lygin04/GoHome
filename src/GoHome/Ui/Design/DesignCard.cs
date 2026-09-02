using System.ComponentModel;

namespace GoHome.Ui.Design;

/// <summary>
/// Карточка: фон на тон светлее окна, рамка, скругление 10.
/// </summary>
/// <remarks>
/// В дизайне карточка отделена от окна тенью. Мягкой тени GDI+ не рисует — только размытием
/// вручную, а это лишний слой на каждой перерисовке. Роль тени здесь берут на себя рамка
/// и разница фонов: в тёмной теме карточка светлее окна, в светлой — белая на сером.
/// <para>
/// Заголовок секции рисуется прописными вразрядку, как в дизайне. Разрядки GDI+ не умеет,
/// поэтому знаки ставит по одному <see cref="Typography.DrawLabel"/> — для короткой метки
/// это дёшево.
/// </para>
/// </remarks>
internal sealed class DesignCard : Panel, IPaletteAware
{
    private const int Inset = 16;

    public DesignCard()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>Метка секции. Пустая — карточка без заголовка.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PerformLayout();
                Invalidate();
            }
        }
    } = string.Empty;

    /// <summary>Пояснение под меткой. Пустое — строки не будет.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Note
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PerformLayout();
                Invalidate();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Карточка без своей рамки и фона: только заголовок и отступы.
    /// </summary>
    /// <remarks>
    /// В настройках дизайн раскладывает разделы без карточек — заголовок, пояснение и строки
    /// прямо на фоне окна. Это та же сущность, поэтому не отдельный контрол, а признак.
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Bare
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
    }

    /// <summary>
    /// Перечитывает палитру.
    /// </summary>
    /// <remarks>
    /// Карточка рисует себя цветом <see cref="Palette.Card"/>, а дети берут фон из её
    /// <see cref="Control.BackColor"/>. Без обновления кнопка внутри карточки затирала бы
    /// свой угол цветом окна.
    /// </remarks>
    public void RefreshPalette()
    {
        var palette = Palette.Current();
        BackColor = Bare ? (Parent?.BackColor ?? palette.Window) : palette.Card;
        Invalidate(invalidateChildren: true);
    }

    /// <summary>Где начинается содержимое — под заголовком и отступами.</summary>
    public Rectangle ContentBounds
    {
        get
        {
            var metrics = Metrics.Of(this);
            var inset = Bare ? 0 : metrics.Scale(Inset);
            var top = inset + HeaderHeight(metrics);

            return Rectangle.FromLTRB(
                inset,
                top,
                Math.Max(inset, Width - inset),
                Math.Max(top, Height - inset));
        }
    }

    /// <inheritdoc/>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshPalette();
    }

    /// <inheritdoc/>
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

        if (!Bare)
        {
            Draw.Surface(
                e.Graphics,
                ClientRectangle,
                new Face(palette.Card, palette.Line, palette.Ink),
                metrics.RadiusCard,
                Draw.LineWidth(metrics));
        }

        var inset = Bare ? 0 : metrics.Scale(Inset);
        var top = inset;

        if (Label.Length > 0)
        {
            fonts.DrawLabel(e.Graphics, Label, new Point(inset, top), palette.Faint);
            top += fonts.Label.Height + metrics.Space(1);
        }

        if (Note.Length > 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                Note,
                fonts.Caption,
                new Rectangle(inset, top, Math.Max(0, Width - (inset * 2)), fonts.Caption.Height),
                palette.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        base.OnPaint(e);
    }

    /// <summary>Сколько места занимает заголовок карточки — метка и пояснение под ней.</summary>
    private int HeaderHeight(Metrics metrics)
    {
        if (Label.Length == 0 && Note.Length == 0)
        {
            return 0;
        }

        var fonts = Typography.Of(metrics);
        var height = 0;

        if (Label.Length > 0)
        {
            height += fonts.Label.Height + metrics.Space(1);
        }

        if (Note.Length > 0)
        {
            height += fonts.Caption.Height;
        }

        return height + metrics.Space(3);
    }
}
