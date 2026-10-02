using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Model parameter tools (toolset <c>parameters</c>). Lists/reads/sets user and model parameters of
/// the active part document, and creates new user parameters. Thin wrappers over wire commands.
/// </summary>
[McpServerToolType]
[Toolset("parameters")]
public sealed class ParameterTools
{
    private readonly PluginClient _client;
    public ParameterTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_list_parameters", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List the active part document's parameters (model + user): name, expression, evaluated value, unit, and kind. Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> ListParameters(string? document = null, CancellationToken ct = default)
        => Call("list_parameters", new JObject { ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_get_parameter", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get a single parameter of the active part document by name: expression, evaluated value, and unit. Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> GetParameter(string name, string? document = null, CancellationToken ct = default)
        => Call("get_parameter", new JObject { ["name"] = name, ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_set_parameter", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Set an existing parameter's expression/value by name (e.g. value=\"25 mm\" or a numeric expression). Then updates the document. Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> SetParameter(string name, string value, string? document = null, CancellationToken ct = default)
        => Call("set_parameter", new JObject { ["name"] = name, ["value"] = value, ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_create_parameter", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Create a new user parameter on the active part document: name, expression, and unit (e.g. unit=mm, expression=\"10\"). Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> CreateParameter(string name, string expression, string unit, string? document = null, CancellationToken ct = default)
        => Call("create_parameter", new JObject { ["name"] = name, ["expression"] = expression, ["unit"] = unit, ["document"] = document }, ct);

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
