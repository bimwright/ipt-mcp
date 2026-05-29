using System;
using System.Diagnostics;
using Bimwright.Inventor.Shared.Contracts;

namespace Bimwright.Inventor.Shared.Transport;

/// <summary>
/// Picks and constructs the per-version transport and builds its <see cref="TargetDescriptor"/>:
/// TCP for Inventor 2022-2024 (.NET Framework 4.8 add-ins), Named Pipe for 2025-2027.
///
/// API-FREE on purpose: it never touches the Inventor API, so it compiles into the net8 test
/// project. The caller (the add-in entrypoint, which holds the <c>Inventor.Application</c>) fills
/// the descriptor's document title/path afterwards and persists it via
/// <see cref="TargetDescriptorWriter"/>.
///
/// Contract: the returned <see cref="ITransportServer"/> is <b>not</b> started — the caller wires the
/// request callback and calls <see cref="ITransportServer.Start"/>. For TCP the listener binds an
/// OS-assigned port at <c>Start</c>, so <see cref="TargetDescriptor.Port"/> is populated lazily; this
/// factory therefore starts the TCP listener (to read back the bound port) but leaves the
/// <c>onRequest</c> callback for the caller — see <see cref="Create(int, string, out TargetDescriptor)"/>.
/// </summary>
public static class TransportFactory
{
    /// <summary>Year threshold: years &lt;= this use TCP; years above use a Named Pipe.</summary>
    public const int LastTcpYear = 2024;

    /// <summary>
    /// Creates the transport for <paramref name="year"/> and builds its descriptor (without document
    /// title/path — the caller fills those). The returned transport is <b>unstarted</b>; the caller
    /// supplies the request callback to <see cref="ITransportServer.Start"/>.
    ///
    /// Because a TCP listener's port is only known after <c>Start</c>, callers that need the port in
    /// the descriptor before starting their own callback should instead pass year &gt;= 2025 (pipe,
    /// where the name is known up front) or read <see cref="TcpTransportServer.Port"/> after starting.
    /// To keep the descriptor port authoritative regardless, use
    /// <see cref="CreateStarted(int, string, Action{string, System.Threading.Tasks.TaskCompletionSource{string}}, out TargetDescriptor)"/>.
    /// </summary>
    public static ITransportServer Create(int year, string descriptorDir, out TargetDescriptor descriptor)
    {
        if (descriptorDir is null) throw new ArgumentNullException(nameof(descriptorDir));

        var token = AuthToken.Generate();
        var pid = Process.GetCurrentProcess().Id;

        ITransportServer server;
        string transportKind;
        int port = 0;
        string? pipeName = null;

        if (year <= LastTcpYear)
        {
            var tcp = new TcpTransportServer(token);
            // Bind now (OS-assigned port 0) so the descriptor can carry the real port. The caller
            // wires its callback by calling Start(onRequest) which is idempotent w.r.t. binding only
            // when not yet started — so we DON'T start here; the caller starts it. The port is read
            // back by CreateStarted, or by the caller after Start. For the descriptor we leave port=0
            // here and rely on the caller to refresh it post-Start (CreateStarted does this).
            server = tcp;
            transportKind = "tcp";
        }
        else
        {
            pipeName = $"BimwrightInventor-{pid}";
            server = new PipeTransportServer(pipeName, token);
            transportKind = "pipe";
        }

        descriptor = new TargetDescriptor
        {
            TargetId = $"inventor-{year}-{pid}",
            InventorYear = year,
            ProcessId = pid,
            HostApp = "Inventor",
            Transport = transportKind,
            Port = port,
            PipeName = pipeName,
            AuthToken = token,
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };

        return server;
    }

    /// <summary>
    /// Convenience overload that creates the transport, starts it with the supplied callback, and
    /// (for TCP) reads back the OS-assigned bound port into the descriptor so it is authoritative.
    /// Use this from the add-in entrypoint. Still API-free.
    /// </summary>
    public static ITransportServer CreateStarted(
        int year,
        string descriptorDir,
        Action<string, System.Threading.Tasks.TaskCompletionSource<string>> onRequest,
        out TargetDescriptor descriptor)
    {
        if (onRequest is null) throw new ArgumentNullException(nameof(onRequest));

        var server = Create(year, descriptorDir, out descriptor);
        server.Start(onRequest);

        if (server is TcpTransportServer tcp)
            descriptor.Port = tcp.Port;

        return server;
    }
}
