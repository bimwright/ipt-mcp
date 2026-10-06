using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Read-only sheet-metal probes (toolset <c>sheetmetal_query</c>, survives <c>--read-only</c>).
/// </summary>
[McpServerToolType]
[Toolset("sheetmetal_query")]
public sealed class SheetMetalQueryTools
{
    private readonly PluginClient _client;
    public SheetMetalQueryTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_sheet_metal_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Sheet-metal part summary: thickness_mm and thickness_expression (effective part Thickness parameter), bend_radius_mm, use_sheet_metal_style_thickness (false = part overrides the style), style_name, unfold_method, bend_count, has_flat_pattern and — once a flat pattern exists — flat_length_mm/flat_width_mm (the blank size before export_dxf source=flat_pattern). Fails WRONG_DOCUMENT_TYPE on a non-sheet-metal part. Optional document targets a document already in memory.")]
    public Task<string> SheetMetalInfo(string? document = null, CancellationToken ct = default)
        => Call("sm_info", new JObject { ["document"] = document }, ct);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }
}
