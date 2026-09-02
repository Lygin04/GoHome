using GoHome.Core;
using GoHome.Ui.Design;

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

    /// <summary>
    /// Перекрашивает открытые окна под текущую тему.
    /// </summary>
    /// <remarks>
    /// Одной перерисовки мало. Нарисованные контролы спрашивают цвет у
    /// <see cref="Palette.Current"/> прямо на отрисовке и обновляются сами, но фон элемент
    /// берёт у родителя через <see cref="Control.BackColor"/> — а окно и карточка рисуют
    /// себя не им. Без обхода дерева окно осталось бы в старом фоне с содержимым в новом.
    /// </remarks>
    public static void Refresh()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (!form.IsDisposed && form.IsHandleCreated)
            {
                Restate(form);
                form.Invalidate(invalidateChildren: true);
            }
        }
    }

    /// <summary>Обходит дерево и даёт перечитать палитру тем, кому перерисовки мало.</summary>
    private static void Restate(Control control)
    {
        if (control is IPaletteAware aware)
        {
            aware.RefreshPalette();
        }

        foreach (Control child in control.Controls)
        {
            Restate(child);
        }
    }
}