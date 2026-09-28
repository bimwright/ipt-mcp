using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Bimwright.Ipt.Toast.Wpf.Tests;

/// <summary>Real dedicated dispatcher and unowned HWNDs; no Inventor SDK/application required.</summary>
public sealed class ToastHostTests
{
    private static ToastModel Result(int n) => new("extrude", "Extrude " + n, "Feature", "Created", "",
        null, ToolActivityKind.Write, true, 0);

    [Fact]
    public void Pending_without_hwnd_restores_on_toast_thread_and_survives_busy_host_sta()
        => Sta.Run(() =>
        {
            var frame = new Window { Title = "Inventor test frame", Left = 80, Top = 80, Width = 900, Height = 600 };
            ToastHost? host = null;
            try
            {
                frame.Show();
                var ui = new InventorUiSnapshot(true, new WindowInteropHelper(frame).Handle.ToInt64(), 0, BackdropHint.None);
                var feed = new ToastFeed(now: () => TimeSpan.Zero);
                var usable = false;
                host = new ToastHost(ToastTheme.Light, ui, feed, "Inventor 2022", () => { },
                    usable: _ => Volatile.Read(ref usable));
                if (feed.Record(Result(1), false)) host.RequestRender();
                Thread.Sleep(300); // main STA deliberately not pumped
                Assert.Empty(ToastWindows());
                Volatile.Write(ref usable, true);
                Assert.True(Wait(() => ToastWindows().Count == 1)); // pending flush needs no main STA callback
                var first = Assert.Single(ToastWindows());
                for (var i = 2; i <= 100; i++)
                    if (feed.Record(Result(i), true)) host.RequestRender();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Suppression_finishes_a_closing_card_without_resurrection(bool requestRender)
        => Sta.Run(() =>
        {
            var frame = new Window { Left = 80, Top = 80, Width = 900, Height = 600 };
            ToastHost? host = null;
            try
            {
                frame.Show();
                var ui = new InventorUiSnapshot(true, new WindowInteropHelper(frame).Handle.ToInt64(), 0, BackdropHint.None);
                var feed = new ToastFeed(now: () => TimeSpan.Zero);
                var usable = true;
                var ready = new TaskCompletionSource<Dispatcher>();
                host = new ToastHost(ToastTheme.Light, ui, feed, "Inventor test", () => { },
                    usable: _ =>
                    {
                        ready.TrySetResult(Dispatcher.CurrentDispatcher);
                        return Volatile.Read(ref usable);
                    }, motion: () => true);
                feed.Record(Result(1), true);
                host.RequestRender();
                Assert.True(Wait(() => ToastWindows().Count == 1));
                var hwnd = Assert.Single(ToastWindows());
                Assert.True(ready.Task.Wait(4000));
                ready.Task.Result.Invoke(() =>
                {
                    var window = Assert.IsType<ToastWindow>(HwndSource.FromHwnd(hwnd)!.RootVisual);
                    Assert.True(feed.Dismiss(window.CardId));
                    window.BeginClose();
                    // Hold the fade so only suppression can close it, not natural animation completion.
                    window.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(1, 0, TimeSpan.FromMinutes(1)));
                    Volatile.Write(ref usable, false);
                    if (requestRender) host.RequestRender(); // otherwise exercise the independent tracker
                });
                Assert.True(Wait(() => !feed.HasWork && ToastWindows().Count == 0));
                Volatile.Write(ref usable, true);
                host.RequestRender();
                ready.Task.Result.Invoke(() => { }, DispatcherPriority.Background);
                Assert.False(feed.HasWork);
                Assert.Empty(ToastWindows());
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
                notifier = new ToastNotifier(new ToastSettings(false, ToastTheme.Light, "test", "test"),
                    "Inventor 2027", () => { });
                notifier.Enabled = true; // Reset claims render before any ToastHost exists
                notifier.UpdateSnapshot(new InventorUiSnapshot(true, new WindowInteropHelper(frame).Handle.ToInt64(), 0, BackdropHint.None));
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
            using var notifier = new ToastNotifier(new ToastSettings(true, ToastTheme.Light, "test", "test"),
                "Inventor 2027", () => { });
            try
            {
                Assert.False(notifier.NotifyConnection("TCP:1234"));
                for (var i = 0; i < 25; i++) notifier.UpdateSnapshot(InventorUiSnapshot.Empty);
                Assert.Empty(ToastWindows());
                frame.Show();
                notifier.UpdateSnapshot(new InventorUiSnapshot(true, new WindowInteropHelper(frame).Handle.ToInt64(), 0, BackdropHint.None));
                Assert.True(Wait(() => ToastWindows().Count == 1));
            }
            finally { notifier.Dispose(); frame.Close(); }
        });

    [Fact]
    public void Auto_palette_samples_the_anchor_once_keeps_it_across_restore_and_rethemes_beside()
        => Sta.Run(() =>
        {
            var frame = new Window { Left = 80, Top = 80, Width = 900, Height = 600 };
            ToastHost? host = null;
            var count = 0;
            var rects = new List<PxRect>();
            try
            {
                frame.Show();
                var ui = new InventorUiSnapshot(true, new WindowInteropHelper(frame).Handle.ToInt64(), 0, BackdropHint.None);
                var feed = new ToastFeed(now: () => TimeSpan.Zero);
                var usable = false;
                host = new ToastHost(ToastTheme.Auto, ui, feed, "Inventor 2027", () => { },
                    usable: _ => Volatile.Read(ref usable),
                    sample: rect =>
                    {
                        lock (rects) rects.Add(rect);
                        Interlocked.Increment(ref count);
                        return new Rgb(20, 20, 20);
                    });
                if (feed.Record(Result(1), false)) host.RequestRender();
                Thread.Sleep(250);
                Assert.Equal(0, Volatile.Read(ref count)); // covered frame: no sample
                Volatile.Write(ref usable, true);
                Assert.True(Wait(() => Volatile.Read(ref count) == 1 && ToastWindows().Count == 1));
                for (var i = 2; i <= 20; i++)
                    if (feed.Record(Result(i), true)) host.RequestRender();
                Thread.Sleep(200);
                Assert.Equal(1, Volatile.Read(ref count));
                Volatile.Write(ref usable, false);
                Assert.True(Wait(() => ToastWindows().Count == 0));
                Thread.Sleep(200);
                Assert.Equal(1, Volatile.Read(ref count)); // parked card keeps its palette
                Volatile.Write(ref usable, true);
                Assert.True(Wait(() => ToastWindows().Count == 1));
                Thread.Sleep(200);
                Assert.Equal(1, Volatile.Read(ref count));
                host.Retheme();
                Assert.True(Wait(() => Volatile.Read(ref count) == 2));
                PxRect anchor, beside;
                lock (rects) { anchor = rects[0]; beside = rects[1]; }
                Assert.True(beside.Left > anchor.Left + 50,
                    $"retheme sampled {beside.Left}, which is not beside the card at {anchor.Left}");
                Volatile.Write(ref usable, false);
                Assert.True(Wait(() => ToastWindows().Count == 0));
                host.Retheme();
                Thread.Sleep(200);
                Assert.Equal(2, Volatile.Read(ref count)); // hidden retheme waits for a usable frame
                Volatile.Write(ref usable, true);
                Assert.True(Wait(() => Volatile.Read(ref count) == 3 && ToastWindows().Count == 1));
                lock (rects) Assert.Equal(anchor, rects[2]); // shown again with no card up: sample the anchor
            }
            finally { host?.Shutdown(); frame.Close(); }
        });

    private static bool Wait(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 4000) Thread.Sleep(20);
        return condition();
    }

    private static List<IntPtr> ToastWindows()
    {
        var windows = new List<IntPtr>();
        var pid = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid == pid && IsWindowVisible(hwnd))
            {
                var title = new StringBuilder(128);
                GetWindowText(hwnd, title, title.Capacity);
                if (title.ToString() == "IPT-MCP activity") windows.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
