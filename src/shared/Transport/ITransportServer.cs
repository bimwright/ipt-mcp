using System;
using System.Threading.Tasks;
using System.Threading;

namespace Bimwright.Ipt.Shared.Transport;

/// <summary>
/// Per-version NDJSON listener abstraction. TCP for Inventor 2022-2024, Named Pipe for 2025-2027.
/// Ported from rvt-mcp's <c>ITransportServer</c>. The <paramref name="onRequest"/> callback receives
/// the raw request line and a <see cref="TaskCompletionSource{TResult}"/> that the add-in completes
/// with the serialized response (after marshalling onto Inventor's STA thread).
/// </summary>
public interface ITransportServer : IDisposable
{
    bool IsRunning { get; }
    /// <summary>Ends listener waits, never an Inventor operation which has already started.</summary>
    CancellationToken Stopping { get; }
    /// <summary>Begins listening. The callback is invoked once per validated request line.</summary>
    void Start(Action<string, TaskCompletionSource<string>> onRequest);

    /// <summary>Stops listening and releases the underlying socket/pipe.</summary>
    void Stop();

    /// <summary>Human-readable description of the bound endpoint (e.g. <c>TCP:49891</c> or <c>Pipe:Bimwright-Inventor-1234</c>).</summary>
    string ConnectionInfo { get; }

    /// <summary>True once the attached client has passed auth, until its connection drops.
    /// A bare socket/pipe attach (or a failed token) never raises it — it signals a verified client.</summary>
    bool IsClientConnected { get; }

    /// <summary>Wall-clock time the last command line was received, or null if none yet.</summary>
    DateTime? LastCommandTime { get; }
}
