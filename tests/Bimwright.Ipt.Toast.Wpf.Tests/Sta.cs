using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Bimwright.Ipt.Toast.Wpf.Tests;

/// <summary>
/// WPF objects only work on an STA thread with a Dispatcher. xUnit runs tests on a
/// thread-pool thread, so every test body hops through here. Pump() turns the
/// dispatcher so DispatcherTimers, render callbacks and animation clocks actually tick.
/// </summary>
internal static class Sta
{
    public static void Run(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { error = e; }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Let the dispatcher run for a wall-clock slice.</summary>
    public static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Pump until the condition holds or the budget is spent; returns whether it held.</summary>
    public static bool PumpUntil(Func<bool> condition, int budgetMs)
    {
        var left = budgetMs;
        while (left > 0 && !condition())
        {
            var slice = Math.Min(50, left);
            Pump(slice);
            left -= slice;
        }
        return condition();
    }
}
