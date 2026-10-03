#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using System.Threading.Tasks;
using InvApi = global::Inventor;      // Autodesk.Inventor.Interop (aliased to avoid the Bimwright.Ipt collision)
using Newtonsoft.Json;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Logging;
using Bimwright.Ipt.Shared.Transport;
using Bimwright.Ipt.Shared.Views;
using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>
/// Abstract <see cref="ApplicationAddInServer"/> base shared by every per-version add-in. It captures
/// <c>Inventor.Application</c> at <see cref="Activate"/>, builds the command registry, starts the
/// per-version transport (TCP for 2022-2024, Named Pipe for 2025-2027), writes a target descriptor with
/// a heartbeat, and marshals every command onto Inventor's STA thread via
/// <see cref="InventorStaDispatcher"/>.
///
/// The concrete per-version subclasses (owned by WS2-A/WS2-B) carry the <c>[ComVisible(true)]</c> and a
/// unique <c>[Guid(...)]</c> matching their <c>.addin</c> ClientId; this base carries neither. Because
/// the base is abstract, each <c>plugin-invNN</c> still compiles as a library even before its subclass
/// exists.
/// </summary>
public abstract class InventorAddInServerBase : InvApi.ApplicationAddInServer
{
    private InvApi.Application _app = null!;
    private ITransportServer? _server;
    private InventorStaDispatcher? _sta;
    private TargetDescriptorWriter? _descriptorWriter;
    private TargetDescriptor? _descriptor;
    private const int HealthFastPathMs = 2000;
    private int _year;
    private string _descriptorDir = "";
    private PluginOptions? _options;
    private ToastSettings? _toastSettings;
    private ToastNotifier? _toasts;
    private BimwrightRibbon? _ribbon;
    private CommandDispatcher? _dispatcher;
    private HistoryHost? _historyHost;
    private McpSessionLog? _sessionLog;
    private int _historyCountQueued;
    private InvApi.ApplicationEvents? _appEvents;
    private string _configPath = "";
    private ConnectionWatch? _connectionWatch;
    private System.Windows.Forms.Timer? _toastSnapshotTimer;

    public void Activate(InvApi.ApplicationAddInSite site, bool firstTime)
    {
        var runtimeLayout = Bimwright.Setup.RuntimeLayout.StartForCurrentUser("ipt-mcp");
        _app = site.Application;                    // stable API entry point (spec)
        _sta = new InventorStaDispatcher();         // created on the STA thread
        try { ModalDialogProbe.MainWindow = new IntPtr(_app.MainFrameHWND); } catch { }   // S1.1: STA-free dialog probe

        _year = InventorVersion.Year;
        var enableSendCode = !EnvFlag("BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE");
        var readOnly = EnvFlag("BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY") || EnvFlag("BIMWRIGHT_INVENTOR_READ_ONLY");
        _descriptorDir = runtimeLayout.RuntimeRoot;

        var options = new PluginOptions(_year, enableSendCode, readOnly, 0);
        _options = options;
        var handlers = InventorCommandRegistry.Build(options);
        var dispatcher = new CommandDispatcher(handlers, maxResponseBytes: 5_000_000);
        _dispatcher = dispatcher;

        // Command history (rvt-mcp parity): journal file + in-memory session log on a
        // dedicated UI thread. Everything is best-effort — history must never stop the MCP path.
        // Call journals are initialized lazily only for requests with record_calls=true.
        try { SendCodeJournal.RunMaintenance(IptPrivacyConfig.Load()); } catch { }
        try { _historyHost = new HistoryHost(); } catch { _historyHost = null; }
        _sessionLog = new McpSessionLog(_historyHost is { } hh ? hh.Post : (Action<Action>?)null);
        _sessionLog.EntryAdded += OnSessionEntryAdded;
        _sessionLog.Cleared += PostHistoryCount;

        // Toasts (Phase 1b): settings before the transport starts, so the first command already sees them.
        _configPath = ToastConfigStore.DefaultPath(runtimeLayout.DataRoot);
        _toastSettings = ToastConfigStore.Load(_configPath, Environment.GetEnvironmentVariable);
        _toasts = new ToastNotifier(_toastSettings.EnableToast,
            $"{BrandAssets.ProductName} {_year}", ShowOrFocusHistoryWindow,
            ToastConfigStore.LoadIdleSeconds(_configPath));
        _toasts.ShowBranding = _toastSettings.ShowBranding;
        _toasts.Position = ToastConfigStore.LoadPosition(_configPath);
        _toasts.SavePosition = value => ToastConfigStore.SavePosition(_configPath, value);

        // Start the transport and read back its bound endpoint into the descriptor.
        var setupIdentity = SetupRuntimeIdentity.Capture(runtimeLayout, GetType().Assembly, "plugin", _year);
        _server = TransportFactory.CreateStarted(
            _year, _descriptorDir,
            (line, tcs) => HandleLine(line, dispatcher, options, _descriptor!, tcs),
            out var descriptor);
        _descriptor = descriptor;
        _descriptor.SetupIdentity = setupIdentity;

        // Fill the active-document title/path (caller holds the Inventor.Application) + persist + heartbeat.
        ReadActiveDocument(out var docTitle, out var docPath);
        _descriptorWriter = new TargetDescriptorWriter(_descriptorDir, _descriptor);
        _descriptorWriter.Start(docTitle, docPath);

        StartToastUi();

        // Rising-edge watch (rvt-mcp IdlingUpdater parity — Inventor has no Idling event):
        // each client attach/re-attach gets one "Agent connected" toast.
        _connectionWatch = new ConnectionWatch(_server, info => _toasts?.NotifyConnection(info));
    }

