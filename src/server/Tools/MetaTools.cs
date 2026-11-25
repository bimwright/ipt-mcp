using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Server-side target/meta tools. These never round-trip to the add-in; they
/// operate purely on the server's view of the descriptor registry. List and get
/// stay under <c>--read-only</c>; switch_target does not (family contract
/// FC-SRV-08): a read-only gateway uses <c>--target</c>, or the only live session.
/// </summary>
[McpServerToolType]
[Toolset("meta")]
public sealed class MetaTools
{
    private readonly PluginClient _client;
    public MetaTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_list_available_targets", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List detected live Inventor add-in targets (year, pid, transport, active document); `selected` marks this gateway's target. Use 4-digit calendar years (2022..2027), never legacy version codes. The gateway binds automatically only when exactly one Inventor is live; with several, calls return AMBIGUOUS_TARGET with candidates: list them here, ask the user which model if unclear, then call inventor_switch_target with pid:<n> or id:<target_id> (--read-only keeps read tools, list/get target and baked-tool listing but drops inventor_switch_target: restart with --target pid:<n>).")]
    public string ListAvailableTargets() =>
        LoggedMeta("inventor_list_available_targets", null, () =>
        {
            var selected = _client.SelectedTargetId;
            var targets = _client.ListTargets();
            // FC-TGT-07: inside the client's AppContainer no target is visible; explain instead of an empty list.
            if (targets.Count == 0 && _client.SandboxJson() is { } sandbox)
                return ToolResponse.Serialize(new JObject
                {
                    ["targets"] = new JArray(),
                    ["sandbox"] = sandbox,
                    ["note"] = ClientSandbox.Message,
                });
            return ToolResponse.Serialize(targets.Select(t => PublicTarget(t, t.TargetId == selected)));
        });

    [McpServerTool(Name = "inventor_get_current_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Report the Inventor target this gateway uses. Never selects one: with no selection yet it returns bound=false with the live candidates (the first host call binds only when exactly one is live). A selection that exited returns TARGET_LOST: it is never replaced automatically, so confirm the model with the user and switch explicitly.")]
    public string GetCurrentTarget()
    {
        return LoggedMeta("inventor_get_current_target", null, () =>
        {
            try
            {
                if (_client.CurrentTarget() is not { } t)
                {
                    var candidates = _client.ListTargets();
                    var sandbox = candidates.Count == 0 ? _client.SandboxJson() : null;
                    var unbound = new JObject
                    {
                        ["bound"] = false,
                        ["candidates"] = JArray.FromObject(candidates.Select(c => PublicTarget(c, false))),
                        ["next_step"] = sandbox is not null ? ClientSandbox.Message
                            : candidates.Count == 0
                            ? "No live Inventor target. Start Inventor with the bimwright add-in loaded."
                            : "Check with the user which Inventor to use, then inventor_switch_target(target=\"pid:<n>\", verify=true) before writes.",
                    };
                    if (sandbox is not null) unbound["sandbox"] = sandbox; // FC-TGT-07
                    return ToolResponse.Serialize(unbound);
                }
                var json = JObject.FromObject(PublicTarget(t, true));
                json["bound"] = true;
                json["selected_by"] = _client.SelectedBy;
                AddDocumentPin(json, t);
                return ToolResponse.Serialize(json);
            }
            catch (InventorGatewayException ex) { return ToolResponse.Error(ex); }
        });
    }

    [McpServerTool(Name = "inventor_switch_target", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Select the Inventor session this gateway uses: pid:<n> or id:<target_id> (preferred), auto, or a 4-digit year (2022..2027); other text returns INVALID_TARGET_SELECTOR. A selector matching several sessions is refused. The selection is kept until switched again and is never replaced automatically. " +
                 "auto and a year never move an existing selection to another Inventor, and leaving a running session this gateway selected needs rebind=true (TARGET_REBIND_REQUIRED otherwise); check with the user first. A refused switch keeps the current selection. Not available under --read-only (use --target at startup). " +
                 "verify=true also confirms the document that is active in that Inventor now: writes run only on a confirmed target and return TARGET_UNCONFIRMED until this is called once, and DOCUMENT_CHANGED or DOCUMENT_UNCONFIRMED (write not started) when another document is active: confirm the intended document again, or pass document=<path or name>. PAIRING_REQUIRED: update the server and add-in together; reads still work. " +
                 "Check with the user that the reported document is the one to change. Opening or creating a document with inventor_open_document/new_part/new_assembly/new_drawing/create_part moves the confirmation to it; a write with an explicit document parameter is not tied to the active document. " +
                 "Selecting the same session again keeps its confirmed document. After HOST_CRASHED, tell the user and confirm before switching to another Inventor. Does not change the Inventor document.")]
    public Task<string> SwitchTarget(string target, bool verify = false,
        [Description("Leave a running Inventor this gateway selected and move to another (default false). Only after the user agreed.")] bool rebind = false,
        CancellationToken ct = default) =>
        LoggedMetaAsync("inventor_switch_target", new { target, verify, rebind }, async () =>
        {
            try
            {
                var t = _client.SwitchTarget(target, rebind);
                var json = new JObject { ["ok"] = true, ["selector"] = target, ["target"] = JObject.FromObject(PublicTarget(t, true)) };
                if (verify) await _client.ConfirmDocumentAsync(t, ct);
                AddDocumentPin(json, t);
                return ToolResponse.Serialize(json);
            }
            catch (InventorGatewayException ex) { return ToolResponse.Error(ex); }
        });

    private void AddDocumentPin(JObject json, TargetDescriptor t)
    {
        var pin = _client.PinnedDocument(t);
        json["document_confirmed"] = pin is not null;
        json["confirmed_document_id"] = pin?.Id;
        json["confirmed_document_title"] = pin?.Title;
        if (pin is null)
            json["next_step"] = PluginClient.SupportsDocumentTarget(t)
                ? $"Before writes: inventor_switch_target(target=\"pid:{t.ProcessId}\", verify=true)."
                : "This add-in does not confirm documents (document_target_v1); writes return PAIRING_REQUIRED until the server and add-in are updated together.";
    }

    private static async Task<string> LoggedMetaAsync(string tool, object? parameters, Func<Task<string>> run)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();
        ServerLogger.LogStart(requestId, tool, parameters);
        try
        {
            var result = await run();
            sw.Stop();
            JToken? payload = null;
            try { payload = JToken.Parse(result); } catch { /* non-JSON payload */ }
            ServerLogger.LogFinish(requestId, tool, true, sw.ElapsedMilliseconds, data: payload);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            ServerLogger.LogFinish(requestId, tool, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    private static string LoggedMeta(string tool, object? parameters, Func<string> run)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();
        ServerLogger.LogStart(requestId, tool, parameters);
        try
        {
            var result = run();
            sw.Stop();
            // Feed the JSON payload as `data` so payload-level ok:false (e.g. NO_TARGET) lands in data_ok (spec F1-R3).
            JToken? payload = null;
            try { payload = JToken.Parse(result); } catch { /* non-JSON payload */ }
            ServerLogger.LogFinish(requestId, tool, true, sw.ElapsedMilliseconds, data: payload);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            ServerLogger.LogFinish(requestId, tool, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    private static object PublicTarget(TargetDescriptor t, bool selected) => new
    {
        target_id = t.TargetId,
        selected,
        inventor_year = t.InventorYear,
        process_id = t.ProcessId,
        host_app = t.HostApp,
        transport = t.Transport,
        port = t.Port,
        pipe_name = t.PipeName,
        document_title = t.DocumentTitle,
        document_path = t.DocumentPath,
        last_heartbeat_utc = t.LastHeartbeatUtc,
    };
}
