#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using InvApi = global::Inventor;      // Autodesk.Inventor.Interop (aliased to avoid the Bimwright.Ipt collision)
using Newtonsoft.Json;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
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
    private InflightBreadcrumb? _inflight;
    private bool _closeDocumentHooked;

    public void Activate(InvApi.ApplicationAddInSite site, bool firstTime)
    {
        if (_server != null) throw new InvalidOperationException("The previous Inventor MCP lifetime has not been deactivated.");
        try
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
        if (LogRetentionPolicy.KeepAll)
            System.Diagnostics.Trace.WriteLine("[ipt-mcp] BIMWRIGHT_KEEP_ALL_LOGS is on; logs, journals and reports are never deleted automatically.");
        try { McpLogger.RunMaintenance(); } catch { }
        try { SendCodeJournal.RunMaintenance(IptPrivacyConfig.Load()); } catch { }
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            _inflight = new InflightBreadcrumb(McpLogger.LogDir, self.Id, self.StartTime.ToUniversalTime());
            // A marker left by a dead Inventor means that process ended inside a command.
            foreach (var orphan in InflightBreadcrumb.TakeOrphans(McpLogger.LogDir, self.Id, InflightBreadcrumb.IsSameProcessRunning))
                if (!EnvFlag("BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_CALL_LOG")) McpLogger.LogCrashSuspected(orphan);
        }
        catch { _inflight = null; }
        try { _historyHost = new HistoryHost(); } catch { _historyHost = null; }
        _sessionLog = new McpSessionLog(_historyHost is { } hh ? hh.Post : (Action<Action>?)null);
        _sessionLog.EntryAdded += OnSessionEntryAdded;
        _sessionLog.Cleared += PostHistoryCount;

        // Toasts (Phase 1b): settings before the transport starts, so the first command already sees them.
        _configPath = ToastConfigStore.DefaultPath(runtimeLayout.DataRoot);
        _toastSettings = ToastConfigStore.Load(_configPath, Environment.GetEnvironmentVariable);
        _toasts = new ToastNotifier(_toastSettings.EnableToast,
            $"{BrandAssets.ProductName} {_year}", ShowOrFocusHistoryWindow,
            ToastConfigStore.LoadIdleSeconds(_configPath),
            _configPath);
        _toasts.ShowBranding = _toastSettings.ShowBranding;
        _toasts.Position = ToastConfigStore.LoadPosition(_configPath);
        _toasts.SavePosition = value => ToastConfigStore.SavePosition(_configPath, value);

        // Start the transport and read back its bound endpoint into the descriptor.
        var setupIdentity = SetupRuntimeIdentity.Capture(runtimeLayout, GetType().Assembly, "plugin", _year);
        var server = TransportFactory.Create(_year, _descriptorDir, out var descriptor);
        _server = server;
        _descriptor = descriptor;
        _descriptor.SetupIdentity = setupIdentity;
        _descriptor.Capabilities = new[] { DocumentTargetPolicy.Protocol, Bimwright.Ipt.Shared.Handlers.Code.SendCodeHelpers.Capability, Bimwright.Ipt.Shared.Handlers.Code.SendCodeHelpers.CapabilityV2 };
        try { using var self = System.Diagnostics.Process.GetCurrentProcess(); _descriptor.ProcessStartUtc = self.StartTime.ToUniversalTime(); } catch { }
        server.Start((line, tcs) => HandleLine(line, dispatcher, options, descriptor, tcs, server.Stopping));
        if (server is TcpTransportServer tcp) descriptor.Port = tcp.Port;

        // Fill the active-document title/path (caller holds the Inventor.Application) + persist + heartbeat.
        ReadActiveDocument(out var docTitle, out var docPath);
        _descriptorWriter = new TargetDescriptorWriter(_descriptorDir, _descriptor);
        _descriptorWriter.Start(docTitle, docPath, lifetime: server.Stopping);

        StartToastUi();

        // Document lifetime ids (document_target_v1): a closed document's id is dropped, so reopening it gets a new one.
        try
        {
            _appEvents ??= _app.ApplicationEvents;
            _appEvents.OnCloseDocument += OnCloseDocument;
            _closeDocumentHooked = true;
        }
        catch { _closeDocumentHooked = false; }

        // Rising-edge watch (rvt-mcp IdlingUpdater parity — Inventor has no Idling event):
        // each client attach/re-attach gets one "Agent connected" toast.
        _connectionWatch = new ConnectionWatch(_server, info => _toasts?.NotifyConnection(info));

        StartUpdateNotice(runtimeLayout.DataRoot, server.Stopping);
        }
        catch { Deactivate(); throw; }
    }

    private string? _updateDataRoot;
    private CancellationToken _updateLifetime;
    private int _updateNoticeShown;
    private int _updateCheckInFlight;

    /// <summary>
    /// rvt-mcp parity: at most one GitHub request a day at startup (BIMWRIGHT_DISABLE_UPDATE_CHECK=1 turns it off),
    /// plus a shorter 30-minute cooldown when the user opens History. Shows at most one modeless card per Inventor
    /// session on the History UI thread. Never touches the STA and never blocks the MCP path.
    /// </summary>
    private void StartUpdateNotice(string dataRoot, CancellationToken lifetime)
    {
        _updateDataRoot = dataRoot;
        _updateLifetime = lifetime;
        RunUpdateNoticeCheck(Bimwright.Ipt.Shared.Update.UpdateChecker.CheckInterval, TimeSpan.FromSeconds(20));
    }

    private void TriggerUpdateNoticeFromUi()
    {
        RunUpdateNoticeCheck(Bimwright.Ipt.Shared.Update.UpdateChecker.InteractiveCheckInterval, TimeSpan.Zero);
    }

    private void RunUpdateNoticeCheck(TimeSpan interval, TimeSpan delayBeforeShow)
    {
        if (_historyHost is not { } host || string.IsNullOrEmpty(_updateDataRoot)) return;
        if (Bimwright.Ipt.Shared.Update.UpdateChecker.IsDisabled(Environment.GetEnvironmentVariable)) return;
        if (Volatile.Read(ref _updateNoticeShown) != 0) return;
        if (Interlocked.CompareExchange(ref _updateCheckInFlight, 1, 0) != 0) return;

        var statePath = Path.Combine(_updateDataRoot!, "update-check.json");
        var current = Bimwright.Ipt.Shared.Update.UpdateChecker.VersionOf(GetType().Assembly);
        var lifetime = _updateLifetime;
        Task.Run(async () =>
        {
            try
            {
                var info = await Bimwright.Ipt.Shared.Update.UpdateChecker.CheckAsync(current, statePath, checkInterval: interval).ConfigureAwait(false);
                if (info == null || info.IsSkipped) return;
                if (delayBeforeShow > TimeSpan.Zero)
                    await Task.Delay(delayBeforeShow, lifetime).ConfigureAwait(false);
                if (Interlocked.Exchange(ref _updateNoticeShown, 1) != 0) return;
                host.Post(() =>
                {
                    try { new UpdateNoticeWindow(info, statePath).Show(); } catch { }
                });
            }
            catch { }
            finally
            {
                Volatile.Write(ref _updateCheckInFlight, 0);
            }
        });
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
                SetToastIdleSeconds,
                () => _toasts?.IsPaused ?? ToastSnooze.IsActive(_configPath),
                SetToastPaused);
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
        if (on && !ToastSnooze.ResumeIfPaused(_configPath)) persisted = false;
        _toasts?.NotifyToggle(persisted);
    }

    private bool SetToastPaused(bool paused) =>
        _toasts != null
            ? (paused ? _toasts.PauseForToday() : _toasts.ResumeFromPause())
            : (paused ? ToastSnooze.PauseForToday(_configPath) : ToastSnooze.Resume(_configPath));

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
        TaskCompletionSource<string> tcs,
        CancellationToken stopping)
    {
        // Meta filled even on early-error paths (UNAUTHORIZED/TIMEOUT/API_ERROR) so the server
        // journal can attribute the call to this target (F1 hand-off note, spec F2).
        var meta = new InventorResponseMeta { TargetId = descriptor.TargetId, InventorYear = o.Year };
        var envId = Guid.Empty;
        InventorCommandEnvelope? authorized = null;              // new: for the toast on the catch path
        var clock = System.Diagnostics.Stopwatch.StartNew();     // new: toast duration (includes STA queue time)
        try
        {
            var env = InventorCommandEnvelope.Parse(line);
            envId = env.Id;
            if (!AuthToken.Verify(descriptor.AuthToken, env.AuthToken))
            {
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.UNAUTHORIZED, "Invalid or missing authorization token.", meta));
                return;   // not agent activity: no toast
            }
            authorized = env;
            _toasts?.SetAgent(env.ClientName);

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
                DeadlineUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(0, env.TimeoutMs - clock.ElapsedMilliseconds)),
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
            // Host busy (FC-BUSY-01): a modal dialog is waiting for the user. Refuse before queueing,
            // so the command neither times out behind the dialog nor runs later.
            if (HostBusyPolicy.RefusesForModal(env.Command, ModalDialogProbe.IsOpen(out var dialogTitle)))
            {
                var busy = HostBusyPolicy.ModalMessage(env.Command, dialogTitle);
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.HOST_BUSY, busy, meta));
                RecordOutcome(env, dispatcher, false, null, InventorErrorCodes.HOST_BUSY, busy, clock.ElapsedMilliseconds);
                return;
            }

            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            var breadcrumb = env.RecordCalls && !EnvFlag("BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_CALL_LOG") ? _inflight : null;
            var task = _sta!.TryInvokeAsync(() =>
            {
                // Marker on the STA, right before the API work: commands run one at a time here.
                // Document target (family contract FC-DOC-04): checked on the STA right before the handler.
                var command = env.Command ?? "";
                // A dialog's own message loop can run queued work: never start a command inside it.
                if (HostBusyPolicy.RefusesForModal(command, ModalDialogProbe.IsOpen(out var staDialogTitle)))
                    return InventorCommandResult.Fail(env.Id, InventorErrorCodes.HOST_BUSY, HostBusyPolicy.ModalMessage(command, staDialogTitle),
                        new InventorResponseMeta { TargetId = descriptor.TargetId, InventorYear = o.Year });
                var isReadOnly = !dispatcher.Commands.TryGetValue(command, out var handler) || handler.IsReadOnly;
                var before = ActiveDocumentStamp.Read(_app);
                var refusal = DocumentTargetPolicy.Check(command, isReadOnly, env.Params, env.DocumentTarget, before.Id, before.Title);
                if (refusal is { } refused)
                {
                    var rm = new InventorResponseMeta { TargetId = descriptor.TargetId, InventorYear = o.Year, DocumentId = before.Id, DocumentTitle = before.Title };
                    return InventorCommandResult.Fail(env.Id, refused.Code, refused.Message, rm);
                }
                breadcrumb?.Begin(env.Id.ToString("N"), command, env.Params?.ToString(Formatting.None));
                try
                {
                    var r = dispatcher.Dispatch(ctx, env);
                    var after = ActiveDocumentStamp.Read(_app);
                    r.Meta.DocumentId = after.Id;
                    r.Meta.DocumentTitle = after.Title;
                    if (r.Ok)
                    {
                        var pinned = env.DocumentTarget?.ExpectedDocumentId;
                        r.Meta.RepinDocumentId = DocumentTargetPolicy.Repin(command, env.DocumentTarget, before.Id, after.Id,
                            pinnedDocumentOpen: string.IsNullOrEmpty(pinned) || DocumentIdTable.Shared.IsOpen(pinned!));
                    }
                    RefreshToastSnapshot();   // still on the STA: cheap HWND/visibility read for toast placement
                    return r;
                }
                finally { breadcrumb?.End(); }
            }, requestCancellation.Token);
            if (task == null)
            {
                if (env.Command == "health")
                {
                    var busyData = BusyHealthData(o, healthIsQueued: false);
                    if (descriptor.SetupIdentity != null) busyData["setup_plugin"] = descriptor.SetupIdentity.DeepClone();
                    tcs.TrySetResult(JsonConvert.SerializeObject(InventorCommandResult.Success(env.Id, busyData, meta)));
                    RecordOutcome(env, dispatcher, true, busyData, null, null, clock.ElapsedMilliseconds);
                }
                else
                {
                    var message = HostBusyPolicy.QueueFullMessage(env.Command);
                    tcs.TrySetResult(Err(env.Id, InventorErrorCodes.HOST_BUSY, message, meta));
                    RecordOutcome(env, dispatcher, false, null, InventorErrorCodes.HOST_BUSY, message, clock.ElapsedMilliseconds);
                }
                return;
            }

            // A liveness probe must still answer when the STA thread is jammed, so `health`
            // gets a short fast-path wait and then a synthesized busy response built purely
            // from the queue counters (the dispatched item stays queued and completes unseen).
            var waitMs = env.Command == "health" ? Math.Min(env.TimeoutMs, HealthFastPathMs) : env.TimeoutMs;
            var finished = task.Wait(waitMs, stopping);
            if (!finished) requestCancellation.Cancel(); // Only queued work is cancellable; running COM retains its outcome.
            if (finished)
            {
                var result = task.Result;
                if (env.Command == "health" && result.Data is JObject healthData && descriptor.SetupIdentity != null)
                    healthData["setup_plugin"] = descriptor.SetupIdentity.DeepClone();
                tcs.TrySetResult(JsonConvert.SerializeObject(result));
                RecordOutcome(env, dispatcher, result.Ok, result.Data, result.Error?.Code, result.Error?.Message, clock.ElapsedMilliseconds);
            }
            else if (env.Command == "health")
            {
                var busyData = BusyHealthData(o, healthIsQueued: !task.IsCanceled);
                if (descriptor.SetupIdentity != null) busyData["setup_plugin"] = descriptor.SetupIdentity.DeepClone();
                tcs.TrySetResult(JsonConvert.SerializeObject(
                    InventorCommandResult.Success(env.Id, busyData, meta)));
                RecordOutcome(env, dispatcher, true, busyData, null, null, clock.ElapsedMilliseconds);
            }
            else if (task.IsCanceled)
            {
                var expired = HostBusyPolicy.ExpiredMessage(env.Command, env.TimeoutMs);
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.HOST_BUSY, expired, meta));
                RecordOutcome(env, dispatcher, false, null, InventorErrorCodes.HOST_BUSY, expired, clock.ElapsedMilliseconds);
            }
            else if (env.Command == "send_code" || env.Command == "ilogic_run_snippet")
            {
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.TIMEOUT,
                    $"{env.Command} exceeded {env.TimeoutMs} ms. The script MAY STILL BE RUNNING on Inventor's STA thread " +
                    "and later commands will queue behind it. Call inventor_health to check sta_busy before retrying; " +
                    "do not resend the same script." + ModalDialogProbe.TimeoutSuffix(), meta));
                RecordOutcome(env, dispatcher, false, null, InventorErrorCodes.TIMEOUT,
                    $"Script still running after {env.TimeoutMs} ms", clock.ElapsedMilliseconds);
            }
            else
            {
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.TIMEOUT,
                    $"{env.Command} did not finish within {env.TimeoutMs} ms; it may still complete on Inventor's STA thread." +
                    HostBusyPolicy.StartedTimeoutNote + ModalDialogProbe.TimeoutSuffix(), meta));
                RecordOutcome(env, dispatcher, false, null, InventorErrorCodes.TIMEOUT,
                    $"Inventor did not answer within {env.TimeoutMs} ms", clock.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Release the listener before Deactivate disposes the STA marshal. Running work is not aborted.
            tcs.TrySetCanceled();
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
            code ??= error?.Value<string>("code") ?? payload.Value<string>("error_code");
            message ??= error?.Value<string>("message") ?? payload.Value<string>("message") ?? payload["error"]?.ToString();
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
            if (tool == "send_code" || tool == "ilogic_run_snippet")
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
        try { p = WireJson.LoadObject(paramsJson ?? "{}"); } catch { p = new JObject(); }
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
            DeadlineUtc = DateTime.UtcNow.AddMilliseconds(env.TimeoutMs),
        };
        return await _sta!.InvokeAsync(() => _dispatcher!.Dispatch(ctx, env));
    }

    /// <summary>Ribbon or toast callback (any thread): show-or-focus on the history dispatcher; no COM access.</summary>
    private void ShowOrFocusHistoryWindow()
    {
        TriggerUpdateNoticeFromUi();
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

    private void OnCloseDocument(InvApi._Document document, string fullDocumentName, InvApi.EventTimingEnum timing,
        InvApi.NameValueMap context, out InvApi.HandlingCodeEnum handling)
    {
        handling = InvApi.HandlingCodeEnum.kEventNotHandled;
        if (timing != InvApi.EventTimingEnum.kBefore) return;
        try { if (Bimwright.Ipt.Shared.Handlers.Core.ActiveDocumentStamp.KeyOf((InvApi.Document)document) is { } key) DocumentIdTable.Shared.Forget(key); } catch { }
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
    private Newtonsoft.Json.Linq.JObject BusyHealthData(PluginOptions o, bool healthIsQueued = true)
        => new Newtonsoft.Json.Linq.JObject
        {
            ["inventor_year"] = o.Year,
            ["process_id"] = System.Diagnostics.Process.GetCurrentProcess().Id,
            ["has_active_document"] = null,
            ["document_type"] = null,
            ["sta_busy"] = true,
            // The queued health item itself still holds a slot — subtract it (same convention
            // as HealthHandler) so this reports *other* work backed up on the STA thread.
            ["pending_commands"] = Math.Max(0, (_sta?.Stats.PendingCommands ?? 0) - (healthIsQueued ? 1 : 0)),
            ["answered_without_sta"] = true,
            ["modal_dialog"] = ModalDialogProbe.Probe(),
        }.WithHostBusy(staBusy: true);

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
        _server?.Dispose();                             // Refuse teardown/reuse if the listener failed to stop.
        try
        {
            if (_appEvents != null)
            {
                _appEvents.OnActivateView -= OnActivateView;
                if (_closeDocumentHooked) _appEvents.OnCloseDocument -= OnCloseDocument;
                _closeDocumentHooked = false;
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
