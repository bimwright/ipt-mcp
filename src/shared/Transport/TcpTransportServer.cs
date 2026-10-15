using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Transport;

/// <summary>
/// Loopback NDJSON TCP listener for Inventor 2022-2024 (.NET Framework 4.8 add-ins).
/// Ported from rvt-mcp's TcpTransportServer with the descriptor-based auth model:
/// the expected token is supplied to the constructor (it lives in the TargetDescriptor),
/// not discovered from a global file. Validates each envelope's <c>auth_token</c> field,
/// bounds each line to 1 MiB, rate-limits to 20 req / 10 s per connection, and waits up to
/// 60 s for the STA dispatch to complete.
/// </summary>
public sealed class TcpTransportServer : ITransportServer
{
    private readonly string _authToken;
    private TcpListener? _listener;
    private Thread? _listenThread;
    private volatile bool _running;
    private Action<string, TaskCompletionSource<string>>? _onRequest;
    private int _port;
    private volatile bool _clientConnected;
    private readonly object _lifecycle = new();
    private readonly object _clientGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _stopping;
    private TcpClient? _activeClient;
    private bool _started;
    private bool _stopped;

    private const int MaxLineBytes = 1024 * 1024; // 1 MiB

    /// <param name="authToken">
    /// The token the add-in wrote into its TargetDescriptor. Every incoming envelope's
    /// <c>auth_token</c> must match this value.
    /// </param>
    public TcpTransportServer(string authToken)
    {
        _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
        _stopping = _stop.Token;
    }

    public bool IsRunning => _running;
    public CancellationToken Stopping => _stopping;
    public int Port => _port;
    public bool IsClientConnected => _clientConnected;
    public DateTime? LastCommandTime { get; private set; }
    public string ConnectionInfo => $"TCP:{_port}";

    public void Start(Action<string, TaskCompletionSource<string>> onRequest)
    {
        if (onRequest == null) throw new ArgumentNullException(nameof(onRequest));
        lock (_lifecycle)
        {
            if (_started || _stopped) throw new InvalidOperationException("Create a new transport for each listener lifetime.");
            _started = true;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start(32);
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _onRequest = onRequest;
                _running = true;
                _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "BimwrightInventor.TcpTransportServer" };
                _listenThread.Start();
            }
            catch
            {
                _running = false;
                _stopped = true;
                _stop.Cancel();
                _listener?.Stop();
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
            try { _listener?.Stop(); } catch { }
            lock (_clientGate) { try { _activeClient?.Close(); } catch { } }
            thread = _listenThread;
        }
        if (thread != null && thread.IsAlive && Thread.CurrentThread != thread && !thread.Join(2000))
            throw new TimeoutException("The Inventor TCP listener has not stopped; do not replace its lifetime.");
    }

    public void Dispose() { Stop(); _stop.Dispose(); }

    private void ListenLoop()
    {
        while (_running)
        {
            TcpClient? client = null;
            try
            {
                client = _listener!.AcceptTcpClient();
                lock (_clientGate)
                {
                    if (!_running) { client.Close(); break; }
                    _activeClient = client;
                }
                HandleClient(client);
            }
            catch (SocketException) when (!_running)
            {
                break; // clean shutdown
            }
            catch
            {
                // A broken listener ends this lifetime; never retry a permanent accept error in a tight loop.
                break;
            }
            finally
            {
                try { client?.Close(); } catch { }
                lock (_clientGate) { if (ReferenceEquals(_activeClient, client)) _activeClient = null; }
            }
        }
        _running = false;
        _clientConnected = false;
        try { _stop.Cancel(); } catch { }
    }

    private void HandleClient(TcpClient client)
    {
        client.ReceiveTimeout = 120000;
        client.SendTimeout = 5000;
        try
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            var requestTimestamps = new Queue<DateTime>();
            const int RateLimitMax = 20;
            var rateLimitWindow = TimeSpan.FromSeconds(10);

            while (_running && client.Connected)
            {
                string? line;
                try
                {
                    line = NdjsonLineReader.ReadLineBounded(reader, MaxLineBytes, out bool overflow);
                    if (overflow)
                    {
                        TryWrite(writer, ErrorJson(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "Request exceeded 1 MiB size limit."));
                        break;
                    }
                    if (line == null) break; // client disconnected
                }
                catch (IOException)
                {
                    break; // receive timeout or broken pipe
                }
                catch
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                JObject request;
                try { request = JObject.Parse(line); }
                catch { continue; }

                var id = ParseId(request);
                var token = request.Value<string>("auth_token");
                if (!AuthToken.Verify(_authToken, token))
                {
                    TryWrite(writer, ErrorJson(id, InventorErrorCodes.UNAUTHORIZED, "Invalid or missing authorization token."));
                    break; // drop the connection on auth failure
                }
                _clientConnected = true;   // authenticated attach — ConnectionWatch fires on this edge

                var now = DateTime.UtcNow;
                while (requestTimestamps.Count > 0 && (now - requestTimestamps.Peek()) > rateLimitWindow)
                    requestTimestamps.Dequeue();
                if (requestTimestamps.Count >= RateLimitMax)
                {
                    TryWrite(writer, ErrorJson(id, InventorErrorCodes.API_ERROR, "Rate limit: 20 requests / 10 seconds per connection."));
                    break;
                }
                requestTimestamps.Enqueue(now);

                LastCommandTime = DateTime.Now;
                var tcs = new TaskCompletionSource<string>();
                _onRequest!(line, tcs);

                string response;
                if (tcs.Task.Wait(60000, Stopping))
                    response = tcs.Task.Result;
                else
                    response = ErrorJson(id, InventorErrorCodes.TIMEOUT, "Request timed out (60s). Inventor may be in a modal dialog or busy.");

                if (!TryWrite(writer, response)) break;
            }
        }
        catch
        {
            // client disconnect or IO error
        }
        finally
        {
            _clientConnected = false;
        }
    }

    private static Guid ParseId(JObject request)
        => Guid.TryParse(request.Value<string>("id"), out var g) ? g : Guid.Empty;

    private static bool TryWrite(StreamWriter writer, string text)
    {
        try { writer.WriteLine(text); return true; }
        catch { return false; }
    }

    private static string ErrorJson(Guid id, string code, string message)
        => JsonConvert.SerializeObject(InventorCommandResult.Fail(id, code, message, new InventorResponseMeta()));
}
