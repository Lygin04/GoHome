using System.Text;

namespace GoHome.Interop;

/// <summary>Заблокирован ли экран прямо сейчас.</summary>
/// <remarks>
/// Нужно на старте: после ночного обновления корпоративная Windows логинится в сессию
/// пользователя сама (ARSO) и блокирует экран. Приход в такой сессии писать нельзя.
/// </remarks>
public static class WorkstationState
{
    private const string InteractiveDesktop = "Default";

    /// <summary>
    /// Экран заблокирован. Проверяется по имени активного десктопа: под локскрином
    /// активен Winlogon, и открыть его обычным процессом не получится.
    /// </summary>
    public static bool IsLocked()
    {
        var desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DesktopReadObjects);
        if (desktop == nint.Zero)
        {
            // Активный десктоп недоступен — значит, это защищённый десктоп локскрина.
            return true;
        }

        try
        {
            var buffer = new byte[512];
            if (!NativeMethods.GetUserObjectInformation(desktop, NativeMethods.UoiName, buffer, (uint)buffer.Length, out var needed))
            {
                return false;
            }

            var length = (int)Math.Min(needed, (uint)buffer.Length);
            var name = Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0');
            return !string.Equals(name, InteractiveDesktop, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            NativeMethods.CloseDesktop(desktop);
        }
    }
}