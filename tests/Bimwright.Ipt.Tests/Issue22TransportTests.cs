using System.IO.Pipes;
using System.Net.Sockets;
using System.Reflection;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Transport;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class Issue22TransportTests
{
    private static string PipeName() => "ipt-issue22-" + Guid.NewGuid().ToString("N");
    private static Thread Listener(object server) => (Thread)server.GetType()
        .GetField("_listenThread", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(server)!;

    [Fact]
    public void Pipe_start_reserves_name_before_reporting_success()
    {
        var name = PipeName();
        using var occupied = new NamedPipeServerStream(name, PipeDirection.InOut, 1);
        using var server = new PipeTransportServer(name, "token");
        Assert.ThrowsAny<IOException>(() => server.Start((_, tcs) => tcs.TrySetResult("{}")));
        Assert.False(server.IsRunning);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Started_transport_cannot_be_started_twice_or_reused_after_stop(bool pipe)
    {
        using ITransportServer server = pipe ? new PipeTransportServer(PipeName(), "token") : new TcpTransportServer("token");
        server.Start((_, tcs) => tcs.TrySetResult("{}"));
        Assert.Throws<InvalidOperationException>(() => server.Start((_, tcs) => tcs.TrySetResult("{}")));
        server.Stop();
        Assert.Throws<InvalidOperationException>(() => server.Start((_, tcs) => tcs.TrySetResult("{}")));
    }

    [Fact]
    public void Pipe_stop_releases_an_attached_idle_reader_and_allows_replacement()
    {
        var name = PipeName();
        using var server = new PipeTransportServer(name, "token");
        server.Start((_, tcs) => tcs.TrySetResult("{}"));
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        client.Connect(2000);
        var writer = new StreamWriter(client) { AutoFlush = true };
        var reader = new StreamReader(client);
        writer.WriteLine("{\"auth_token\":\"token\"}");
        Assert.Equal("{}", reader.ReadLine());
        try
        {
            server.Stop();
            Assert.True(Listener(server).Join(500), "The old pipe listener survived Stop while reading an attached client.");
            using var replacement = new PipeTransportServer(name, "next");
            replacement.Start((_, tcs) => tcs.TrySetResult("{}"));
        }
        finally { client.Dispose(); }
    }

    [Fact]
    public void Tcp_stop_releases_an_attached_idle_reader()
    {
        using var server = new TcpTransportServer("token");
        server.Start((_, tcs) => tcs.TrySetResult("{}"));
        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream) { AutoFlush = true };
        using var reader = new StreamReader(stream);
        writer.WriteLine("{\"auth_token\":\"token\"}");
        Assert.Equal("{}", reader.ReadLine());
        try
        {
            server.Stop();
            Assert.True(Listener(server).Join(500), "The old TCP listener survived Stop while reading an attached client.");
        }
        finally { client.Dispose(); }
    }

    [Fact]
    public void Descriptor_publication_failure_is_observable_and_cannot_start_heartbeat()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-issue22-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(root, "A file cannot be a descriptor directory.");
        try
        {
            using var writer = new TargetDescriptorWriter(root, new TargetDescriptor { InventorYear = 2027, ProcessId = 1 });
            Assert.ThrowsAny<IOException>(() => writer.Start(heartbeatMs: 10));
        }
        finally { File.Delete(root); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stop_cancels_callback_wait_without_running_queued_sta_work(bool pipe)
    {
        using ITransportServer server = pipe ? new PipeTransportServer(PipeName(), "token") : new TcpTransportServer("token");
        Action? posted = null;
        var stats = new StaQueueStats();
        using var queue = new StaWorkQueue(action => posted = action, stats);
        using var entered = new ManualResetEventSlim();
        var ran = false;
        server.Start((_, tcs) =>
        {
            var operation = queue.InvokeAsync(() => { ran = true; return "{}"; }, server.Stopping);
            entered.Set();
            #pragma warning disable xUnit1031 // This is the production synchronous listener callback seam, not the test runner thread.
            try { operation.Wait(60000, server.Stopping); tcs.TrySetResult(operation.Result); }
            #pragma warning restore xUnit1031
            catch (OperationCanceledException) { tcs.TrySetCanceled(); }
        });
        using var socket = new TcpClient();
        using var named = pipe ? new NamedPipeClientStream(".", ((PipeTransportServer)server).PipeName, PipeDirection.InOut) : null;
        Stream stream;
        if (pipe) { named!.Connect(2000); stream = named; }
        else { socket.Connect("127.0.0.1", ((TcpTransportServer)server).Port); stream = socket.GetStream(); }
        var writer = new StreamWriter(stream) { AutoFlush = true };
        writer.WriteLine("{\"auth_token\":\"token\"}");
        Assert.True(entered.Wait(2000));
        server.Stop();
        Assert.True(Listener(server).Join(100));
        posted!();
        Assert.False(ran);
        Assert.Equal(0, stats.PendingCommands);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stop_releases_a_partial_request_without_dispatch(bool pipe)
    {
        using ITransportServer server = pipe ? new PipeTransportServer(PipeName(), "token") : new TcpTransportServer("token");
        var dispatches = 0;
        server.Start((_, tcs) => { Interlocked.Increment(ref dispatches); tcs.TrySetResult("{}"); });
        using var socket = new TcpClient();
        using var named = pipe ? new NamedPipeClientStream(".", ((PipeTransportServer)server).PipeName, PipeDirection.InOut) : null;
        Stream stream;
        if (pipe) { named!.Connect(2000); stream = named; }
        else { socket.Connect("127.0.0.1", ((TcpTransportServer)server).Port); stream = socket.GetStream(); }
        stream.Write(new byte[] { (byte)'{' }, 0, 1);
        server.Stop();
        Assert.True(Listener(server).Join(100));
        Assert.Equal(0, dispatches);
    }

    [Fact]
    public void Old_descriptor_writer_cannot_delete_a_replacement_or_resurrect_after_stop()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-issue22-descriptor-" + Guid.NewGuid().ToString("N"));
        var descriptor = new TargetDescriptor { InventorYear = 2027, ProcessId = 1, AuthToken = "first" };
        var old = new TargetDescriptorWriter(root, descriptor);
        try
        {
            old.Start(heartbeatMs: 10);
            using var replacement = new TargetDescriptorWriter(root,
                new TargetDescriptor { InventorYear = 2027, ProcessId = 1, AuthToken = "second" });
            replacement.Start(heartbeatMs: Timeout.Infinite);
            old.WriteOnce();
            Assert.Contains("second", File.ReadAllText(replacement.Path_));
            old.Dispose();
            Assert.Contains("second", File.ReadAllText(replacement.Path_));
            replacement.Dispose();
            old.WriteOnce();
            Assert.False(File.Exists(replacement.Path_));
        }
        finally { old.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Pipe_peer_that_does_not_drain_response_is_released_without_retrying_execution()
    {
        var name = PipeName();
        using var server = new PipeTransportServer(name, "token");
        var executions = 0;
        server.Start((_, tcs) => { Interlocked.Increment(ref executions); tcs.TrySetResult(new string('x', 8 * 1024 * 1024)); });
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        client.Connect(2000);
        var writer = new StreamWriter(client) { AutoFlush = true };
        writer.WriteLine("{\"auth_token\":\"token\"}");
        Assert.True(SpinWait.SpinUntil(() => server.IsClientConnected, 2000));
        Assert.True(SpinWait.SpinUntil(() => !server.IsClientConnected, 7000));
        Assert.Equal(1, executions);
        using var next = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        next.Connect(2000);
        server.Stop();
        Assert.True(Listener(server).Join(100));
    }

    [Fact]
    public async Task Sta_admission_is_bounded_and_timeout_does_not_cancel_a_running_operation()
    {
        Action? posted = null;
        var stats = new StaQueueStats();
        using var queue = new StaWorkQueue(action => posted = action, stats, capacity: 1);
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var operation = queue.InvokeAsync(() => { entered.Set(); release.Wait(); return "completed once"; }, cancellation.Token);
        Assert.Throws<InvalidOperationException>(() => { _ = queue.InvokeAsync(() => "never"); });
        var run = Task.Run(() => posted!());
        try
        {
            Assert.True(entered.Wait(2000));
            cancellation.Cancel();
            queue.Dispose();
            Assert.False(operation.IsCompleted);
            Assert.Equal(1, stats.ExecutingCommands);
        }
        finally { release.Set(); }
        await run;
        Assert.Equal("completed once", await operation);
        Assert.Equal(0, stats.PendingCommands);
    }

    [Fact]
    public void Canceled_sta_work_keeps_its_native_post_slot_until_callback_drains()
    {
        var posted = new List<Action>();
        var stats = new StaQueueStats();
        using var queue = new StaWorkQueue(posted.Add, stats, capacity: 2);
        for (var i = 0; i < 2; i++)
        {
            using var cancellation = new CancellationTokenSource();
            var task = queue.InvokeAsync<string>(() => throw new InvalidOperationException("Canceled work must never execute."), cancellation.Token);
            cancellation.Cancel();
            Assert.True(task.IsCanceled);
        }
        for (var i = 0; i < 100; i++)
            Assert.Throws<InvalidOperationException>(() => { _ = queue.InvokeAsync(() => "never posted"); });
        Assert.Equal(2, posted.Count);
        Assert.Null(queue.TryInvokeAsync(() => "health must use safe busy counters without posting"));
        Assert.Equal(2, posted.Count);
        Assert.Equal(0, stats.PendingCommands);
        posted[0]();
        var admitted = queue.InvokeAsync(() => "new slot after drain");
        posted[2]();
        Assert.True(admitted.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Canceled_health_is_removed_once_from_stats_while_other_work_remains_running()
    {
        var posted = new List<Action>();
        var stats = new StaQueueStats();
        using var queue = new StaWorkQueue(posted.Add, stats, capacity: 2);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var running = queue.InvokeAsync(() => { entered.Set(); release.Wait(); return "done"; });
        var run = Task.Run(() => posted[0]());
        try
        {
            Assert.True(entered.Wait(2000));
            using var healthDeadline = new CancellationTokenSource();
            var health = queue.InvokeAsync(() => "must not execute", healthDeadline.Token);
            Assert.Equal(2, stats.PendingCommands);
            healthDeadline.Cancel();
            Assert.True(health.IsCanceled);
            Assert.Equal(1, stats.PendingCommands); // Busy fallback must not subtract the canceled health slot again.
            Assert.Equal(1, stats.ExecutingCommands);
            posted[1]();
        }
        finally { release.Set(); }
        await run;
        Assert.Equal("done", await running);
    }
}
