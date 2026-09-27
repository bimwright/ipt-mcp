using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Write-capable document tools. Read-only document/query probes live in <see cref="QueryTools"/> so
/// <c>--read-only</c> can hide this entire toolset by type.
/// </summary>
[McpServerToolType]
public sealed class DocumentTools
{
    private readonly PluginClient _client;
    public DocumentTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_new_part"),
     Description("Create a new part document (.ipt). Optional template path; omit to use the default standard part template.")]
    public Task<string> NewPart(string? template = null, CancellationToken ct = default)
        => Call("new_part", new JObject { ["template"] = template }, ct);

    [McpServerTool(Name = "inventor_new_assembly"),
     Description("Create a new assembly document (.iam). Optional template path; omit to use the default standard assembly template.")]
    public Task<string> NewAssembly(string? template = null, CancellationToken ct = default)
        => Call("new_assembly", new JObject { ["template"] = template }, ct);

    [McpServerTool(Name = "inventor_open_document"),
     Description("Open an existing Inventor document from a full file path and make it the active document. visible=false loads it without a window (for batch reads/edits via the document parameter of other tools). Runs silently by default (silent=true: Inventor answers its own prompts with their defaults instead of showing a blocking dialog).")]
    public Task<string> OpenDocument(string path, bool visible = true, bool silent = true, CancellationToken ct = default)
        => Call("open_document", new JObject { ["path"] = path, ["visible"] = visible, ["silent"] = silent }, ct);

    [McpServerTool(Name = "inventor_save_document"),
     Description("Save a document: the active one, or document (full path or name of an open/loaded document). Provide path to Save-As to that location; omit to save in place (fails if the document was never saved). Runs silently by default (silent=true auto-answers prompts such as 'save referenced documents?' with their defaults so the call cannot hang on a hidden dialog).")]
    public Task<string> SaveDocument(string? path = null, string? document = null, bool silent = true, CancellationToken ct = default)
        => Call("save_document", new JObject { ["path"] = path, ["document"] = document, ["silent"] = silent }, ct);

    [McpServerTool(Name = "inventor_close_document"),
     Description("Close a document: the active one, or document (full path or name of an open document). save=true saves before closing; save=false (default) discards unsaved changes. Runs silently by default.")]
    public Task<string> CloseDocument(bool save = false, string? document = null, bool silent = true, CancellationToken ct = default)
        => Call("close_document", new JObject { ["save"] = save, ["document"] = document, ["silent"] = silent }, ct);

    [McpServerTool(Name = "inventor_set_units"),
     Description("Set the active document's length unit. length_unit is a unit name such as mm, cm, m, in, or ft.")]
    public Task<string> SetUnits(string lengthUnit, CancellationToken ct = default)
        => Call("set_units", new JObject { ["length_unit"] = lengthUnit }, ct);

    [McpServerTool(Name = "inventor_set_material"),
     Description("Assign a material to the active part document by material name (must exist in the document's material library). Optional document: full path or name of a document already open or loaded in Inventor (e.g. a part referenced by the open assembly) — never opened or activated; omit for the active document.")]
    public Task<string> SetMaterial(string materialName, string? document = null, CancellationToken ct = default)
        => Call("set_material", new JObject { ["material_name"] = materialName, ["document"] = document }, ct);

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
