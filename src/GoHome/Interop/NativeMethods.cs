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
}