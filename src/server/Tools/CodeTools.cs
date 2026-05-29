using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Tools;

/// <summary>
/// The opt-in <c>send_code</c> escape hatch (toolset <c>code</c>, off by default, never exposed in
/// read-only mode). Runs a C# snippet in-process inside the Inventor add-in against
/// <c>Inventor.Application</c>. Requires both server (<c>--enable-send-code</c> /
/// <c>BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1</c>) and add-in
/// (<c>BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1</c>) opt-in; the dispatcher returns
/// <c>SEND_CODE_DISABLED</c> otherwise.
/// </summary>
[McpServerToolType]
public sealed class CodeTools
{
    private readonly PluginClient _client;
    public CodeTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_send_code"),
     Description("DANGEROUS, opt-in only. Execute a C# code snippet in-process within the Inventor add-in against Inventor.Application for workflows not covered by typed tools. Disabled unless both server and add-in opt in (else SEND_CODE_DISABLED). Banned APIs (file/process/network/environment) are rejected.")]
    public Task<string> SendCode(string code, CancellationToken ct = default)
        => Call("send_code", new JObject { ["code"] = code }, ct);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        catch (InventorGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.Indented);
        }
    }
}
