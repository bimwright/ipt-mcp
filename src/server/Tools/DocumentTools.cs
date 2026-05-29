using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Tools;

/// <summary>
/// Core/document tools. Registered under BOTH the <c>query</c> and <c>document</c> toolsets
/// (Program.ResolveToolTypesForRegistration maps both → this type and de-dups). The read-only
/// query methods (<c>list_open_documents</c>, <c>get_document_info</c>) and the mutating document
/// methods all live here. Each method is a thin wrapper that ships a wire envelope to the add-in.
/// </summary>
[McpServerToolType]
public sealed class DocumentTools
{
    private readonly PluginClient _client;
    public DocumentTools(PluginClient client) => _client = client;

    // ---- query (read-only) ----

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

    // ---- document (write) ----

    [McpServerTool(Name = "inventor_new_part"),
     Description("Create a new part document (.ipt). Optional template path; omit to use the default standard part template.")]
    public Task<string> NewPart(string? template = null, CancellationToken ct = default)
        => Call("new_part", new JObject { ["template"] = template }, ct);

    [McpServerTool(Name = "inventor_new_assembly"),
     Description("Create a new assembly document (.iam). Optional template path; omit to use the default standard assembly template.")]
    public Task<string> NewAssembly(string? template = null, CancellationToken ct = default)
        => Call("new_assembly", new JObject { ["template"] = template }, ct);

    [McpServerTool(Name = "inventor_open_document"),
     Description("Open an existing Inventor document from a full file path and make it the active document.")]
    public Task<string> OpenDocument(string path, CancellationToken ct = default)
        => Call("open_document", new JObject { ["path"] = path }, ct);

    [McpServerTool(Name = "inventor_save_document"),
     Description("Save the active document. Provide a path to Save-As to that location; omit to save in place (fails if the document was never saved).")]
    public Task<string> SaveDocument(string? path = null, CancellationToken ct = default)
        => Call("save_document", new JObject { ["path"] = path }, ct);

    [McpServerTool(Name = "inventor_close_document"),
     Description("Close the active document. save=true saves before closing; save=false (default) discards unsaved changes.")]
    public Task<string> CloseDocument(bool save = false, CancellationToken ct = default)
        => Call("close_document", new JObject { ["save"] = save }, ct);

    [McpServerTool(Name = "inventor_set_units"),
     Description("Set the active document's length unit. length_unit is a unit name such as mm, cm, m, in, or ft.")]
    public Task<string> SetUnits(string lengthUnit, CancellationToken ct = default)
        => Call("set_units", new JObject { ["length_unit"] = lengthUnit }, ct);

    [McpServerTool(Name = "inventor_set_material"),
     Description("Assign a material to the active part document by material name (must exist in the document's material library).")]
    public Task<string> SetMaterial(string materialName, CancellationToken ct = default)
        => Call("set_material", new JObject { ["material_name"] = materialName }, ct);

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
