using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
/// </summary>
public sealed class PipeTransportServer : ITransportServer
{
    private readonly string _pipeName;
    private readonly string _authToken;
    private Thread? _listenThread;
    private volatile bool _running;
    private Action<string, TaskCompletionSource<string>>? _onRequest;
    private volatile bool _clientConnected;

    private const int MaxLineBytes = 1024 * 1024; // 1 MiB

    /// <param name="pipeName">The pipe name the add-in wrote into its TargetDescriptor.</param>
    /// <param name="authToken">
    /// The token the add-in wrote into its TargetDescriptor. Every incoming envelope's
    /// <c>auth_token</c> must match this value.
    /// </param>
    public PipeTransportServer(string pipeName, string authToken)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
    }

    public bool IsRunning => _running;
    public string PipeName => _pipeName;
    public bool IsClientConnected => _clientConnected;
    public DateTime? LastCommandTime { get; private set; }
    public string ConnectionInfo => $"Pipe:{_pipeName}";

    public void Start(Action<string, TaskCompletionSource<string>> onRequest)
    {
        _onRequest = onRequest ?? throw new ArgumentNullException(nameof(onRequest));
        _running = true;
        _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "BimwrightInventor.PipeTransportServer" };
        _listenThread.Start();
    }

    public void Stop()
    {
        _running = false;
        // Wake a blocked WaitForConnection by connecting briefly.
        try
        {
            using var dummy = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            dummy.Connect(100);
        }
        catch { }
    }

    public void Dispose() => Stop();

    private void ListenLoop()
    {
        while (_running)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1, // maxNumberOfServerInstances
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);

                pipe.WaitForConnection();
                if (!_running) break;

                HandleClient(pipe);
            }
            catch (IOException) when (!_running)
            {
                break; // clean shutdown
            }
            catch
            {
                // listen error; keep listening
            }
            finally
            {
                try { pipe?.Disconnect(); } catch { }
                try { pipe?.Dispose(); } catch { }
            }
        }
    }

    private void HandleClient(NamedPipeServerStream pipe)
    {
        _clientConnected = true;
        try
        {
            var reader = new StreamReader(pipe, Encoding.UTF8);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };

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
                        TryWrite(writer, ErrorJson(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "Request exceeded 1 MiB size limit."));
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
}
