using GoHome.Core;

namespace GoHome.Ui;

/// <summary>
/// Оформление окон приложения.
/// </summary>
/// <remarks>
/// Кольца в трее это не касается вовсе, и так и должно остаться. Кольцо живёт на панели
/// задач и берёт цвета из <see cref="Interop.SystemTheme.IsDarkTaskbar"/> — то есть из
/// оформления самой панели. Принудительно тёмное кольцо на светлой панели снова стало бы
/// невидимым, а это ровно тот баг, который уже чинился.
/// </remarks>
internal static class WindowTheme
{
    /// <summary>Применяет тему ко всем окнам, которые откроются дальше.</summary>
    /// <remarks>
    /// Уже открытые окна перекрашивают клиентскую часть сразу (<see cref="Refresh"/>),
    /// а заголовок окна рисует сама Windows по атрибуту, который выставляется при создании
    /// окна, — он догонит при следующем открытии.
    /// </remarks>
    public static void Apply(AppTheme theme)
    {
        Application.SetColorMode(theme switch
        {
            AppTheme.Light => SystemColorMode.Classic,
            AppTheme.Dark => SystemColorMode.Dark,
            _ => SystemColorMode.System,
        });

        Refresh();
    }

    /// <summary>Перекрашивает открытые окна под текущую тему.</summary>
    public static void Refresh()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (!form.IsDisposed && form.IsHandleCreated)
            {
                form.Invalidate(invalidateChildren: true);
            }
        }
    }
}