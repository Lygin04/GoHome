using Microsoft.Win32;

namespace GoHome.Interop;

/// <summary>
/// Оформление Windows.
/// </summary>
/// <remarks>
/// Windows оформляет панель задач и окна приложений по отдельности, двумя разными
/// значениями. Приложение спрашивает про них в разных местах и по разным поводам:
/// кольцо в трее живёт на панели задач, окна — сами по себе. Подставить одно вместо
/// другого — значит получить невидимое кольцо на панели противоположного оформления.
/// </remarks>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Оформление панели задач и системных элементов.</summary>
    private const string TaskbarThemeValue = "SystemUsesLightTheme";

    /// <summary>Оформление окон приложений.</summary>
    private const string WindowsThemeValue = "AppsUseLightTheme";

    /// <summary>Панель задач тёмная. По умолчанию — да: так выглядит Windows из коробки.</summary>
    public static bool IsDarkTaskbar() => IsDark(TaskbarThemeValue);

    /// <summary>
    /// Окна приложений тёмные.
    /// </summary>
    /// <remarks>
    /// Нужно только для настройки «как в системе». При явно выбранной светлой или тёмной
    /// теме сюда не заглядывают вовсе: выбор человека сильнее системы.
    /// </remarks>
    public static bool IsDarkWindows() => IsDark(WindowsThemeValue);

    /// <summary>
    /// Включён режим высокой контрастности. Читается каждый раз заново:
    /// режим переключается горячими клавишами в любой момент.
    /// </summary>
    public static bool IsHighContrast() => SystemInformation.HighContrast;

    /// <summary>Читает одно значение оформления. Не прочиталось — считаем тёмным.</summary>
    private static bool IsDark(string value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(value) is not int usesLightTheme || usesLightTheme == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }
}