    /// <summary>Ribbon + Inventor events for toasts. Failures here never stop the MCP transport.</summary>
    private void StartToastUi()
    {
        RefreshToastWhenReady(); // Activate may precede app.Visible=true; retry on Inventor's STA.
        try
        {
            _ribbon = new BimwrightRibbon(
                _app,
                GetType().GUID.ToString("B").ToUpperInvariant(),   // the per-year add-in ClientId
                () => _toasts?.Enabled ?? false,
                SetToastsOn,
                BuildStatusText,
                ShowOrFocusHistoryWindow,
                () => _sessionLog?.Count ?? 0,
                () => _toasts?.ShowBranding ?? false,
                SetBrandingOn,
                () => _toasts?.Position ?? new ToastPositionOptions(),
                value => _toasts?.SetPosition(value) ?? false,
                () => _toasts?.IdleSeconds ?? ToastConfigStore.DefaultIdleSeconds,
                SetToastIdleSeconds);
            _ribbon.Build();
        }
        catch
        {
            _ribbon = null;
        }
        try
        {
            _appEvents = _app.ApplicationEvents;
            _appEvents.OnActivateView += OnActivateView;
        }
        catch
        {
            _appEvents = null;
        }
    }

    private bool SetToastIdleSeconds(int seconds)
    {
        if (!ToastConfigStore.SaveIdleSeconds(_configPath, seconds)) return false;
        _toasts?.SetIdleSeconds(seconds);
        return true;
    }

    /// <summary>Ribbon Toast Brand (STA): takes effect now and persists for the next start.</summary>
    private void SetBrandingOn(bool show)
    {
        if (_toasts != null) _toasts.ShowBranding = show;
        ToastConfigStore.SaveShowBranding(_configPath, show);
    }

    /// <summary>Ribbon toggle (STA): takes effect now and persists. Env still wins at the next start.</summary>
    private void SetToastsOn(bool on)
    {
        if (_toasts != null) _toasts.Enabled = on;
        RefreshToastSnapshot(force: true); // on/off confirmations need a fresh snapshot, even when now off
        if (on) RefreshToastWhenReady();
        else StopToastSnapshotTimer();
        var persisted = ToastConfigStore.SaveEnableToast(_configPath, on);
        _toasts?.NotifyToggle(persisted);
    }

    /// <summary>Retry readiness on the Inventor STA, slowing down if startup keeps the frame hidden.</summary>
    private void RefreshToastWhenReady()
    {
        RefreshToastSnapshot();
        if (_toasts == null || !_toasts.Enabled || _toasts.HasVisibleSnapshot || _toastSnapshotTimer != null) return;
        var attempts = 0;
        _toastSnapshotTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _toastSnapshotTimer.Tick += (_, _) =>
        {
            RefreshToastSnapshot();
            if (_toasts == null || !_toasts.Enabled || _toasts.HasVisibleSnapshot)
            {
                StopToastSnapshotTimer();
                return;
            }
            if (attempts < 20 && ++attempts == 20)
                _toastSnapshotTimer!.Interval = 2000;
        };
        _toastSnapshotTimer.Start();
    }

