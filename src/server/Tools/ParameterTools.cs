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

    [McpServerTool(Name = "inventor_list_excel_tables", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List native spreadsheet parameter tables on an already-open part: table_id, linked, path, start_cell and parameter values/units/health. table_id identifies the linked path and start cell; duplicate links are ambiguous. Embedded tables can be listed but cannot be reloaded by these tools. Does not open Excel or modify the document.")]
    public Task<string> ListExcelTables(string? document = null, CancellationToken ct = default)
        => Call("list_excel_tables", new JObject { ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_link_excel_table", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Link a local .xlsx parameter table to an already-open part using native Inventor ParameterTables. First worksheet only; start_cell points to the first parameter row, not its header. Vertical columns: unique ASCII parameter name, finite saved numeric value (or saved formula result), explicit units (ul for unitless), optional comment. Max 1000 rows/16 MiB. No network paths, macros or external workbook links. Existing parameter-name collisions fail; an identical current link returns existing. Recomputes and verifies native values against saved workbook data; formulas must be recalculated/saved in Excel first. Failure aborts the owned document transaction and reports rollback state; the source workbook is never edited. Not available inside batch_execute.")]
    public Task<string> LinkExcelTable(string path, string start_cell = "A1", string? document = null, CancellationToken ct = default)
        => Call("link_excel_table", new JObject { ["path"] = path, ["start_cell"] = start_cell, ["document"] = document }, ct);

    [McpServerTool(Name = "inventor_reload_excel_table", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Reload one linked .xlsx table identified by list_excel_tables.table_id on an already-open part. Save/recalculate Excel first. Parameter names must remain unchanged. Reassigns the native link, recomputes, and compares parameter readback to saved numeric cells/formula caches; at most two attempts on the same unchanged workbook. Returns changed_parameters, source_sha256, attempts and verified_saved_values. Never claims success on stale readback. Failure aborts the owned document transaction and reports rollback state; workbook/external side effects are not undone. Does not save the part or edit Excel. Not available inside batch_execute.")]
    public Task<string> ReloadExcelTable(string table_id, string? document = null, CancellationToken ct = default)
        => Call("reload_excel_table", new JObject { ["table_id"] = table_id, ["document"] = document }, ct);

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
