#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using System.Threading.Tasks;
using InvApi = global::Inventor;      // Autodesk.Inventor.Interop (aliased to avoid the Bimwright.Ipt collision)
using Newtonsoft.Json;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Transport;

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
    private int _year;
    private string _descriptorDir = "";

    public void Activate(InvApi.ApplicationAddInSite site, bool firstTime)
    {
        _app = site.Application;                    // stable API entry point (spec)
        _sta = new InventorStaDispatcher();         // created on the STA thread

        _year = InventorVersion.Year;
        var enableSendCode = EnvFlag("BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE");
        var readOnly = EnvFlag("BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY") || EnvFlag("BIMWRIGHT_INVENTOR_READ_ONLY");
        _descriptorDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bimwright", "ipt-mcp");

        var options = new PluginOptions(_year, enableSendCode, readOnly, 0);
        var handlers = InventorCommandRegistry.Build(options);
        var dispatcher = new CommandDispatcher(handlers, maxResponseBytes: 5_000_000);

        // Start the transport and read back its bound endpoint into the descriptor.
        _server = TransportFactory.CreateStarted(
            _year, _descriptorDir,
            (line, tcs) => HandleLine(line, dispatcher, options, _descriptor!, tcs),
            out var descriptor);
        _descriptor = descriptor;

        // Fill the active-document title/path (caller holds the Inventor.Application) + persist + heartbeat.
        ReadActiveDocument(out var docTitle, out var docPath);
        _descriptorWriter = new TargetDescriptorWriter(_descriptorDir, _descriptor);
        _descriptorWriter.Start(docTitle, docPath);
    }

    private void HandleLine(
        string line,
        CommandDispatcher dispatcher,
        PluginOptions o,
        TargetDescriptor descriptor,
        TaskCompletionSource<string> tcs)
    {
        try
        {
            var env = JsonConvert.DeserializeObject<InventorCommandEnvelope>(line)!;
            if (!AuthToken.Verify(descriptor.AuthToken, env.AuthToken))
            {
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.UNAUTHORIZED, "Invalid or missing authorization token."));
                return;
            }

            var ctx = new InventorCommandContext
            {
                ReadOnly = o.ReadOnly || env.ReadOnly,
                EnableSendCode = o.EnableSendCode,
                InventorYear = o.Year,
                TargetId = descriptor.TargetId,
                Application = _app,
                Commands = dispatcher.Commands,
            };

            // Marshal the actual API work onto the STA thread.
            var task = _sta!.InvokeAsync(() => dispatcher.Dispatch(ctx, env), env.TimeoutMs);
            if (task.Wait(env.TimeoutMs))
                tcs.TrySetResult(JsonConvert.SerializeObject(task.Result));
            else
                tcs.TrySetResult(Err(env.Id, InventorErrorCodes.TIMEOUT, "STA dispatch timed out"));
        }
        catch (Exception ex)
        {
            tcs.TrySetResult(Err(Guid.Empty, InventorErrorCodes.API_ERROR, ex.Message));
        }
    }

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
        try { _server?.Dispose(); } catch { }
        try { _descriptorWriter?.Dispose(); } catch { }
        try { _sta?.Dispose(); } catch { }
        _server = null;
        _descriptorWriter = null;
        _sta = null;
        _descriptor = null;
        _app = null!;
        GC.Collect();
    }

    public object Automation => null!;

    public void ExecuteCommand(int commandID) { }   // legacy no-op

    private static string Err(Guid id, string code, string message)
        => JsonConvert.SerializeObject(InventorCommandResult.Fail(id, code, message, new InventorResponseMeta()));

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
