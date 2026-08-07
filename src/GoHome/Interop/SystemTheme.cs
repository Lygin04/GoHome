using Microsoft.Win32;

namespace GoHome.Interop;

/// <summary>
/// Тема панели задач. Полупрозрачный белый трек кольца невидим на светлой панели,
/// поэтому подложку приходится подбирать под тему.
/// </summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
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
}
