using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// iProperty and mass-property tools (toolset <c>properties</c>). Reads/writes Inventor iProperties by
/// property-set and property name, and reports mass properties (mass, volume, area, centre of mass,
/// bounding box) of the active part or assembly document. Thin wrappers over wire commands.
/// </summary>
[McpServerToolType]
public sealed class PropertyTools
{
    private readonly PluginClient _client;
    public PropertyTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_get_iproperty"),
     Description("Get an iProperty value. set_name is the property set (e.g. \"Inventor Summary Information\", \"Design Tracking Properties\" — the \"Inventor \" prefix is optional); prop_name is the property (e.g. \"Title\", \"Author\", \"Part Number\"). Use inventor_list_iproperty_sets to discover set and property names.")]
    public Task<string> GetIProperty(string setName, string propName, CancellationToken ct = default)
        => Call("get_iproperty", new JObject { ["set_name"] = setName, ["prop_name"] = propName }, ct);

    [McpServerTool(Name = "inventor_set_iproperty"),
     Description("Set an iProperty value. set_name is the property set (\"Inventor \" prefix optional, same as get_iproperty), prop_name the property, value the new value (string).")]
    public Task<string> SetIProperty(string setName, string propName, string value, CancellationToken ct = default)
        => Call("set_iproperty", new JObject { ["set_name"] = setName, ["prop_name"] = propName, ["value"] = value }, ct);

    [McpServerTool(Name = "inventor_list_iproperty_sets"),
     Description("List the iProperty sets of the active document: each set's name, internal_name and property names. include_values (default false) also returns each property's current value as a string (truncated at 200 chars). Read-only — use it to discover set_name/prop_name for get/set_iproperty.")]
    public Task<string> ListIPropertySets(bool include_values = false, CancellationToken ct = default)
        => Call("list_iproperty_sets", new JObject { ["include_values"] = include_values }, ct);

    [McpServerTool(Name = "inventor_get_mass_properties"),
     Description("Get mass properties of the active part or assembly document: mass (g), volume (mm^3), surface area (mm^2), centre of mass (mm), and bounding box (mm).")]
    public Task<string> GetMassProperties(CancellationToken ct = default)
        => Call("get_mass_properties", new JObject(), ct);

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
