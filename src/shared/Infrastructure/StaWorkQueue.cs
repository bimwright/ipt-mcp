using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Admission and cancellation before STA execution; a running COM operation is never aborted.</summary>
public sealed class StaWorkQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<Action> _post;
    private readonly StaQueueStats _stats;
    private readonly int _capacity;
    private readonly HashSet<Item> _items = new();
    private bool _disposed;

    private abstract class Item
    {
        internal int State; // 0 queued, 1 executing, 2 released
        internal CancellationTokenRegistration Cancellation;
        internal abstract void Execute();
        internal abstract void Cancel();
        internal abstract void Fail(Exception error);
    }

    private sealed class Item<T> : Item
    {
        internal readonly TaskCompletionSource<T> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Func<T> Work;
        internal Item(Func<T> work) => Work = work;
        internal override void Execute() { try { Completion.TrySetResult(Work()); } catch (Exception ex) { Completion.TrySetException(ex); } }
        internal override void Cancel() => Completion.TrySetCanceled();
        internal override void Fail(Exception error) => Completion.TrySetException(error);
    }

    public StaWorkQueue(Action<Action> post, StaQueueStats stats, int capacity = 64)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    }

    public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellation = default)
        => TryInvokeAsync(work, cancellation) ?? throw new InvalidOperationException("Inventor STA queue is full; no operation was started.");

    /// <summary>Null means admission was refused before posting; health can answer from safe counters.</summary>
    public Task<T>? TryInvokeAsync<T>(Func<T> work, CancellationToken cancellation = default)
    {
        var item = new Item<T>(work ?? throw new ArgumentNullException(nameof(work)));
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StaWorkQueue));
            if (_items.Count >= _capacity) return null;
            cancellation.ThrowIfCancellationRequested();
            _items.Add(item);
            _stats.OnQueued();
        }
        item.Cancellation = cancellation.Register(() => Abandon(item, null));
        try { _post(() => Run(item)); }
        catch (Exception ex)
        {
            Abandon(item, ex);
            lock (_gate) _items.Remove(item); // A failed post has no native callback left to drain.
            item.Cancellation.Dispose();
        }
        return item.Completion.Task;
    }

    private void Abandon(Item item, Exception? error)
    {
        lock (_gate)
        {
            if (item.State != 0) return;
            item.State = 2;
            // Keep its admission slot until the posted callback drains. Cancellation cannot remove
            // a WinForms BeginInvoke message, so releasing here would allow an unbounded native queue.
            _stats.OnAbandoned();
            if (error == null) item.Cancel(); else item.Fail(error);
        }
    }

    private void Run(Item item)
    {
        bool start;
        lock (_gate)
        {
            start = item.State == 0;
            if (start) { item.State = 1; _stats.OnStarted(); }
            else _items.Remove(item);
        }
        item.Cancellation.Dispose();
        if (!start) return;
        try { item.Execute(); }
        finally
        {
            lock (_gate)
            {
                item.State = 2;
                _items.Remove(item);
                _stats.OnCompleted();
            }
        }
    }

    public void Dispose()
    {
        Item[] items;
        lock (_gate) { _disposed = true; items = new Item[_items.Count]; _items.CopyTo(items); }
        foreach (var item in items) { Abandon(item, null); item.Cancellation.Dispose(); }
    }
}
