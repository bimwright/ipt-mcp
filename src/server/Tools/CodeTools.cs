using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

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
     Description("DANGEROUS, opt-in only. Execute a C# code snippet in-process within the Inventor add-in against Inventor.Application for workflows not covered by typed tools. Disabled unless both server and add-in opt in (else SEND_CODE_DISABLED). Banned APIs (file/process/network/environment/dynamic-invocation) are rejected; reading type metadata via typeof/GetType is allowed. File writes made through the Inventor API (SaveAs, SaveCopyAs, translators) are NOT restricted by the export-root policy that typed export tools obey. Returns the script's return value as result plus captured stdout; timeout_ms (max 600000) overrides the per-call STA timeout.")]
    public Task<string> SendCode(string code, int? timeout_ms = null, CancellationToken ct = default)
        => Call("send_code", new JObject { ["code"] = code }, ct, timeout_ms);

    private async Task<string> Call(string command, JObject p, CancellationToken ct, int? timeoutMs = null)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct, timeoutMs);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }
}