    private void StopToastSnapshotTimer()
    {
        _toastSnapshotTimer?.Stop();
        _toastSnapshotTimer?.Dispose();
        _toastSnapshotTimer = null;
    }

    private string BuildStatusText() => StatusText.Build(new StatusInfo(
        _descriptor?.TargetId ?? "",
        _year,
        _descriptor?.Transport ?? "",
        _descriptor?.PipeName,
        _descriptor?.Port ?? 0,
        _options?.EnableSendCode ?? false,
        _options?.ReadOnly ?? false,
        _toastSettings ?? new ToastSettings(false, "default"),
        _toasts?.Enabled ?? false,
        _configPath));

    private void HandleLine(
        string line,
        CommandDispatcher dispatcher,
        PluginOptions o,
        TargetDescriptor descriptor,
        TaskCompletionSource<string> tcs)
    {
        // Meta filled even on early-error paths (UNAUTHORIZED/TIMEOUT/API_ERROR) so the server
        // journal can attribute the call to this target (F1 hand-off note, spec F2).
        var meta = new InventorResponseMeta { TargetId = descriptor.TargetId, InventorYear = o.Year };
        var envId = Guid.Empty;
        InventorCommandEnvelope? authorized = null;              // new: for the toast on the catch path
        var clock = System.Diagnostics.Stopwatch.StartNew();     // new: toast duration (includes STA queue time)
        try
        {
            var env = JsonConvert.DeserializeObject<InventorCommandEnvelope>(line)!;
            envId = env.Id;
            if (!AuthToken.Verify(descriptor.AuthToken, env.AuthToken))
            {
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.UNAUTHORIZED, "Invalid or missing authorization token.", meta));
                return;   // not agent activity: no toast
            }
            authorized = env;

            // Server-only tools report their outcome through the authenticated transport.
            // This is activity metadata, not an Inventor command or a second tool execution.
            if (env.Command == "report_tool_activity" && env.Tool != null)
            {
                var p = env.Params;
                env.Command = p.Value<string>("command") ?? "";
                env.Params = p["arguments"] as JObject ?? new JObject();
                RecordOutcome(env, dispatcher, p.Value<bool?>("ok") == true, p["result"], null,
                    p.Value<string>("error"), p.Value<long?>("duration_ms") ?? 0);
                tcs.TrySetResult(JsonConvert.SerializeObject(InventorCommandResult.Success(env.Id, new JObject { ["recorded"] = true }, meta)));
                return;
            }

            var ctx = new InventorCommandContext
            {
                ReadOnly = o.ReadOnly || env.ReadOnly,
                SpillRetentionHours = env.SpillRetentionHours > 0 ? env.SpillRetentionHours : 36,
                EnableSendCode = o.EnableSendCode,
                InventorYear = o.Year,
                TargetId = descriptor.TargetId,
                Application = _app,
                Commands = dispatcher.Commands,
                StaQueue = _sta!.Stats,
            };

            // STA-independent commands (report_task_result) touch no Inventor API: dispatch them on
            // this listener thread so a report still lands while the STA is jammed behind a timed-out
            // send_code. Notify runs BEFORE the response so the agent can see whether its card posted.
            if (dispatcher.Commands.TryGetValue(env.Command ?? "", out var direct) && direct is IStaIndependentCommand)
            {
                var directResult = dispatcher.Dispatch(ctx, env);
                var shown = RecordOutcome(env, dispatcher, directResult.Ok, directResult.Data,
                    directResult.Error?.Code, directResult.Error?.Message, clock.ElapsedMilliseconds);
                if (directResult.Data is JObject directData) directData["toast_shown"] = shown;
                tcs.TrySetResult(JsonConvert.SerializeObject(directResult));
                return;
            }

            // Marshal the actual API work onto the STA thread. task.Wait is the single owner of
            // the timeout (spec F2-b): nothing else can interrupt work running on the STA thread.
            var task = _sta!.InvokeAsync(() =>
            {
                var r = dispatcher.Dispatch(ctx, env);
                RefreshToastSnapshot();   // still on the STA: cheap HWND/visibility read for toast placement
                return r;
            });

