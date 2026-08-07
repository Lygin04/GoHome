using GoHome.Interop;

namespace GoHome.Ui;

/// <summary>
/// Владелец иконки в трее. Держит текущий хендл и следит за порядком его замены.
/// </summary>
/// <remarks>
/// Кольцо перерисовывается раз в минуту. <c>Icon.FromHandle</c> хендлом не владеет,
/// и сборщик мусора его не освободит — без явного <c>DestroyIcon</c> за рабочий день
/// набегает сотня утёкших объектов GDI. Порядок строгий: сперва подменить иконку
/// у <see cref="NotifyIcon"/>, и только потом убить предыдущий хендл — иначе трей
/// на мгновение остаётся с уничтоженным хендлом.
/// </remarks>
public sealed class TrayRing : IDisposable
{
    private readonly NotifyIcon _target;
    private nint _handle;
    private Icon? _icon;
    private RingVisual? _shown;
    private bool _disposed;

    public TrayRing(NotifyIcon target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
    }

    /// <summary>Сторона иконки трея из системной метрики: на 125% DPI система ждёт 20 пикселей, а не 16.</summary>
    public static int IconSize => Math.Clamp(SystemInformation.SmallIconSize.Width, 16, 64);

    /// <summary>Перерисовать на следующем обновлении: сменилась тема или параметры экрана.</summary>
    public void Invalidate() => _shown = null;

    /// <summary>Обновляет иконку. Возвращает <c>true</c>, если картинку действительно перерисовали.</summary>
    public bool Update(double progress, RingMood mood)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var visual = RingVisual.From(IconSize, progress, mood, SystemTheme.IsDarkTaskbar());
        if (_shown == visual)
        {
            return false;
        }

        using var bitmap = TrayRingPainter.Paint(visual);
        var handle = bitmap.GetHicon();

        var previousHandle = _handle;
        var previousIcon = _icon;

        _icon = Icon.FromHandle(handle);
        _handle = handle;
        _shown = visual;

        _target.Icon = _icon;

        previousIcon?.Dispose();
        Release(previousHandle);

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _target.Icon = null;

        var icon = _icon;
        var handle = _handle;
        _icon = null;
        _handle = nint.Zero;
        _shown = null;

        icon?.Dispose();
        Release(handle);
    }

    private static void Release(nint handle)
    {
        if (handle != nint.Zero)
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}