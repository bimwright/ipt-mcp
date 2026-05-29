using System.ComponentModel;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Server-side target/meta tools. These never round-trip to the add-in; they
/// operate purely on the server's view of the descriptor registry. They stay
/// exposed even under <c>--read-only</c> so an agent can always enumerate and
/// pin a target.
/// </summary>
[McpServerToolType]
public sealed class MetaTools
{
    private readonly PluginClient _client;
    public MetaTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_list_available_targets"),
     Description("List detected live Inventor add-in targets (year, pid, transport, active document). Use 4-digit calendar years (2022..2027), never legacy version codes.")]
    public string ListAvailableTargets() =>
        JsonConvert.SerializeObject(_client.ListTargets().Select(PublicTarget), Formatting.Indented);

    [McpServerTool(Name = "inventor_get_current_target"),
     Description("Report the server's currently selected Inventor target, or NO_TARGET if none is live.")]
    public string GetCurrentTarget()
    {
        var t = _client.CurrentTarget;
        return t is null
            ? JsonConvert.SerializeObject(new { ok = false, error = new { code = "NO_TARGET", message = "no live target" } }, Formatting.Indented)
            : JsonConvert.SerializeObject(PublicTarget(t), Formatting.Indented);
    }

    [McpServerTool(Name = "inventor_switch_target"),
     Description("Select the active target by descriptor id, year, or session. Server-side only; does not change the Inventor document. Use 4-digit years (2022..2027).")]
    public string SwitchTarget(string target) =>
        JsonConvert.SerializeObject(new { ok = _client.SwitchTarget(target), target }, Formatting.Indented);

    private static object PublicTarget(TargetDescriptor t) => new
    {
        target_id = t.TargetId,
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
