using System;
using System.Diagnostics;

namespace Bimwright.Ipt.Scripting;

/// <summary>
/// Run-time budget for a <c>send_code</c> script. <c>ScriptLoopGuard</c> puts a call to <see cref="Check"/> at the top of
/// every loop body, so a loop stops with a clear error once the script's time is up instead of holding Inventor's STA
/// thread after the call has already returned TIMEOUT. Host-free; per thread, because a script runs on one thread.
/// </summary>
public static class IptGuard
{
    [ThreadStatic] private static long _deadline;   // Stopwatch timestamp; 0 = no budget

    /// <summary>Starts a budget of <paramref name="budgetMs"/> for scripts run on this thread (≤ 0 = none).</summary>
    public static void Begin(long budgetMs) =>
        _deadline = budgetMs > 0 ? Stopwatch.GetTimestamp() + budgetMs * Stopwatch.Frequency / 1000 : 0;

    public static void End() => _deadline = 0;

    /// <summary>Throws <see cref="IptTimeoutException"/> when the budget is spent. Cheap enough for every loop iteration.</summary>
    public static void Check(string source, int line)
    {
        var deadline = _deadline;
        if (deadline != 0 && Stopwatch.GetTimestamp() > deadline)
            throw new IptTimeoutException(source, line);
    }
}

/// <summary>A helper could not do what the script asked; the message names what was found instead.</summary>
public class IptException : Exception
{
    public IptException(string message) : base(message) { }
}

/// <summary>The script ran out of time inside a loop. The add-in rolls the transaction back and frees Inventor.</summary>
public sealed class IptTimeoutException : IptException
{
    public IptTimeoutException(string source, int line)
        : base("script stopped: its time budget ran out " + (line > 0 ? $"inside the loop at {source} line {line}. " : $"inside {source}. ") +
               "The transaction was rolled back. Make the loop smaller (filter first with Ipt.Occs/Ipt.Overlapping, " +
               "or split the work over several calls) or raise timeout_ms.")
    {
        Source = source;
        Line = line;
    }

    public new string Source { get; }
    public int Line { get; }
}
