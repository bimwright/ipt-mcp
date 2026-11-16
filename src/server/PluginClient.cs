using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
    public JToken? ResponseData { get; }
    public InventorGatewayException(string code, string message, JToken? responseData = null) : base(message)
    { Code = code; ResponseData = responseData; }
}

/// <summary>
/// Server-side transport client. Discovers live add-in targets via <see cref="TargetRegistry"/>
/// and sends NDJSON command envelopes over the per-target transport: TCP for Inventor 2022-2024,
/// Named Pipe for 2025-2027. Ported from nwd-mcp's PluginClient, adapted to branch on
/// <see cref="TargetDescriptor.Transport"/>.
/// </summary>
public sealed class PluginClient
{
    private static readonly System.Threading.AsyncLocal<System.Runtime.CompilerServices.StrongBox<bool>?> ForwardedCall = new();
    private static readonly AsyncLocal<string?> ActivityCommand = new();
    internal static System.Runtime.CompilerServices.StrongBox<bool> BeginActivity(string? toolName)
    {
        ActivityCommand.Value = toolName?.StartsWith("inventor_", StringComparison.Ordinal) == true ? toolName.Substring(9) : toolName;
        return ForwardedCall.Value = new System.Runtime.CompilerServices.StrongBox<bool>(false);
    }

    internal async Task ReportServerActivityAsync(string name, object? arguments,
        ModelContextProtocol.Protocol.CallToolResult? result, string? error, long durationMs)
    {
        var target = _binding.Peek(_registry.List());
        if (target == null) return;
        try
        {
            var command = name.StartsWith("inventor_", StringComparison.Ordinal) ? name.Substring(9) : name;
            var text = result?.Content?.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault()?.Text;
            JToken? data = null;
            try { if (text != null) data = JToken.Parse(text); } catch (JsonReaderException) { }
            if (text?.Length > 10240) data = new JObject { ["summary"] = "Output returned to the MCP client", ["response_bytes"] = Encoding.UTF8.GetByteCount(text) };
            var envelope = new InventorCommandEnvelope {
                Id = Guid.NewGuid(), Command = "report_tool_activity", AuthToken = target.AuthToken,
                TimeoutMs = 300, RecordCalls = _config.EnableCallLog,
                Tool = ToolCatalog.ForCommand(command, _config.TimeoutMs),
                Params = new JObject {
                    ["command"] = command, ["ok"] = error == null && result?.IsError != true,
                    ["arguments"] = arguments == null ? null : JToken.FromObject(ServerLogger.MaskParams(arguments)!),
                    ["result"] = data, ["error"] = error, ["duration_ms"] = durationMs
                }
            };
            using var timeout = new CancellationTokenSource(300);
            await SendLineAsync(target, JsonConvert.SerializeObject(envelope) + "\n", timeout.Token, 300);
        }
        catch { /* Activity delivery must not change a server-only tool's result. */ }
    }
    private readonly JObject _setupIdentity;
    private readonly InventorMcpConfig _config;
    private readonly TargetRegistry _registry;
    private readonly TargetBinding _binding;
    private readonly SemaphoreSlim _sendSlots = new(32, 32);

    /// <summary>Writes since the last agent report and the _report reminder (FC-RPT-05).</summary>
    internal Reports.ReportTracker Reports { get; } = new(command => ToolCatalog.ForCommand(command, 0)?.ReadOnly);

    /// <summary>Saved agent reports (FC-RPT-04).</summary>
    internal Reports.ReportStore ReportStore { get; }

    public PluginClient(InventorMcpConfig config) : this(config, null) { }

    internal PluginClient(InventorMcpConfig config, Func<int, TargetBinding.ProcessProbe>? processProbe)
    {
        _config = config;
        _registry = new TargetRegistry(config.DescriptorDirectory);
        ReportStore = new Reports.ReportStore(config.ReportDirectory ?? System.IO.Path.Combine(config.DataDirectory, "reports"));
        _binding = new TargetBinding(config.TargetId, processProbe);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Inventor requires Windows.");
        _setupIdentity = SetupRuntimeIdentity.Capture(config.LocalAppDataRoot is { } root
            ? Bimwright.Setup.RuntimeLayout.Resolve(root, System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, "ipt-mcp")
            : Bimwright.Setup.RuntimeLayout.ForCurrentUser("ipt-mcp"),
            typeof(PluginClient).Assembly, "gateway");
    }

