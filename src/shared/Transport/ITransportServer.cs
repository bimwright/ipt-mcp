using System;
using System.Threading.Tasks;

namespace Bimwright.Inventor.Shared.Transport;

/// <summary>
/// Per-version NDJSON listener abstraction. TCP for Inventor 2022-2024, Named Pipe for 2025-2027.
/// Ported from rvt-mcp's <c>ITransportServer</c>. The <paramref name="onRequest"/> callback receives
/// the raw request line and a <see cref="TaskCompletionSource{TResult}"/> that the add-in completes
/// with the serialized response (after marshalling onto Inventor's STA thread).
/// </summary>
public interface ITransportServer : IDisposable
{
    /// <summary>Begins listening. The callback is invoked once per validated request line.</summary>
    void Start(Action<string, TaskCompletionSource<string>> onRequest);

    /// <summary>Stops listening and releases the underlying socket/pipe.</summary>
    void Stop();

    /// <summary>Human-readable description of the bound endpoint (e.g. <c>TCP:49891</c> or <c>Pipe:Bimwright-Inventor-1234</c>).</summary>
    string ConnectionInfo { get; }

    /// <summary>True while a client is connected and being served.</summary>
    bool IsClientConnected { get; }

    /// <summary>Wall-clock time the last command line was received, or null if none yet.</summary>
    DateTime? LastCommandTime { get; }
}
