using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Read-only sheet fabrication probes (toolset <c>sheet_fabrication_query</c>, survives <c>--read-only</c>).
/// </summary>
[McpServerToolType]
[Toolset("sheet_fabrication_query")]
public sealed class SheetFabricationQueryTools
{
    private readonly PluginClient _client;
    public SheetFabricationQueryTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_sheet_fabrication_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Sheet Fabrication part summary: thickness_mm and thickness_expression (effective part Thickness parameter), bend_radius_mm, use_sheet_fabrication_style_thickness (false = part overrides the style), style_name, unfold_method, bend_count, has_flat_pattern and — once a flat pattern exists — flat_length_mm/flat_width_mm (the blank size before export_dxf source=flat_pattern). Fails WRONG_DOCUMENT_TYPE on a non-sheet fabrication part. Optional document targets a document already in memory.")]
    public Task<string> SheetFabricationInfo(string? document = null, CancellationToken ct = default)
        => Call("sheet_fabrication_info", new JObject { ["document"] = document }, ct);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex);
        }
    }
}
