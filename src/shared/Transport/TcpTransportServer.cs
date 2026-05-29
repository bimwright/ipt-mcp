using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Inventor.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Shared.Transport;

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

    private const int MaxLineBytes = 1024 * 1024; // 1 MiB

    /// <param name="authToken">
    /// The token the add-in wrote into its TargetDescriptor. Every incoming envelope's
    /// <c>auth_token</c> must match this value.
    /// </param>
    public TcpTransportServer(string authToken)
    {
        _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
    }

    public bool IsRunning => _running;
    public int Port => _port;
    public bool IsClientConnected => _clientConnected;
    public DateTime? LastCommandTime { get; private set; }
    public string ConnectionInfo => $"TCP:{_port}";

    public void Start(Action<string, TaskCompletionSource<string>> onRequest)
    {
        _onRequest = onRequest ?? throw new ArgumentNullException(nameof(onRequest));

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _running = true;
        _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "BimwrightInventor.TcpTransportServer" };
        _listenThread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
    }

    public void Dispose() => Stop();

    private void ListenLoop()
    {
        while (_running)
        {
            TcpClient? client = null;
            try
            {
                client = _listener!.AcceptTcpClient();
                HandleClient(client);
            }
            catch (SocketException) when (!_running)
            {
                break; // clean shutdown
            }
            catch
            {
                // accept error; keep listening
            }
            finally
            {
                try { client?.Close(); } catch { }
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        client.ReceiveTimeout = 120000;
        _clientConnected = true;
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
                    line = ReadLineBounded(reader, MaxLineBytes, out bool overflow);
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
                if (tcs.Task.Wait(TimeSpan.FromSeconds(60)))
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

    private static string? ReadLineBounded(StreamReader reader, int maxBytes, out bool overflow)
    {
        overflow = false;
        var sb = new StringBuilder();
        int count = 0;
        while (true)
        {
            int ch = reader.Read();
            if (ch == -1) return sb.Length == 0 ? null : sb.ToString();
            if (ch == '\n') return sb.ToString();
            if (ch == '\r') continue;
            count++;
            if (count > maxBytes) { overflow = true; return null; }
            sb.Append((char)ch);
        }
    }
}
