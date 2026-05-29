using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Tools;

/// <summary>
/// iProperty and mass-property tools (toolset <c>properties</c>). Reads/writes Inventor iProperties by
/// property-set and property name, and reports mass properties (mass, volume, area, centre of mass,
/// bounding box) of the active part document. Thin wrappers over wire commands.
/// </summary>
[McpServerToolType]
public sealed class PropertyTools
{
    private readonly PluginClient _client;
    public PropertyTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_get_iproperty"),
     Description("Get an iProperty value. set_name is the property set (e.g. \"Design Tracking Properties\", \"Summary Information\"); prop_name is the property (e.g. \"Author\", \"Part Number\").")]
    public Task<string> GetIProperty(string setName, string propName, CancellationToken ct = default)
        => Call("get_iproperty", new JObject { ["set_name"] = setName, ["prop_name"] = propName }, ct);

    [McpServerTool(Name = "inventor_set_iproperty"),
     Description("Set an iProperty value. set_name is the property set, prop_name the property, value the new value (string).")]
    public Task<string> SetIProperty(string setName, string propName, string value, CancellationToken ct = default)
        => Call("set_iproperty", new JObject { ["set_name"] = setName, ["prop_name"] = propName, ["value"] = value }, ct);

    [McpServerTool(Name = "inventor_get_mass_properties"),
     Description("Get mass properties of the active part document: mass (g), volume (mm^3), surface area (mm^2), centre of mass (mm), and bounding box (mm).")]
    public Task<string> GetMassProperties(CancellationToken ct = default)
        => Call("get_mass_properties", new JObject(), ct);

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
