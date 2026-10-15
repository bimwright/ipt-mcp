using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Server-side target/meta tools. These never round-trip to the add-in; they
/// operate purely on the server's view of the descriptor registry. They stay
/// exposed even under <c>--read-only</c> so an agent can always enumerate and
/// pin a target.
/// </summary>
[McpServerToolType]
[Toolset("meta")]
public sealed class MetaTools
{
    private readonly PluginClient _client;
    public MetaTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_list_available_targets", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List detected live Inventor add-in targets (year, pid, transport, active document); `selected` marks this gateway's target. Use 4-digit calendar years (2022..2027), never legacy version codes.")]
    public string ListAvailableTargets() =>
        LoggedMeta("inventor_list_available_targets", null, () =>
        {
            var selected = _client.SelectedTargetId;
            return ToolResponse.Serialize(_client.ListTargets().Select(t => PublicTarget(t, t.TargetId == selected)));
        });

    [McpServerTool(Name = "inventor_get_current_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Report the Inventor target this gateway uses. Binds automatically only when exactly one target is live; otherwise returns AMBIGUOUS_TARGET, TARGET_LOST or NO_TARGET with candidates.")]
    public string GetCurrentTarget()
    {
        return LoggedMeta("inventor_get_current_target", null, () =>
        {
            try
            {
                var t = _client.ResolveTarget();
                var json = JObject.FromObject(PublicTarget(t, true));
                json["selected_by"] = _client.SelectedBy;
                return ToolResponse.Serialize(json);
            }
            catch (InventorGatewayException ex) { return ToolResponse.Error(ex); }
        });
    }

    [McpServerTool(Name = "inventor_switch_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Select the Inventor session this gateway uses: pid:<n> or id:<target_id> (preferred), a 4-digit year (2022..2027), or a pipe name. A selector matching several sessions is refused. The selection is kept until switched again and is never replaced automatically. Server-side only; does not change the Inventor document.")]
    public string SwitchTarget(string target) =>
        LoggedMeta("inventor_switch_target", new { target }, () =>
        {
            try
            {
                var t = _client.SwitchTarget(target);
                return ToolResponse.Serialize(new { ok = true, selector = target, target = PublicTarget(t, true) });
            }
            catch (InventorGatewayException ex) { return ToolResponse.Error(ex); }
        });

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
