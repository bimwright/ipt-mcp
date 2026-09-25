using System;
using System.Threading;

namespace Bimwright.Ipt.Shared.Transport;

/// <summary>
/// Rising-edge watch on <see cref="ITransportServer.IsClientConnected"/> (rvt-mcp parity: their
/// IdlingUpdater polls once a second; Inventor has no Idling event, so a small timer does it).
/// The flag only flips after the client's auth token verifies, so each authenticated attach or
/// re-attach fires <c>onAttach</c> exactly once with <see cref="ITransportServer.ConnectionInfo"/>
/// — the proof the agent↔plugin wire works end-to-end. API-free: never touches Inventor.
/// </summary>
public sealed class ConnectionWatch : IDisposable
{
    public const int DefaultIntervalMs = 1000;

    private readonly ITransportServer _server;
    private readonly Action<string> _onAttach;
    private readonly Timer _timer;
    private bool _wasConnected;

    public ConnectionWatch(ITransportServer server, Action<string> onAttach, int intervalMs = DefaultIntervalMs)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _onAttach = onAttach ?? throw new ArgumentNullException(nameof(onAttach));
        _timer = new Timer(_ => Poll(), null, intervalMs, intervalMs);
    }

    /// <summary>One edge check. Public so tests drive it deterministically instead of waiting on the timer.</summary>
    public void Poll()
    {
        bool connected;
        string? info = null;
        try
        {
            connected = _server.IsClientConnected;
            if (connected) info = _server.ConnectionInfo;
        }
        catch
        {
            return;   // transport mid-dispose: skip this tick
        }

        if (connected && !_wasConnected)
        {
            try { _onAttach(info!); }
            catch { }
        }
        _wasConnected = connected;
    }

    public void Dispose()
    {
        try { _timer.Dispose(); } catch { }
    }
}
