using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Transport;

/// <summary>
/// Named-pipe NDJSON listener for Inventor 2025-2027 (.NET 8 / .NET 10 add-ins). Avoids the
/// loopback-firewall prompt UX on modern Windows. Ported from rvt-mcp's PipeTransportServer with
/// the descriptor-based auth model: both the pipe name and the expected token are supplied to the
/// constructor (they live in the TargetDescriptor written by the add-in). Validates each envelope's
/// <c>auth_token</c> field, bounds each line to 1 MiB, rate-limits to 20 req / 10 s per connection,
/// and waits up to 60 s for the STA dispatch to complete.
/// Serves up to <see cref="MaxConnections"/> clients at once, each on its own thread: a call that holds
/// its connection for minutes (a long send_code) must not stop health or the next call from connecting
/// (13/11 log: every later call failed with "connect timed out" until the script ended).
/// </summary>
public sealed class PipeTransportServer : ITransportServer
{
    private readonly string _pipeName;
    private readonly string _authToken;
    private Thread? _listenThread;
    private volatile bool _running;
    private Action<string, TaskCompletionSource<string>>? _onRequest;
    private int _authenticatedClients;
    private readonly object _lifecycle = new();
    private readonly object _clientGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _stopping;
    private NamedPipeServerStream? _activePipe;   // the instance waiting for the next client
    private readonly HashSet<NamedPipeServerStream> _connected = new();
    private readonly SemaphoreSlim _instances = new(MaxConnections, MaxConnections);
    private bool _started;
    private bool _stopped;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafePipeHandle handle, IntPtr overlapped);

    private const int MaxLineBytes = 1024 * 1024; // 1 MiB

    /// <summary>Pipe instances (connected clients plus the one waiting to accept).</summary>
    internal const int MaxConnections = 4;

    /// <param name="pipeName">The pipe name the add-in wrote into its TargetDescriptor.</param>
    /// <param name="authToken">
    /// The token the add-in wrote into its TargetDescriptor. Every incoming envelope's
    /// <c>auth_token</c> must match this value.
    /// </param>
    public PipeTransportServer(string pipeName, string authToken)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
        _stopping = _stop.Token;
    }

    public bool IsRunning => _running;
    public CancellationToken Stopping => _stopping;
    public string PipeName => _pipeName;
    public bool IsClientConnected => Volatile.Read(ref _authenticatedClients) > 0;
    public DateTime? LastCommandTime { get; private set; }
    public string ConnectionInfo => $"Pipe:{_pipeName}";

    public void Start(Action<string, TaskCompletionSource<string>> onRequest)
    {
        if (onRequest == null) throw new ArgumentNullException(nameof(onRequest));
        lock (_lifecycle)
        {
            if (_started || _stopped) throw new InvalidOperationException("Create a new transport for each listener lifetime.");
            _started = true;
            try
            {
                _instances.Wait();
                _activePipe = CreatePipe(); // Reserve the endpoint before returning success or publishing discovery.
                _onRequest = onRequest;
                _running = true;
                _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "BimwrightInventor.PipeTransportServer" };
                _listenThread.Start();
            }
            catch
            {
                _running = false;
                _stopped = true;
                _stop.Cancel();
                ClosePipe(_activePipe);
                _activePipe = null;
                throw;
            }
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            _running = false;
            if (!_stopped) { _stopped = true; _stop.Cancel(); }
            lock (_clientGate)
            {
                ClosePipe(_activePipe);
                foreach (var pipe in _connected) ClosePipe(pipe);
            }
            thread = _listenThread;
        }
        if (thread != null && thread.IsAlive && Thread.CurrentThread != thread && !thread.Join(2000))
            throw new TimeoutException("The Inventor pipe listener has not stopped; do not replace its lifetime.");
    }

    public void Dispose() { Stop(); _stop.Dispose(); }   // _instances is not disposed: a late client thread still releases its slot

    private NamedPipeServerStream CreatePipe() => new(_pipeName, PipeDirection.InOut, MaxConnections,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static void ClosePipe(NamedPipeServerStream? pipe)
    {
        if (pipe == null) return;
        try { CancelIoEx(pipe.SafePipeHandle, IntPtr.Zero); } catch { }
        try { pipe.Dispose(); } catch { }
    }

    private void ListenLoop()
    {
        try
        {
            while (_running)
            {
                NamedPipeServerStream? pipe;
                lock (_clientGate) pipe = _activePipe;
                try
                {
                    pipe!.WaitForConnection();
                }
                catch
                {
                    ClosePipe(pipe);
                    lock (_clientGate) { if (ReferenceEquals(_activePipe, pipe)) _activePipe = null; }
                    _instances.Release();
                    throw;
                }
                lock (_clientGate)
                {
                    if (ReferenceEquals(_activePipe, pipe)) _activePipe = null;
                    if (!_running) { ClosePipe(pipe); _instances.Release(); break; }
                    _connected.Add(pipe);
                }
                new Thread(() => ServeClient(pipe)) { IsBackground = true, Name = "BimwrightInventor.PipeClient" }.Start();

                // The next instance needs a free slot; Stop cancels the wait.
                _instances.Wait(_stopping);
                lock (_clientGate)
                {
                    if (!_running) { _instances.Release(); break; }
                    _activePipe = CreatePipe();
                }
            }
        }
        catch { /* Stop, or a failed rebind: end this lifetime instead of spinning on a busy endpoint. */ }
        finally
        {
            _running = false;
            try { _stop.Cancel(); } catch { }
            lock (_clientGate) { ClosePipe(_activePipe); _activePipe = null; }
        }
    }

    private void ServeClient(NamedPipeServerStream pipe)
    {
        try { HandleClient(pipe); }
        catch { /* client disconnect or IO error */ }
        finally
        {
            ClosePipe(pipe);
            lock (_clientGate) _connected.Remove(pipe);
            _instances.Release();
        }
    }

    private void HandleClient(NamedPipeServerStream pipe)
    {
        var authenticated = false;
        try
        {
            var reader = new StreamReader(pipe, Encoding.UTF8);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            long activity = Stopwatch.GetTimestamp();
            int inFlight = 0;
            using var idle = new Timer(_ =>
            {
                if (Volatile.Read(ref inFlight) == 0 && (Stopwatch.GetTimestamp() - Interlocked.Read(ref activity)) / (double)Stopwatch.Frequency >= 120)
                    ClosePipe(pipe);
            }, null, 15000, 15000);

            var requestTimestamps = new Queue<DateTime>();
            const int RateLimitMax = 20;
            var rateLimitWindow = TimeSpan.FromSeconds(10);

            while (_running && pipe.IsConnected)
            {
                string? line;
                try
                {
                    line = NdjsonLineReader.ReadLineBounded(reader, MaxLineBytes, out bool overflow);
                    if (overflow)
                    {
                        TryWrite(pipe, writer, ErrorJson(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "Request exceeded 1 MiB size limit."));
                        break;
                    }
                    if (line == null) break; // client disconnected
                }
                catch (IOException)
                {
                    break; // broken pipe
                }
                catch
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;
                Interlocked.Exchange(ref activity, Stopwatch.GetTimestamp());

                JObject request;
                try { request = JObject.Parse(line); }
                catch { continue; }

                var id = ParseId(request);
                var token = request.Value<string>("auth_token");
                if (!AuthToken.Verify(_authToken, token))
                {
                    TryWrite(pipe, writer, ErrorJson(id, InventorErrorCodes.UNAUTHORIZED, "Invalid or missing authorization token."));
                    break; // drop the connection on auth failure
                }
                if (!authenticated) { authenticated = true; Interlocked.Increment(ref _authenticatedClients); }   // ConnectionWatch fires on this edge

                var now = DateTime.UtcNow;
                while (requestTimestamps.Count > 0 && (now - requestTimestamps.Peek()) > rateLimitWindow)
                    requestTimestamps.Dequeue();
                if (requestTimestamps.Count >= RateLimitMax)
                {
                    TryWrite(pipe, writer, ErrorJson(id, InventorErrorCodes.API_ERROR, "Rate limit: 20 requests / 10 seconds per connection."));
                    break;
                }
                requestTimestamps.Enqueue(now);

                LastCommandTime = DateTime.Now;
                var tcs = new TaskCompletionSource<string>();
                Interlocked.Exchange(ref inFlight, 1);
                try
                {
                    _onRequest!(line, tcs);
                    string response;
                    if (tcs.Task.Wait(60000, Stopping)) response = tcs.Task.Result;
                    else response = ErrorJson(id, InventorErrorCodes.TIMEOUT, "Request timed out (60s). Inventor may be in a modal dialog or busy.");
                    if (!TryWrite(pipe, writer, response)) break;
                }
                finally { Interlocked.Exchange(ref activity, Stopwatch.GetTimestamp()); Interlocked.Exchange(ref inFlight, 0); }
            }
        }
        catch
        {
            // client disconnect or IO error
        }
        finally
        {
            if (authenticated) Interlocked.Decrement(ref _authenticatedClients);
        }
    }

    private static Guid ParseId(JObject request)
        => Guid.TryParse(request.Value<string>("id"), out var g) ? g : Guid.Empty;

    private static bool TryWrite(NamedPipeServerStream peer, StreamWriter writer, string text)
    {
        // A peer that stops reading must not retain its pipe instance indefinitely.
        using var deadline = new Timer(_ => ClosePipe(peer), null, 5000, Timeout.Infinite);
        try { writer.WriteLine(text); return true; }
        catch { return false; }
    }

    private static string ErrorJson(Guid id, string code, string message)
        => JsonConvert.SerializeObject(InventorCommandResult.Fail(id, code, message, new InventorResponseMeta()));
}
