// PROTOTYPE — throwaway toast compatibility spike (roadmap Phase 1a).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ToastSpike
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public int W => R - L; public int H => B - T; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret;
        }

        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_NOACTIVATE = 0x08000000L, WS_EX_TOOLWINDOW = 0x00000080L, WS_EX_TOPMOST = 0x00000008L, WS_EX_LAYERED = 0x00080000L;
        public const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        public const int SW_MINIMIZE = 6, SW_RESTORE = 9;
        public const uint WM_NULL = 0, WM_CLOSE = 0x10, SMTO_ABORTIFHUNG = 0x2;
        public const uint GW_OWNER = 4;

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        public delegate bool MonitorEnumProc(IntPtr m, IntPtr hdc, ref RECT r, IntPtr data);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
        [DllImport("shcore.dll")] public static extern int GetProcessDpiAwareness(IntPtr proc, out int value);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        public delegate bool EnumWndProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumThreadWindows(uint tid, EnumWndProc cb, IntPtr l);

        public static List<object> ThreadWindows(uint tid, bool visibleOnly)
        {
            var list = new List<object>();
            EnumThreadWindows(tid, (h, l) => { if (!visibleOnly || IsWindowVisible(h)) list.Add(Win(h)); return true; }, IntPtr.Zero);
            return list;
        }

        public static IntPtr FindThreadWindow(uint tid, string cls)
        {
            var found = IntPtr.Zero;
            EnumThreadWindows(tid, (h, l) => { if (IsWindowVisible(h) && ClassOf(h) == cls) { found = h; return false; } return true; }, IntPtr.Zero);
            return found;
        }

        public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
        public static string TitleOf(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
        public static long ExStyle(IntPtr h) => GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();

        public static object Win(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            GetWindowRect(h, out var r);
            var tid = GetWindowThreadProcessId(h, out var pid);
            return new
            {
                hwnd = h.ToInt64(), cls = ClassOf(h), title = TitleOf(h), pid, tid,
                rect = new[] { r.L, r.T, r.W, r.H }, dpi = GetDpiForWindow(h),
                dpi_awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(h)),
                visible = IsWindowVisible(h), iconic = IsIconic(h), owner = GetWindow(h, GW_OWNER).ToInt64(),
                owner_cls = ClassOf(GetWindow(h, GW_OWNER)), owner_tid = GetWindowThreadProcessId(GetWindow(h, GW_OWNER), out _),
                exstyle = "0x" + ExStyle(h).ToString("X"),
            };
        }

        public static object GuiThread(uint tid)
        {
            var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!GetGUIThreadInfo(tid, ref gi)) return new { ok = false, tid };
            return new { ok = true, tid, active = gi.hwndActive.ToInt64(), active_cls = ClassOf(gi.hwndActive), focus = gi.hwndFocus.ToInt64(), focus_cls = ClassOf(gi.hwndFocus), capture = gi.hwndCapture.ToInt64(), flags = gi.flags };
        }

        public static List<object> Monitors()
        {
            var list = new List<object>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref RECT r, IntPtr d) =>
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(m, ref mi);
                GetDpiForMonitor(m, 0, out var dx, out _);
                list.Add(new { handle = m.ToInt64(), rect = new[] { mi.rcMonitor.L, mi.rcMonitor.T, mi.rcMonitor.W, mi.rcMonitor.H }, primary = (mi.dwFlags & 1) != 0, effective_dpi = dx });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static List<RECT> MonitorRects()
        {
            var list = new List<RECT>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref RECT r, IntPtr d) =>
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(m, ref mi); list.Add(mi.rcMonitor); return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
