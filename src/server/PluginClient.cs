using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

public sealed class InventorGatewayException : Exception
{
    public string Code { get; }
    public InventorGatewayException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// Server-side transport client. Discovers live add-in targets via <see cref="TargetRegistry"/>
/// and sends NDJSON command envelopes over the per-target transport: TCP for Inventor 2022-2024,
/// Named Pipe for 2025-2027. Ported from nwd-mcp's PluginClient, adapted to branch on
/// <see cref="TargetDescriptor.Transport"/>.
/// </summary>
public sealed class PluginClient
{
    private readonly InventorMcpConfig _config;
    private readonly TargetRegistry _registry;
    private TargetDescriptor? _current;

    public PluginClient(InventorMcpConfig config)
    {
        _config = config;
        _registry = new TargetRegistry(config.DescriptorDirectory);
    }

    public IReadOnlyList<TargetDescriptor> ListTargets() => _registry.List();

    public TargetDescriptor? CurrentTarget
    {
        get
        {
            var live = _registry.List();
            if (_current is not null && live.Any(t => t.TargetId == _current.TargetId)) return _current;
            _current = (_config.TargetId is { } id ? live.FirstOrDefault(t => t.TargetId == id) : null) ?? live.FirstOrDefault();
            return _current;
        }
    }

    public bool SwitchTarget(string targetId)
    {
        var key = (targetId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;

        var live = _registry.List();
        TargetDescriptor? match = live.FirstOrDefault(t =>
            string.Equals(t.TargetId, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.PipeName, key, StringComparison.OrdinalIgnoreCase));

        if (match is null && int.TryParse(key, out var numeric))
        {
            match = numeric is >= 2022 and <= 2027
                ? live.FirstOrDefault(t => t.InventorYear == numeric)
                : live.FirstOrDefault(t => t.ProcessId == numeric);
        }

        if (match is null) return false;
        _current = match;
        return true;
    }

    /// <summary>
    /// Per-call STA budget (spec F2-b): the envelope carries <paramref name="timeoutMs"/> and the
    /// add-in's <c>task.Wait</c> is the single owner of the TIMEOUT decision. The transport waits
    /// an extra 5 s so the add-in's own TIMEOUT response reaches the client first.
    /// </summary>
    internal static int ResolveEffectiveTimeout(int configuredMs, int? requestedMs)
    {
        var t = requestedMs ?? configuredMs;
        if (t < 1) t = 1;
        return t > 600_000 ? 600_000 : t;
    }

    private const int TransportGraceMs = 5000;

    /// <param name="logParams">What the journal records instead of <paramref name="parameters"/> — send_code
    /// passes its params with module bodies replaced by name+hash, so helper code is not re-logged on every call.</param>
    public async Task<JToken> SendAsync(string command, object parameters, CancellationToken ct, int? timeoutMs = null,
        JObject? logParams = null)
    {
        // LogStart before target resolution so NO_TARGET calls still leave a journal entry (spec F1-R1).
        var requestId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();
        var @params = parameters as JObject ?? JObject.FromObject(parameters);
        ServerLogger.LogStart(requestId, command, logParams ?? @params);
        var ok = false;
        string? err = null;
        string? errCode = null;
        string? targetId = null;
        long? responseBytes = null;
        long? pluginDurationMs = null;
        JToken? data = null;
        try
        {
            var target = CurrentTarget ?? throw new InventorGatewayException(
                InventorErrorCodes.NO_TARGET,
                "No live Inventor target. Start Inventor with the bimwright add-in loaded.");
            targetId = target.TargetId;

            var effectiveTimeoutMs = ResolveEffectiveTimeout(_config.TimeoutMs, timeoutMs);
            var env = new InventorCommandEnvelope
            {
                Id = new Guid(requestId),
                Command = command,
                Params = @params,
                TimeoutMs = effectiveTimeoutMs,
                AuthToken = target.AuthToken,
                ReadOnly = _config.ReadOnly,
                SpillRetentionHours = _config.SpillRetentionHours,
                Tool = ToolCatalog.ForCommand(command, effectiveTimeoutMs)
            };

            var line = JsonConvert.SerializeObject(env) + "\n";
            var response = await SendLineAsync(target, line, ct, effectiveTimeoutMs);
            responseBytes = Encoding.UTF8.GetByteCount(response);

            var result = JsonConvert.DeserializeObject<InventorCommandResult>(response)
                         ?? throw new InventorGatewayException(InventorErrorCodes.API_ERROR, "unparseable response");
            // "meta": null in the wire JSON overrides the initializer, so guard before reading it.
            targetId = result.Meta?.TargetId ?? targetId;
            pluginDurationMs = result.Meta?.DurationMs;
            data = result.Data;
            if (!result.Ok)
            {
                var code = result.Error?.Code ?? InventorErrorCodes.API_ERROR;
                var message = result.Error?.Message ?? "unknown error";
                err = code + ": " + message;
                errCode = code;
                // Drawing failures may contain rollback/partial-file effects. Preserve them
                // for the caller while the journal still records a failed invocation.
                if (DrawingInput.Commands.Contains(command) && result.Data is JObject drawing && drawing.Value<bool?>("ok") == false)
                    return drawing;
                throw new InventorGatewayException(code, message);
            }

            ok = true;
            return result.Data ?? JValue.CreateNull();
        }
        catch (Exception ex)
        {
            err ??= ex.Message;
            errCode = (ex as InventorGatewayException)?.Code;
            throw;
        }
        finally
        {
            sw.Stop();
            ServerLogger.LogFinish(requestId, command, ok, sw.ElapsedMilliseconds, err, errCode, targetId,
                responseBytes, pluginDurationMs, data);
        }
    }

    private async Task<string> SendLineAsync(TargetDescriptor target, string line, CancellationToken ct, int timeoutMs)
    {
        Stream stream;
        IDisposable owner;

        // Connect is transport setup, not the STA budget — keep a floor so a pathological
        // timeout_ms (clamped to 1) cannot starve the loopback handshake.
        var connectBudgetMs = Math.Max(timeoutMs, TransportGraceMs);

        if (string.Equals(target.Transport, "pipe", StringComparison.OrdinalIgnoreCase))
        {
            var pipe = new NamedPipeClientStream(".", target.PipeName ?? "", PipeDirection.InOut);
            owner = pipe;
            try
            {
                var connect = pipe.ConnectAsync(ct);
                if (await Task.WhenAny(connect, Task.Delay(connectBudgetMs, ct)) != connect)
                    throw new InventorGatewayException(InventorErrorCodes.TIMEOUT, $"connect to target {target.TargetId} timed out");
                await connect;
            }
            catch (InventorGatewayException) { pipe.Dispose(); throw; }
            catch (Exception ex)
            {
                pipe.Dispose();
                throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, $"cannot reach target {target.TargetId}: {ex.Message}");
            }
            stream = pipe;
        }
        else // default: tcp
        {
            var client = new TcpClient();
            owner = client;
            try
            {
                var connect = client.ConnectAsync("127.0.0.1", target.Port);
                if (await Task.WhenAny(connect, Task.Delay(connectBudgetMs, ct)) != connect)
                    throw new InventorGatewayException(InventorErrorCodes.TIMEOUT, $"connect to target {target.TargetId} timed out");
                await connect;
            }
            catch (InventorGatewayException) { client.Dispose(); throw; }
            catch (Exception ex)
            {
                client.Dispose();
                throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, $"cannot reach target {target.TargetId}: {ex.Message}");
            }
            stream = client.GetStream();
        }

        using (owner)
        using (stream)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line), ct);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            // Grace: the add-in declares TIMEOUT at timeoutMs; wait a little longer so its
            // error response (which carries the useful send_code guidance) reaches us first.
            var readTask = NdjsonLineReader.ReadLineBoundedAsync(reader, _config.MaxResponseBytes);
            if (await Task.WhenAny(readTask, Task.Delay(timeoutMs + TransportGraceMs, ct)) != readTask)
                throw new InventorGatewayException(InventorErrorCodes.TIMEOUT,
                    $"add-in did not respond within {timeoutMs + TransportGraceMs} ms (command budget {timeoutMs} ms)");
            var read = await readTask;
            if (read.Overflow)
                throw new InventorGatewayException(InventorErrorCodes.RESPONSE_TOO_LARGE, $"add-in response exceeded {_config.MaxResponseBytes} bytes");
            return read.Line ?? throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, "add-in closed the connection");
        }
    }
}
