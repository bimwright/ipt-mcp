using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Tools;

/// <summary>
/// Model parameter tools (toolset <c>parameters</c>). Lists/reads/sets user and model parameters of
/// the active part document, and creates new user parameters. Thin wrappers over wire commands.
/// </summary>
[McpServerToolType]
public sealed class ParameterTools
{
    private readonly PluginClient _client;
    public ParameterTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_list_parameters"),
     Description("List the active part document's parameters (model + user): name, expression, evaluated value, unit, and kind.")]
    public Task<string> ListParameters(CancellationToken ct = default)
        => Call("list_parameters", new JObject(), ct);

    [McpServerTool(Name = "inventor_get_parameter"),
     Description("Get a single parameter of the active part document by name: expression, evaluated value, and unit.")]
    public Task<string> GetParameter(string name, CancellationToken ct = default)
        => Call("get_parameter", new JObject { ["name"] = name }, ct);

    [McpServerTool(Name = "inventor_set_parameter"),
     Description("Set an existing parameter's expression/value by name (e.g. value=\"25 mm\" or a numeric expression). Then updates the document.")]
    public Task<string> SetParameter(string name, string value, CancellationToken ct = default)
        => Call("set_parameter", new JObject { ["name"] = name, ["value"] = value }, ct);

    [McpServerTool(Name = "inventor_create_parameter"),
     Description("Create a new user parameter on the active part document: name, expression, and unit (e.g. unit=mm, expression=\"10\").")]
    public Task<string> CreateParameter(string name, string expression, string unit, CancellationToken ct = default)
        => Call("create_parameter", new JObject { ["name"] = name, ["expression"] = expression, ["unit"] = unit }, ct);

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
