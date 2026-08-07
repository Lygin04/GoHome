using System.Diagnostics;
using System.Runtime.InteropServices;
using GoHome.Ui;

namespace GoHome.Tests;

/// <summary>
/// Кольцо перерисовывается раз в минуту весь рабочий день. Здесь проверяется,
/// что старый хендл иконки действительно освобождается: сборщик мусора его не тронет.
/// </summary>
public class TrayRingHandleTests
{
    private const uint GdiObjects = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(nint hProcess, uint uiFlags);

    private static uint CountGdiObjects() => GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjects);

    [Fact]
    public void Перерисовка_не_копит_объекты_gdi()
    {
        using var notifyIcon = new NotifyIcon();
        using var ring = new TrayRing(notifyIcon);

        for (var step = 0; step < 20; step++)
        {
            ring.Update((double)step / RingVisual.TotalSteps, RingMood.Running);
        }

        var before = CountGdiObjects();

        for (var step = 20; step < 320; step++)
        {
            ring.Update((double)step / RingVisual.TotalSteps, RingMood.Running);
        }

        var after = CountGdiObjects();

        // Без DestroyIcon здесь набежало бы триста хендлов.
        Assert.True(after < before + 30, $"объектов GDI было {before}, стало {after}");
    }

    [Fact]
    public void Неизменившаяся_картинка_не_перерисовывается()
    {
        using var notifyIcon = new NotifyIcon();
        using var ring = new TrayRing(notifyIcon);

        Assert.True(ring.Update(0.5, RingMood.Running));
        Assert.False(ring.Update(0.5, RingMood.Running));
        Assert.False(ring.Update(0.5001, RingMood.Running));

        Assert.True(ring.Update(0.5, RingMood.Paused));
        Assert.True(ring.Update(0.6, RingMood.Paused));
    }

    [Fact]
    public void Сброс_заставляет_перерисовать()
    {
        using var notifyIcon = new NotifyIcon();
        using var ring = new TrayRing(notifyIcon);

        Assert.True(ring.Update(0.5, RingMood.Running));
        Assert.False(ring.Update(0.5, RingMood.Running));

        ring.Invalidate();

        Assert.True(ring.Update(0.5, RingMood.Running));
    }

    [Fact]
    public void После_освобождения_иконка_снимается()
    {
        using var notifyIcon = new NotifyIcon();
        var ring = new TrayRing(notifyIcon);

        ring.Update(0.5, RingMood.Running);
        Assert.NotNull(notifyIcon.Icon);

        ring.Dispose();

        Assert.Null(notifyIcon.Icon);
        Assert.Throws<ObjectDisposedException>(() => ring.Update(0.5, RingMood.Running));
    }

    [Fact]
    public void Размер_иконки_берётся_из_системной_метрики()
    {
        // На 125% DPI система ждёт 20 пикселей, а не 16.
        Assert.Equal(SystemInformation.SmallIconSize.Width, TrayRing.IconSize);
    }
}