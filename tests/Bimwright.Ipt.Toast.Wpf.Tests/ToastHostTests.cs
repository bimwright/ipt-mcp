using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Bimwright.Ipt.Toast.Wpf.Tests;

/// <summary>
/// The Inventor-specific toast host around rvt-mcp's card: real dedicated dispatcher and unowned HWNDs;
/// no Inventor SDK/application required. The card itself is covered by tests/Bimwright.Ipt.Toast.Tests.
/// </summary>
public sealed class ToastHostTests
{
    [Fact]
    public void Notifier_uses_configured_idle_on_the_next_deadline_without_resetting_the_current_card()
    {
        using var notifier = new ToastNotifier(true, "ipt-mcp 2027", () => { }, 10);
        var activity = Field<ActivityAggregator>(notifier, "_activity");
        var now = Field<Func<TimeSpan>>(activity, "_now");
        activity.Record(Result(1), true);
        var card = activity.TakeRender().Card!;
        var firstDeadline = Field<TimeSpan>(activity, "_deadline");
        Assert.InRange((firstDeadline - now()).TotalSeconds, 9, 10);

        notifier.SetIdleSeconds(60);
        Assert.Equal(firstDeadline, Field<TimeSpan>(activity, "_deadline"));
        activity.PointerEntered(card.CardId);
        activity.PointerLeft(card.CardId);
        Assert.InRange((Field<TimeSpan>(activity, "_deadline") - now()).TotalSeconds, 59, 60);
        Assert.Equal(card.CardId, activity.TakeRender().Card!.CardId);

        notifier.SetIdleSeconds(30);
        activity.Record(Result(2), true);
        Assert.InRange((Field<TimeSpan>(activity, "_deadline") - now()).TotalSeconds, 29, 30);
        Assert.Equal(2, activity.TakeRender().Card!.Succeeded);
        notifier.SetIdleSeconds(15);
        activity.Record(Result(3), true);
        Assert.InRange((Field<TimeSpan>(activity, "_deadline") - now()).TotalSeconds, 19, 20);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;

    private static ToastModel Result(int n) => new("extrude", "Extrude " + n, "Feature", "Created", "",
        null, ToolActivityKind.Write, true, 0);

    private static InventorUiSnapshot Frame(Window frame)
        => new(true, new WindowInteropHelper(frame).Handle.ToInt64(), 0);

    [Fact]
    public void Pending_without_hwnd_restores_on_toast_thread_and_survives_busy_host_sta()
        => Sta.Run(() =>
        {
            var frame = new Window { Title = "Inventor test frame", Left = 80, Top = 80, Width = 900, Height = 600 };
            ToastHost? host = null;
            try
            {
                frame.Show();
                var activity = new ActivityAggregator(now: () => TimeSpan.Zero);
                var usable = false;
                host = new ToastHost(Frame(frame), activity, "ipt-mcp 2022", () => { },
                    usable: _ => Volatile.Read(ref usable));
                if (activity.Record(Result(1), false)) host.RequestRender();
                Thread.Sleep(300); // main STA deliberately not pumped
                Assert.Empty(ToastWindows());
                Volatile.Write(ref usable, true);
                Assert.True(Wait(() => ToastWindows().Count == 1)); // pending flush needs no main STA callback
                var first = Assert.Single(ToastWindows());
                for (var i = 2; i <= 100; i++)
                    if (activity.Record(Result(i), true)) host.RequestRender();
                Thread.Sleep(300);
                Assert.Equal(first, Assert.Single(ToastWindows())); // updated in place, no playback queue
                Volatile.Write(ref usable, false);
                Assert.True(Wait(() => ToastWindows().Count == 0));
                Volatile.Write(ref usable, true);
                Assert.True(Wait(() => ToastWindows().Count == 1));
                var clock = Stopwatch.StartNew();
                host.Shutdown();
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
                Assert.Empty(ToastWindows());
            }
            finally { host?.Shutdown(); frame.Close(); }
        });

    [Fact]
    public void A_dismissed_card_fades_out_and_is_not_resurrected_by_a_later_render()
        => Sta.Run(() =>
        {
            var frame = new Window { Left = 80, Top = 80, Width = 900, Height = 600 };
            ToastHost? host = null;
            try
            {
                frame.Show();
                var activity = new ActivityAggregator(now: () => TimeSpan.Zero);
                var usable = true;
                host = new ToastHost(Frame(frame), activity, "ipt-mcp 2027", () => { },
                    usable: _ => Volatile.Read(ref usable), motion: () => true);
                activity.Record(Result(1), true);
                host.RequestRender();
                Assert.True(Wait(() => ToastWindows().Count == 1));
                var id = activity.TakeRender().Card!.CardId;
                Assert.True(activity.Dismiss(id));
                host.RequestRender();
                Volatile.Write(ref usable, false);
                Assert.True(Wait(() => !activity.HasUnrenderedResults && ToastWindows().Count == 0));
                Volatile.Write(ref usable, true);
                host.RequestRender();
                Thread.Sleep(300);
                Assert.False(activity.HasUnrenderedResults);
                Assert.Empty(ToastWindows());
            }
            finally { host?.Shutdown(); frame.Close(); }
        });

    [Fact]
    public void Card_uses_the_RVT_owner_corner_in_physical_pixels()
        => Sta.Run(() =>
        {
            // Keep the fixture inside the current work area so app bars do not trigger clamping.
            var work = SystemParameters.WorkArea;
            var frame = new Window { Left = work.Left + 80, Top = work.Top + 80, Width = 900, Height = 600 };
            ToastHost? host = null;
            try
            {
                frame.Show();
                var ui = Frame(frame);
                var main = new IntPtr(ui.MainHwnd);
                var activity = new ActivityAggregator(now: () => TimeSpan.Zero);
                host = new ToastHost(ui, activity, "ipt-mcp 2027", () => { }, usable: _ => true);
                activity.Record(Result(1), true);
                host.RequestRender();
                Assert.True(Wait(() => ToastWindows().Count == 1));
                var toast = Assert.Single(ToastWindows());
                var frameRect = ToastNative.Rect(main)!.Value;
                var dpi = ToastNative.Dpi(main);
                GetWindowRect(toast, out var rect);
                // RVT parity: top corners add clearance below the title bar and ribbon tabs.
                Assert.InRange(rect.Left, frameRect.Left + ToastLayout.Px(ToastLayout.EdgeDip, dpi) - 2,
                    frameRect.Left + ToastLayout.Px(ToastLayout.EdgeDip, dpi) + 2);
                var topInset = ToastPlacement.Margin + ToastPlacement.RibbonClearance;
                Assert.InRange(rect.Top, frameRect.Top + ToastLayout.Px(topInset, dpi) - 2,
                    frameRect.Top + ToastLayout.Px(topInset, dpi) + 2);
            }
            finally { host?.Shutdown(); frame.Close(); }
        });

    [Fact]
    public void Toggle_before_first_result_does_not_strand_render_and_reports_respect_disable()
        => Sta.Run(() =>
        {
            var frame = new Window { Left = 80, Top = 80, Width = 900, Height = 600 };
            ToastNotifier? notifier = null;
            try
            {
                frame.Show();
                notifier = new ToastNotifier(false, "ipt-mcp 2027", () => { });
                notifier.Enabled = true; // Reset claims render before any ToastHost exists
                notifier.UpdateSnapshot(Frame(frame));
                var report = new ToastEvent("report_task_result", true,
                    JObject.FromObject(new { task_id = "job-1", outcome = "completed", summary = "Verified" }), null, null, 0, true);
                Assert.True(notifier.Notify(report));
                Assert.True(Wait(() => ToastWindows().Count == 1));
                notifier.Enabled = false;
                Assert.False(notifier.Notify(report));
                Assert.True(Wait(() => ToastWindows().Count == 0));
                notifier.NotifyToggle(persisted: false);
                Assert.True(Wait(() => ToastWindows().Count == 1));
                notifier.Dispose();
                Assert.Empty(ToastWindows());
                Assert.False(notifier.Notify(report));
            }
            finally { notifier?.Dispose(); frame.Close(); }
        });

    [Fact]
    public void Connection_before_visible_snapshot_is_retried_when_sta_supplies_ready_frame()
        => Sta.Run(() =>
        {
            var frame = new Window { Left = 80, Top = 80, Width = 900, Height = 600 };
            using var notifier = new ToastNotifier(true, "ipt-mcp 2027", () => { });
            try
            {
                Assert.False(notifier.NotifyConnection("TCP:1234"));
                for (var i = 0; i < 25; i++) notifier.UpdateSnapshot(InventorUiSnapshot.Empty);
                Assert.Empty(ToastWindows());
                frame.Show();
                notifier.UpdateSnapshot(Frame(frame));
                Assert.True(Wait(() => ToastWindows().Count == 1));
            }
            finally { notifier.Dispose(); frame.Close(); }
        });

    private static bool Wait(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 4000) Thread.Sleep(20);
        return condition();
    }

    /// <summary>
    /// Toast cards are the only windows of this process that are visible and never activated
    /// (WS_EX_NOACTIVATE), which is how they are told apart from the test's stand-in Inventor frame.
    /// </summary>
    private static List<IntPtr> ToastWindows()
    {
        const long NoActivate = 0x08000000L;
        var windows = new List<IntPtr>();
        var pid = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid == pid && IsWindowVisible(hwnd)
                && (GetWindowLongPtr(hwnd, -20).ToInt64() & NoActivate) != 0)
                windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
