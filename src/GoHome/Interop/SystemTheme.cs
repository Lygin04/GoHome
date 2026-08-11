using Microsoft.Win32;

namespace GoHome.Interop;

/// <summary>
/// Оформление Windows: от него зависят цвета кольца в трее.
/// </summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Тема панели задач и системных элементов. Оформление окон приложений задаёт
    /// соседний <c>AppsUseLightTheme</c> — кольцо живёт на панели, и брать нужно этот.
    /// </summary>
    private const string SystemThemeValue = "SystemUsesLightTheme";

    /// <summary>Панель задач тёмная. По умолчанию — да: так выглядит Windows из коробки.</summary>
    public static bool IsDarkTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(SystemThemeValue) is int usesLightTheme
                ? usesLightTheme == 0
                : true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    /// <summary>
    /// Включён режим высокой контрастности. Читается каждый раз заново:
    /// режим переключается горячими клавишами в любой момент.
    /// </summary>
    public static bool IsHighContrast() => SystemInformation.HighContrast;
}