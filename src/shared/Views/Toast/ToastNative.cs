#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Win32 for the toast thread. Calls on Inventor HWNDs are read-only (spike 1a): the toast thread never
/// sends or posts window messages to Inventor windows. The only writes go to our own toast windows.
/// </summary>
internal static class ToastNative
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public static Point CursorPosition() => GetCursorPos(out var point)
        ? new Point(point.X, point.Y) : new Point(double.NaN, double.NaN);

    /// <summary>Spike 1a: without this the toast becomes the foreground window and takes Inventor's focus.</summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    public static void MoveNoActivate(IntPtr hwnd, int x, int y)
        => SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

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
