using System;
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

    public async Task<JToken> SendAsync(string command, object parameters, CancellationToken ct)
    {
        var target = CurrentTarget ?? throw new InventorGatewayException(
            InventorErrorCodes.NO_TARGET,
            "No live Inventor target. Start Inventor with the bimwright add-in loaded.");

        var env = new InventorCommandEnvelope
        {
            Id = Guid.NewGuid(),
            Command = command,
            Params = parameters as JObject ?? JObject.FromObject(parameters),
            TimeoutMs = _config.TimeoutMs,
            AuthToken = target.AuthToken,
            ReadOnly = _config.ReadOnly
        };

        var line = JsonConvert.SerializeObject(env) + "\n";
        var response = await SendLineAsync(target, line, ct);

        var result = JsonConvert.DeserializeObject<InventorCommandResult>(response)
                     ?? throw new InventorGatewayException(InventorErrorCodes.API_ERROR, "unparseable response");
        if (!result.Ok)
            throw new InventorGatewayException(result.Error?.Code ?? InventorErrorCodes.API_ERROR, result.Error?.Message ?? "unknown error");
        return result.Data ?? JValue.CreateNull();
    }

    private async Task<string> SendLineAsync(TargetDescriptor target, string line, CancellationToken ct)
    {
        Stream stream;
        IDisposable owner;

        if (string.Equals(target.Transport, "pipe", StringComparison.OrdinalIgnoreCase))
        {
            var pipe = new NamedPipeClientStream(".", target.PipeName ?? "", PipeDirection.InOut);
            owner = pipe;
            try
            {
                var connect = pipe.ConnectAsync(ct);
                if (await Task.WhenAny(connect, Task.Delay(_config.TimeoutMs, ct)) != connect)
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
                if (await Task.WhenAny(connect, Task.Delay(_config.TimeoutMs, ct)) != connect)
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
            var readTask = NdjsonLineReader.ReadLineBoundedAsync(reader, _config.MaxResponseBytes);
            if (await Task.WhenAny(readTask, Task.Delay(_config.TimeoutMs, ct)) != readTask)
                throw new InventorGatewayException(InventorErrorCodes.TIMEOUT, $"request {target.TargetId} timed out after {_config.TimeoutMs} ms");
            var read = await readTask;
            if (read.Overflow)
                throw new InventorGatewayException(InventorErrorCodes.RESPONSE_TOO_LARGE, $"add-in response exceeded {_config.MaxResponseBytes} bytes");
            return read.Line ?? throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE, "add-in closed the connection");
        }
    }
}