    internal JObject GatewayIdentity => (JObject)_setupIdentity.DeepClone();

    public IReadOnlyList<TargetDescriptor> ListTargets() => _registry.List();

    /// <summary>The selected target, binding it on first use only when exactly one live target matches.
    /// Throws NO_TARGET, AMBIGUOUS_TARGET, TARGET_LOST or TARGET_UNAVAILABLE instead of falling back.</summary>
    public TargetDescriptor ResolveTarget() => _binding.Resolve(_registry.List());

    /// <summary>Selects one live target by pid:&lt;n&gt;, id:&lt;target_id&gt;, year, pid or pipe name; ambiguity is refused.</summary>
    public TargetDescriptor SwitchTarget(string selector, bool rebind = false) => _binding.Switch(selector, _registry.List(), rebind);

    /// <summary>The current selection without binding one (inventor_get_current_target, FC-TGT-05); null when none.</summary>
    public TargetDescriptor? CurrentTarget() => _binding.Current(_registry.List());

    internal string? SelectedTargetId => _binding.SelectedTargetId;

    internal string? SelectedBy => _binding.SelectedBy;

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
        if (ForwardedCall.Value is { } activity) activity.Value = true;
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
            var target = ResolveTarget();
            targetId = target.TargetId;
            var pinned = GateWrite(command, @params, target);

            var effectiveTimeoutMs = ResolveEffectiveTimeout(_config.TimeoutMs, timeoutMs);
            var env = new InventorCommandEnvelope
            {
                Id = new Guid(requestId),
                Command = command,
                Params = @params,
                TimeoutMs = effectiveTimeoutMs,
                AuthToken = target.AuthToken,
                ReadOnly = _config.ReadOnly,
                RecordCalls = _config.EnableCallLog,
                SpillRetentionHours = _config.SpillRetentionHours,
                Tool = ToolCatalog.ForCommand(ActivityCommand.Value ?? command, effectiveTimeoutMs),
                DocumentTarget = new DocumentTargetEnvelope { ExpectedDocumentId = pinned?.Id },
            };

            var line = JsonConvert.SerializeObject(env) + "\n";
            var response = await SendLineAsync(target, line, ct, effectiveTimeoutMs);
            responseBytes = Encoding.UTF8.GetByteCount(response);

