using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Read-only target/document probes. This class is registered by the <c>query</c> toolset and remains
/// visible in <c>--read-only</c> mode.
/// </summary>
[McpServerToolType]
public sealed class QueryTools
{
    private readonly PluginClient _client;
    public QueryTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_health"),
     Description("Probe the active Inventor add-in target: reports inventor_year, process_id, whether a document is open, and the active document type. Read-only; use it to confirm the add-in is reachable.")]
    public Task<string> Health(CancellationToken ct = default)
        => Call("health", new JObject(), ct);

    [McpServerTool(Name = "inventor_list_open_documents"),
     Description("List all open Inventor documents: title, full path, document type, and which one is active.")]
    public Task<string> ListOpenDocuments(CancellationToken ct = default)
        => Call("list_open_documents", new JObject(), ct);

    [McpServerTool(Name = "inventor_get_document_info"),
     Description("Get the active Inventor document's title, full path, and document type.")]
    public Task<string> GetDocumentInfo(CancellationToken ct = default)
        => Call("get_document_info", new JObject(), ct);

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
