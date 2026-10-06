#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Runtime.InteropServices;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Win32 for the toast thread. Calls on Inventor HWNDs are read-only (spike 1a): the toast thread never
/// sends or posts window messages to Inventor windows.
/// </summary>
internal static class ToastNative
{

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);

    public static PxRect? Rect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !GetWindowRect(hwnd, out var r)) return null;
        return new PxRect(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static PxRect? VisibleRect(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindowVisible(hwnd) ? Rect(hwnd) : null;

    public static HostWindowState MainState(IntPtr main)
    {
        var exists = main != IntPtr.Zero && IsWindow(main);
        return new HostWindowState(exists, exists && IsWindowVisible(main), exists && IsIconic(main), exists && IsWindowEnabled(main));
    }

    public static uint Dpi(IntPtr hwnd)
    {
        try
        {
            var dpi = hwnd == IntPtr.Zero ? 0u : GetDpiForWindow(hwnd);
            return dpi == 0 ? 96u : dpi;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;   // Windows before 10 1607
        }
    }
}
#endif
