using System.Collections.Generic;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Transport;

namespace Bimwright.Ipt.Tests;

public sealed class ConnectionWatchTests
{
    private sealed class FakeTransport : ITransportServer
    {
        public bool Connected;
        public string Info = "Pipe:BimwrightInventor-1234";
        public bool ThrowOnRead;

        public bool IsClientConnected => ThrowOnRead ? throw new System.InvalidOperationException() : Connected;
        public string ConnectionInfo => Info;
        public System.DateTime? LastCommandTime => null;

        public void Start(System.Action<string, TaskCompletionSource<string>> onRequest) { }
        public void Stop() { }
        public void Dispose() { }
    }

    private static (ConnectionWatch watch, List<string> infos) Start(FakeTransport transport)
    {
        var infos = new List<string>();
        // A huge interval: tests drive Poll() directly, the timer never ticks in time.
        return (new ConnectionWatch(transport, infos.Add, intervalMs: 60_000), infos);
    }

    [Fact]
    public void First_attach_fires_once_with_connection_info()
    {
        var t = new FakeTransport { Connected = true };
        var (watch, infos) = Start(t);

        watch.Poll();

        Assert.Equal(new[] { "Pipe:BimwrightInventor-1234" }, infos);
        watch.Dispose();
    }

    [Fact]
    public void Steady_state_never_refires()
    {
        var t = new FakeTransport { Connected = true };
        var (watch, infos) = Start(t);

        watch.Poll();
        watch.Poll();
        watch.Poll();

        Assert.Single(infos);
        watch.Dispose();
    }

    [Fact]
    public void Disconnect_then_reattach_fires_again()
    {
        var t = new FakeTransport { Connected = true };
        var (watch, infos) = Start(t);
        watch.Poll();

        t.Connected = false;
        watch.Poll();
        Assert.Single(infos);   // the drop itself is quiet

        t.Info = "TCP:50011";
        t.Connected = true;
        watch.Poll();

        Assert.Equal(2, infos.Count);
        Assert.Equal("TCP:50011", infos[1]);
        watch.Dispose();
    }

    [Fact]
    public void A_disconnected_start_reports_nothing()
    {
        var t = new FakeTransport { Connected = false };
        var (watch, infos) = Start(t);

        watch.Poll();
        watch.Poll();

        Assert.Empty(infos);
        watch.Dispose();
    }

    [Fact]
    public void A_throwing_transport_or_callback_never_escapes()
    {
        var t = new FakeTransport { Connected = true, ThrowOnRead = true };
        var watch = new ConnectionWatch(t, _ => throw new System.Exception("boom"), intervalMs: 60_000);

        watch.Poll();           // transport throw: swallowed
        t.ThrowOnRead = false;
        watch.Poll();           // callback throw: swallowed, edge still consumed
        watch.Poll();

        watch.Dispose();
    }
}