            var result = JsonConvert.DeserializeObject<InventorCommandResult>(response)
                         ?? throw new InventorGatewayException(InventorErrorCodes.API_ERROR, "unparseable response");
            // "meta": null in the wire JSON overrides the initializer, so guard before reading it.
            targetId = result.Meta?.TargetId ?? targetId;
            pluginDurationMs = result.Meta?.DurationMs;
            // Owner decision Q-B: an open/create command that activated a new document moves the pin to it.
            if (result.Meta?.RepinDocumentId is { Length: > 0 } repin)
                _binding.PinDocument(target, repin, result.Meta.DocumentTitle);
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
                throw new InventorGatewayException(code, message, result.Data);
            }

            ok = true;
            if (Reports.Record(command, @params, requestId, result.Meta?.DocumentTitle) is { } reminder && result.Data is JObject reminded)
                reminded["_report"] = reminder;
            if (command == "health" && result.Data is JObject healthData)
                healthData["setup_gateway"] = GatewayIdentity;
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

    internal (string Id, string? Title)? PinnedDocument(TargetDescriptor target) => _binding.PinnedDocument(target);

    internal bool PinDocument(TargetDescriptor target, string documentId, string? title) => _binding.PinDocument(target, documentId, title);

    internal static bool SupportsDocumentTarget(TargetDescriptor target) =>
        target.Capabilities?.Contains(DocumentTargetPolicy.Protocol, StringComparer.Ordinal) == true;

    /// <summary>
    /// Family contract FC-PAIR-01 / FC-DOC-03: a write is not sent to an add-in without document_target_v1,
    /// nor before the target and its document are confirmed. Returns the pinned document for the envelope.
    /// </summary>
    private (string Id, string? Title)? GateWrite(string command, JObject @params, TargetDescriptor target)
    {
        var pinned = _binding.PinnedDocument(target);
        // Commands without an MCP tool (health, internal calls) are left to the add-in's own check.
        if (ToolCatalog.ForCommand(command, 0) is not { ReadOnly: false }
            || !DocumentTargetPolicy.NeedsConfirmedTarget(command, isReadOnly: false, @params))
            return pinned;
        if (!SupportsDocumentTarget(target))
            throw PairingRequired(target, command);
        if (pinned is null)
            throw new InventorGatewayException(InventorErrorCodes.TARGET_UNCONFIRMED,
                $"{command} was not sent: writes need a confirmed Inventor and document. Check with the user that " +
                (string.IsNullOrEmpty(target.DocumentTitle) ? "this Inventor" : $"'{target.DocumentTitle}'") +
                $" is what to change, then call inventor_switch_target(target=\"pid:{target.ProcessId}\", verify=true) and repeat the call.",
                new JObject { ["selected_target_id"] = target.TargetId, ["document_title"] = target.DocumentTitle });
        return pinned;
    }

    private static InventorGatewayException PairingRequired(TargetDescriptor target, string command) =>
        new(InventorErrorCodes.PAIRING_REQUIRED,
            $"{command} was not sent: the Inventor add-in of {target.TargetId} does not confirm documents ({DocumentTargetPolicy.Protocol}). " +
            "Update the ipt-mcp server and the Inventor add-in together from the same release, then restart Inventor and the MCP client. Reads still work.");

    /// <summary>inventor_switch_target(verify=true): reads the active document on the selected target and pins it.</summary>
    internal async Task<(string Id, string? Title)> ConfirmDocumentAsync(TargetDescriptor target, CancellationToken ct)
    {
        if (!SupportsDocumentTarget(target)) throw PairingRequired(target, "verify");
        var data = await SendAsync("document_target", new JObject(), ct) as JObject;
        var id = (string?)data?["document_id"];
        if (string.IsNullOrEmpty(id))
            throw new InventorGatewayException(InventorErrorCodes.API_ERROR, $"Inventor {target.TargetId} did not report its active document.");
        var title = (string?)data!["document_title"];
        if (!_binding.PinDocument(target, id!, title))
            throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE,
                $"The selected target changed while {target.TargetId} was being verified; call inventor_switch_target again.");
        return (id!, title);
    }

    // Windows Error Reporting can keep a crashing Inventor alive for a few seconds while it writes the dump.
    internal const int CrashProbeMs = 5000;

    /// <summary>
    /// The add-in closed the connection without answering. If the Inventor process is gone (checked for up
    /// to <paramref name="waitMs"/>), the host died inside the call: HOST_CRASHED. Otherwise TARGET_UNAVAILABLE.
    /// Both name the target so the agent knows which session the call went to.
    /// </summary>
    internal static async Task<InventorGatewayException> ClassifyClosedConnectionAsync(TargetDescriptor target,
        Func<int, TargetBinding.ProcessProbe> probe, int waitMs, CancellationToken ct = default)
    {
        var deadline = Environment.TickCount64 + waitMs;
        while (true)
        {
            if (!probe(target.ProcessId).Running)
                return new InventorGatewayException(InventorErrorCodes.HOST_CRASHED,
                    $"Inventor {target.TargetId} exited during this call; it most likely crashed inside the command. " +
                    "Edits since the last save are lost. Do not resend the call. Tell the user, and confirm with them before " +
                    "switching to another Inventor and reopening the model. Inventor's own crash files, if written: " +
                    @"%TEMP%\Inventor*.dmp and Inventor*data.tmp, %LOCALAPPDATA%\Autodesk\CER.");
            if (Environment.TickCount64 >= deadline) break;
            await Task.Delay(250, ct);
        }
        return new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE,
            $"The add-in of {target.TargetId} closed the connection while Inventor is still running " +
            "(it may be writing a crash report or showing a dialog). Call inventor_health before retrying; do not automatically resend mutations.");
    }

    private static bool IsPipe(TargetDescriptor target) => string.Equals(target.Transport, "pipe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when at least one instance of the pipe exists, busy or not. WaitNamedPipe with a 1 ms wait
    /// fails with ERROR_FILE_NOT_FOUND only when no instance exists; it never takes an instance.
    /// </summary>
    private static bool PipeExists(string? name)
    {
        if (string.IsNullOrEmpty(name) || !OperatingSystem.IsWindows()) return true;
        if (WaitNamedPipe(@"\\.\pipe\" + name, 1)) return true;
        return Marshal.GetLastWin32Error() != 2;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WaitNamedPipeW")]
    private static extern bool WaitNamedPipe(string name, uint timeoutMs);

    private async Task<string> SendLineAsync(TargetDescriptor target, string line, CancellationToken ct, int timeoutMs)
    {
        if (!await _sendSlots.WaitAsync(0, ct))
            throw new InventorGatewayException(InventorErrorCodes.API_ERROR, "Gateway connection capacity reached; this request was not sent.");
        IDisposable? owner = null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Setup/write has a separate finite budget. It must not consume the Inventor execution allowance.
        budget.CancelAfter(Math.Min(10000, Math.Max(timeoutMs, TransportGraceMs)));
        using var abort = budget.Token.Register(() => { try { owner?.Dispose(); } catch { } });
        var stage = "connect";
        try
        {
            Stream stream;
            if (IsPipe(target))
            {
                var pipe = new NamedPipeClientStream(".", target.PipeName ?? "", PipeDirection.InOut, PipeOptions.Asynchronous);
                owner = pipe;
                await pipe.ConnectAsync(budget.Token);
                stream = pipe;
            }
            else
            {
                var client = new TcpClient();
                owner = client;
                await client.ConnectAsync("127.0.0.1", target.Port, budget.Token);
                stream = client.GetStream();
            }
            stage = "write";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line), budget.Token);
            stage = "response";
            budget.CancelAfter(timeoutMs + TransportGraceMs);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // Cancellation closes this call's stream, which wakes the bounded NDJSON reader too.
            var read = await NdjsonLineReader.ReadLineBoundedAsync(reader, _config.MaxResponseBytes);
            if (read.Overflow)
                throw new InventorGatewayException(InventorErrorCodes.RESPONSE_TOO_LARGE, $"add-in response exceeded {_config.MaxResponseBytes} bytes");
            if (read.Line != null) return read.Line;
            // A cancelled call closes its own stream: report cancellation/TIMEOUT, not a host fault.
            budget.Token.ThrowIfCancellationRequested();
            throw await ClassifyClosedConnectionAsync(target, _binding.Probe, CrashProbeMs, budget.Token);
        }
        catch (Exception) when (ct.IsCancellationRequested) { ct.ThrowIfCancellationRequested(); throw; }
        catch (Exception) when (budget.IsCancellationRequested && stage == "connect" && IsPipe(target) && !PipeExists(target.PipeName))
        {
            throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE,
                $"The add-in pipe of {target.TargetId} does not exist (the add-in is not listening). Nothing was sent, so the command did not run. " +
                "Call inventor_health or inventor_list_available_targets before retrying.");
        }
        catch (Exception) when (budget.IsCancellationRequested && stage == "connect")
        {
            // Nothing was written: the add-in is alive but did not accept a connection (an older add-in
            // serves one call at a time, so a long call holds the only connection).
            throw new InventorGatewayException(InventorErrorCodes.TARGET_BUSY,
                $"Target {target.TargetId} did not accept the connection in time. Nothing was sent, so the command did not run. " +
                "The add-in may still be serving a long call (an add-in older than this gateway serves one call at a time). " +
                "Wait for that call to finish or ask the user, then retry; check inventor_health first.");
        }
        catch (Exception) when (budget.IsCancellationRequested)
        {
            throw new InventorGatewayException(InventorErrorCodes.TIMEOUT,
                $"Target {target.TargetId} {stage} timed out. A transmitted operation may still complete; inspect its outcome before retrying mutations.");
        }
        catch (InventorGatewayException) { throw; }
        catch (Exception ex) when (stage == "connect")
        {
            throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE,
                $"Cannot reach target {target.TargetId}: {ex.Message}. Nothing was sent, so the command did not run.");
        }
        catch (Exception ex)
        {
            throw new InventorGatewayException(InventorErrorCodes.TARGET_UNAVAILABLE,
                $"Cannot reach target {target.TargetId}: {ex.Message}. A transmitted operation may still complete; do not automatically resend mutations.");
        }
        finally { try { owner?.Dispose(); } finally { _sendSlots.Release(); } }
    }
}