            // A liveness probe must still answer when the STA thread is jammed, so `health`
            // gets a short fast-path wait and then a synthesized busy response built purely
            // from the queue counters (the dispatched item stays queued and completes unseen).
            var waitMs = env.Command == "health" ? Math.Min(env.TimeoutMs, HealthFastPathMs) : env.TimeoutMs;
            if (task.Wait(waitMs))
            {
                var result = task.Result;
                if (env.Command == "health" && result.Data is JObject healthData && descriptor.SetupIdentity != null)
                    healthData["setup_plugin"] = descriptor.SetupIdentity.DeepClone();
                tcs.TrySetResult(JsonConvert.SerializeObject(result));
                RecordOutcome(env, dispatcher, result.Ok, result.Data, result.Error?.Code, result.Error?.Message, clock.ElapsedMilliseconds);
            }
            else if (env.Command == "health")
            {
                var busyData = BusyHealthData(o);
                if (descriptor.SetupIdentity != null) busyData["setup_plugin"] = descriptor.SetupIdentity.DeepClone();
                tcs.TrySetResult(JsonConvert.SerializeObject(
                    InventorCommandResult.Success(env.Id, busyData, meta)));
                RecordOutcome(env, dispatcher, true, busyData, null, null, clock.ElapsedMilliseconds);
            }
            else if (env.Command == "send_code")
            {
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.TIMEOUT,
                    $"send_code exceeded {env.TimeoutMs} ms. The script MAY STILL BE RUNNING on Inventor's STA thread " +
                    "and later commands will queue behind it. Call inventor_health to check sta_busy before retrying; " +
                    "do not resend the same script." + ModalDialogProbe.TimeoutSuffix(), meta));
                RecordOutcome(env, dispatcher, false, null, InventorErrorCodes.TIMEOUT,
                    $"Script still running after {env.TimeoutMs} ms", clock.ElapsedMilliseconds);
            }
            else
            {
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.TIMEOUT,
                    $"{env.Command} did not finish within {env.TimeoutMs} ms; it may still complete on Inventor's STA thread." +
                    ModalDialogProbe.TimeoutSuffix(), meta));
                RecordOutcome(env, dispatcher, false, null, InventorErrorCodes.TIMEOUT,
                    $"Inventor did not answer within {env.TimeoutMs} ms", clock.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            // task.Wait surfaces STA-side failures as AggregateException — unwrap for a useful message.
            var inner = ex is AggregateException agg ? agg.GetBaseException() : ex;
            tcs.TrySetResult(Err(envId, InventorErrorCodes.API_ERROR, inner.Message, meta));
            if (authorized != null)
                RecordOutcome(authorized, dispatcher, false, null, InventorErrorCodes.API_ERROR, inner.Message, clock.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Command outcome fan-out (rvt-mcp parity): every authorized wire command is journaled to
    /// mcp-calls.jsonl, appended to the session log behind the History window, and (for
    /// send_code) offered to the opt-in send-code journal — then toasted. Returns the toast_shown
    /// value for STA-independent commands. Never throws; never changes a command's outcome.
    /// </summary>
    private bool RecordOutcome(
        InventorCommandEnvelope env, CommandDispatcher dispatcher, bool ok, JToken? data, string? code, string? message, long ms)
    {
        if (data is JObject payload && payload.Value<bool?>("ok") == false)
        {
            ok = false;
            var error = payload["error"] as JObject;
            code ??= error?.Value<string>("code");
            message ??= error?.Value<string>("message") ?? payload["error"]?.ToString();
        }
        LogCall(env, dispatcher, ok, data, code, message, ms);
        return NotifyToast(env, dispatcher, ok, data, code, message, ms);
    }

    /// <summary>Journal + session-log side of <see cref="RecordOutcome"/>. Best effort, swallows everything.</summary>
    private void LogCall(
        InventorCommandEnvelope env, CommandDispatcher dispatcher, bool ok, JToken? data, string? code, string? message, long ms)
    {
        try
        {
            var tool = env.Command ?? "";
            var paramsJson = env.Params?.ToString(Formatting.None);
            var handler = dispatcher.Commands.TryGetValue(tool, out var h) ? h : null;
            var error = ok ? null : (message ?? code ?? "failed");
            string? codeSnippet = null;
            if (tool == "send_code")
            {
                try { codeSnippet = env.Params?.Value<string>("code"); } catch { }
            }
            string? resultJson = null;
            try { resultJson = data?.ToString(Formatting.None); } catch { }
            var sessionResult = resultJson != null && resultJson.Length > 10240
                ? resultJson.Substring(0, 10240) : resultJson;

            var recordCalls = env.RecordCalls && !EnvFlag("BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_CALL_LOG");
            McpLogger.Log(tool, paramsJson, ok, ms, error, codeSnippet, resultJson, enabled: recordCalls);
            if (recordCalls) SendCodeJournalGate.OnSendCodeLogged(tool, paramsJson, codeSnippet, ok, ms, error, resultJson);
            _sessionLog?.Add(new McpCallEntry
            {
                ToolName = tool,
                ParamsJson = paramsJson,
                Success = ok,
                DurationMs = ms,
                ErrorMessage = error,
                CodeSnippet = codeSnippet,
                ResultJson = sessionResult,
                IsReadOnly = env.Tool?.ReadOnly ?? handler?.IsReadOnly,
                Summary = SummaryGenerator.Generate(tool, paramsJson, sessionResult, ok, error),
            });
        }
        catch
        {
            // history is best effort — never disturb the command path
        }
    }

    /// <summary>
    /// Re-run executor for the History window (rvt-mcp parity: their window re-enqueues through
    /// the event handler + ExternalEvent; Inventor marshals through <see cref="InventorStaDispatcher"/>).
    /// Re-runs have no server call-log authorization. The window adds the in-memory
    /// session entry itself (marked re-run of #N), without persisting call or body logs.
    /// </summary>
    private async Task<InventorCommandResult> ReRunCommandAsync(string toolName, string? paramsJson)
    {
        JObject p;
        try { p = JObject.Parse(paramsJson ?? "{}"); } catch { p = new JObject(); }
        var env = new InventorCommandEnvelope
        {
            Id = Guid.NewGuid(),
            Command = toolName,
            Params = p,
            TimeoutMs = 60000,
        };
        var ctx = new InventorCommandContext
        {
            ReadOnly = _options?.ReadOnly ?? false,
            EnableSendCode = _options?.EnableSendCode ?? false,
            InventorYear = _year,
            TargetId = _descriptor?.TargetId,
            Application = _app,
            Commands = _dispatcher?.Commands,
            StaQueue = _sta?.Stats,
        };
        return await _sta!.InvokeAsync(() => _dispatcher!.Dispatch(ctx, env));
    }

    /// <summary>Ribbon or toast callback (any thread): show-or-focus on the history dispatcher; no COM access.</summary>
    private void ShowOrFocusHistoryWindow()
    {
        var host = _historyHost;
        var log = _sessionLog;
        var dispatcher = _dispatcher;
        if (host == null || log == null || dispatcher == null) return;
        host.ShowOrFocus(() => new HistoryWindow(log, dispatcher, ReRunCommandAsync));
    }

    /// <summary>
    /// Session log change → ribbon "History (N)". EntryAdded/Cleared fire on the history
    /// UI thread; the label update marshals to Inventor's STA, coalesced so bursts cost
    /// one hop.
    /// </summary>
    private void OnSessionEntryAdded(McpCallEntry entry) => PostHistoryCount();

    private void PostHistoryCount()
    {
        var sta = _sta;
        var ribbon = _ribbon;
        if (sta == null || ribbon == null) return;
        if (System.Threading.Interlocked.Exchange(ref _historyCountQueued, 1) != 0) return;
        try
        {
            _ = sta.InvokeAsync(() =>
            {
                System.Threading.Interlocked.Exchange(ref _historyCountQueued, 0);
                try { ribbon.SetHistoryCount(_sessionLog?.Count ?? 0); } catch { }
                return true;
            });
        }
        catch
        {
            System.Threading.Interlocked.Exchange(ref _historyCountQueued, 0);
        }
    }

    /// <summary>
    /// Listener thread, after the response was handed back. A toast must never change a command's outcome.
    /// <c>health</c> is a liveness probe, not agent work, so it gets no toast. Returns whether a card
    /// was retained for display — STA-independent commands report it as <c>toast_shown</c>.
    /// </summary>
    private bool NotifyToast(
        InventorCommandEnvelope env, CommandDispatcher dispatcher, bool ok, JToken? data, string? code, string? message, long ms)
    {
        var toasts = _toasts;
        if (toasts is null || env.Command == "health") return false;
        try
        {
            bool? isReadOnly = env.Tool?.ReadOnly ?? (dispatcher.Commands.TryGetValue(env.Command ?? "", out var handler) ? handler.IsReadOnly : null);
            return toasts.Notify(new ToastEvent(env.Command ?? "", ok, data, code, message, ms, isReadOnly,
                env.Tool?.Name, env.Tool?.Toolset, env.Tool?.Description, env.Tool?.TimeoutMs));
        }
        catch
        {
            return false;   // best effort
        }
    }

    /// <summary>STA only. Refreshes what the toast thread needs; skipped entirely while toasts are off.</summary>
    private void RefreshToastSnapshot(bool force = false)
    {
        var toasts = _toasts;
        if (toasts is null || (!force && !toasts.Enabled)) return;
        try
        {
            toasts.UpdateSnapshot(InventorUiSnapshotReader.Read(_app));
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>A different view means a different anchor.</summary>
    private void OnActivateView(
        InvApi.View view, InvApi.EventTimingEnum timing, InvApi.NameValueMap context, out InvApi.HandlingCodeEnum handling)
    {
        handling = InvApi.HandlingCodeEnum.kEventNotHandled;
        if (timing != InvApi.EventTimingEnum.kAfter) return;
        try
        {
            RefreshToastSnapshot();
        }
        catch { }
    }

    /// <summary>
    /// Health payload for the fast path: the STA thread did not answer in time, so report the
    /// queue counters without touching <c>Inventor.Application</c> (spec F2-b).
    /// </summary>
    private Newtonsoft.Json.Linq.JObject BusyHealthData(PluginOptions o)
        => new()
        {
            ["inventor_year"] = o.Year,
            ["process_id"] = System.Diagnostics.Process.GetCurrentProcess().Id,
            ["has_active_document"] = null,
            ["document_type"] = null,
            ["sta_busy"] = true,
            // The queued health item itself still holds a slot — subtract it (same convention
            // as HealthHandler) so this reports *other* work backed up on the STA thread.
            ["pending_commands"] = Math.Max(0, (_sta?.Stats.PendingCommands ?? 0) - 1),
            ["answered_without_sta"] = true,
            ["modal_dialog"] = ModalDialogProbe.Probe(),
        };

    /// <summary>Reads the active document's display name and full path (best effort) off the STA thread.</summary>
    private void ReadActiveDocument(out string? title, out string? path)
    {
        title = null;
        path = null;
        try
        {
            var doc = _app.ActiveDocument;
            if (doc != null)
            {
                title = doc.DisplayName;
                try { var p = doc.FullFileName; path = string.IsNullOrEmpty(p) ? null : p; } catch { }
            }
        }
        catch
        {
            // no active document / API not ready — leave nulls.
        }
    }

    public void Deactivate()
    {
        StopToastSnapshotTimer();
        try { _connectionWatch?.Dispose(); } catch { }   // 0. stop the attach watch before the transport dies
        try { _server?.Dispose(); } catch { }            // 1. no new commands, so no new toasts
        try
        {
            if (_appEvents != null)
            {
                _appEvents.OnActivateView -= OnActivateView;
            }
        }
        catch { }
        try { _toasts?.Dispose(); } catch { }            // 2. close toast windows on their thread, stop it
        try { _historyHost?.Shutdown(); } catch { }      // 3. close history window on its thread, stop it
        try { _ribbon?.Remove(); } catch { }             // 4. ribbon (STA)
        try { _descriptorWriter?.Dispose(); } catch { }
        try { _sta?.Dispose(); } catch { }
        if (_sessionLog != null)
        {
            _sessionLog.EntryAdded -= OnSessionEntryAdded;
            _sessionLog.Cleared -= PostHistoryCount;
        }
        _server = null;
        _connectionWatch = null;
        _descriptorWriter = null;
        _sta = null;
        _descriptor = null;
        _toasts = null;
        _ribbon = null;
        _historyHost = null;
        _sessionLog = null;
        _dispatcher = null;
        _appEvents = null;
        _app = null!;
        GC.Collect();
    }

    public object Automation => null!;

    public void ExecuteCommand(int commandID) { }   // legacy no-op

    private static string Err(Guid id, string code, string message, InventorResponseMeta meta)
        => JsonConvert.SerializeObject(InventorCommandResult.Fail(id, code, Bimwright.Ipt.Shared.Security.ErrorSanitizer.Sanitize(message), meta));

    private static bool EnvFlag(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrEmpty(v) &&
            (v!.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
             v.Equals("on", StringComparison.OrdinalIgnoreCase));
    }
}
#endif
