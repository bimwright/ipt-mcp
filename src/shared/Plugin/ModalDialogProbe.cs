using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>
/// Detects a modal dialog in Inventor WITHOUT touching the STA thread or the Inventor API (it runs
/// on the listener thread, while the STA may be stuck inside the dialog's message loop). A modal
/// dialog disables its owner, so: Inventor's main frame disabled + a visible, enabled top-level
/// window on the main UI thread = a modal dialog; its caption tells the agent what is waiting.
/// The add-in's own toast/history windows live on other threads and are never reported.
/// </summary>
internal static class ModalDialogProbe
{
    /// <summary>Main frame HWND, captured at Activate (on the STA) from Application.MainFrameHWND.</summary>
    public static IntPtr MainWindow { get; set; }

    public static JObject Probe()
    {
        var result = new JObject { ["open"] = false };
        var main = MainWindow;
        if (main == IntPtr.Zero || !IsWindow(main)) return result;
        try
        {
            if (IsWindowEnabled(main)) return result;
            var uiThread = GetWindowThreadProcessId(main, out var pid);
            var dialogs = new List<(string title, string cls)>();
            EnumWindows((h, _) =>
            {
                if (h == main || !IsWindowVisible(h) || !IsWindowEnabled(h)) return true;
                if (GetWindowThreadProcessId(h, out var wpid) != uiThread || wpid != pid) return true;
                dialogs.Add((Text(h), Class(h)));
                return dialogs.Count < 5;
            }, IntPtr.Zero);

            result["open"] = true;   // main frame disabled = something modal owns the UI
            // Prefer a standard dialog (#32770) caption; else the first enabled window.
            var pick = dialogs.Find(d => d.cls == "#32770");
            if (pick.title is null && dialogs.Count > 0) pick = dialogs[0];
            result["title"] = pick.title ?? "";
            if (dialogs.Count > 1)
            {
                var all = new JArray();
                foreach (var d in dialogs) all.Add(d.title);
                result["windows"] = all;
            }
        }
        catch
        {
            // probe is advisory
        }
        return result;
    }

    /// <summary>One-line suffix for TIMEOUT messages; empty when no modal dialog is detected.</summary>
    public static string TimeoutSuffix()
    {
        var probe = Probe();
        if (probe["open"]?.Value<bool>() != true) return "";
        var title = (string?)probe["title"];
        return " A modal dialog is open in Inventor" + (string.IsNullOrEmpty(title) ? "" : $" (\"{title}\")")
               + " and is blocking it — ask the user to answer it. Typed save/open/close tools run silently; "
               + "for send_code pass silent:true so Inventor answers prompts with their defaults.";
    }

    private static string Text(IntPtr h)
    {
        var len = GetWindowTextLength(h);
        var sb = new StringBuilder(Math.Max(len + 1, 2));
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Class(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);
}
