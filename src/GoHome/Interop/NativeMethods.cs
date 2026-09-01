using System.Runtime.InteropServices;

namespace GoHome.Interop;

/// <summary>Вызовы Win32. Внешних зависимостей у приложения нет — только BCL и P/Invoke.</summary>
internal static class NativeMethods
{
    internal const int UoiName = 2;
    internal const uint DesktopReadObjects = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        public uint CbSize;

        /// <summary>Счётчик тиков системы — 32 бита, переполняется через 49 суток аптайма.</summary>
        public uint DwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint OpenInputDesktop(
        uint dwFlags,
        [MarshalAs(UnmanagedType.Bool)] bool fInherit,
        uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(nint hDesktop);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetUserObjectInformation(
        nint hObj,
        int nIndex,
        [Out] byte[] pvInfo,
        uint nLength,
        out uint lpnLengthNeeded);

    // ---- окно без системной рамки ----------------------------------------------------
    //
    // Заголовок окна приложение рисует само, но рамку у Windows не забирает: стили окна
    // остаются обычными, и всё, что система делает бесплатно — тень, скругление углов,
    // прилипание к краям, сочетания с клавишей Windows, системное меню по Alt+Пробел —
    // продолжает работать. Забирается только клиентская область (WM_NCCALCSIZE) и решение
    // о том, что под курсором (WM_NCHITTEST).

    internal const int WmNcCalcSize = 0x0083;
    internal const int WmNcHitTest = 0x0084;
    internal const int WmNcRButtonUp = 0x00A5;
    internal const int WmDpiChanged = 0x02E0;

    /// <summary>Курсор не над этим окном — пусть решает то, что под ним.</summary>
    internal const int HtTransparent = -1;

    internal const int HtClient = 1;
    internal const int HtCaption = 2;
    internal const int HtLeft = 10;
    internal const int HtRight = 11;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtBottom = 15;
    internal const int HtBottomLeft = 16;
    internal const int HtBottomRight = 17;

    internal const uint MonitorDefaultToNearest = 0x0002;

    /// <summary>Скругление углов окна. Радиус выбирает Windows, приложение только просит.</summary>
    internal const int DwmWindowCornerPreference = 33;

    /// <summary>Скруглять как системные окна.</summary>
    internal const int DwmCornerRound = 2;

    /// <summary>Спросить у оболочки автоскрывающуюся панель на заданном крае монитора.</summary>
    internal const uint AbmGetAutoHideBarEx = 0x0000000B;

    internal const uint AbeLeft = 0;
    internal const uint AbeTop = 1;
    internal const uint AbeRight = 2;
    internal const uint AbeBottom = 3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    /// <summary>
    /// Первый прямоугольник — предлагаемая системой клиентская область. Остальные поля
    /// структуры нужны системе для переноса содержимого и здесь не трогаются.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NcCalcSizeParams
    {
        public Rect Proposed;
        public Rect Before;
        public Rect Clipped;
        public nint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int CbSize;
        public Rect Monitor;

        /// <summary>Экран за вычетом панели задач — то, что должно занять развёрнутое окно.</summary>
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        public uint CbSize;
        public nint Wnd;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Bounds;
        public nint Param;
    }

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    /// <summary>
    /// Окно развёрнуто.
    /// </summary>
    /// <remarks>
    /// Спрашивается у системы, а не у <c>Form.WindowState</c>: во время самого разворота
    /// WinForms ещё не знает о нём, а решать, отдавать ли окну рабочую область, нужно
    /// уже в этот момент — иначе поправка не сработает ни разу.
    /// </remarks>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("shell32.dll")]
    internal static extern nint SHAppBarMessage(uint dwMessage, ref AppBarData pData);
}
