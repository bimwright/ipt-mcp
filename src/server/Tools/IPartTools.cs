using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

[McpServerToolType, Toolset("ipart")]
public sealed class IPartTools
{
    private readonly PluginClient _client;
    public IPartTools(PluginClient client) => _client = client;
    [McpServerTool(Name="inventor_get_ipart_info",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Inspect an open part/factory/member, native table schema and rows, managed Excel binding and generated-member state. Read-only; no Excel automation or file generation. Writes support Standard iPart factories created by these tools. Custom iParts and sheet-metal authoring are not supported. max_rows=1..1000.")]
    public Task<string> GetInfo(string? document=null, int max_rows=100, int row_offset=0, CancellationToken ct=default)
        => Call("get_ipart_info",new JObject { ["document"]=document,["max_rows"]=max_rows,["row_offset"]=row_offset },ct);
    [McpServerTool(Name="inventor_create_ipart_factory",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Create a Standard iPart factory COPY from a clean saved open ordinary part. output_path must be a new local .ipt path. columns=[{name,kind:parameter|material|description|suppression,target?}]; parameter/suppression targets are existing parameter/feature names. Member and Part Number are included automatically. base_member is a file-safe identifier without extension. Creates/saves the copy and adjacent .ipart-mcp.json state; does not modify or save the source. Native embedded table editing requires Excel. Not allowed inside batch_execute.")]
    public Task<string> CreateFactory(string output_path,string base_member,JsonElement columns,string? document=null,CancellationToken ct=default)
        => Call("create_ipart_factory",new JObject { ["document"]=document,["output_path"]=output_path,["base_member"]=base_member,["columns"]=JToken.Parse(columns.GetRawText()) },ct);
    [McpServerTool(Name="inventor_update_ipart_table",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Preview/apply complete row upserts on a managed Standard iPart factory. rows=[{member,part_number,values:{configured_column:value}}]. Configured cells must be nonempty; clearing property cells is unsupported. Parameter numbers use the column's units; strings are Inventor expressions. Suppression values are compute/suppress or boolean true=suppress. Never removes rows. dry_run defaults true. Apply rebuilds changed rows and saves the managed factory; member regeneration is separate. Direct writes are refused once Excel owns the table. Not allowed in batch_execute.")]
    public Task<string> UpdateTable(JsonElement rows,bool dry_run=true,string? document=null,CancellationToken ct=default)
        => Call("update_ipart_table",new JObject { ["document"]=document,["rows"]=JToken.Parse(rows.GetRawText()),["dry_run"]=dry_run },ct);
    [McpServerTool(Name="inventor_set_ipart_row",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Select a member row by its exact name, rebuild/read back and save the managed factory. Does not change any assembly occurrence or generate a member file. Requires a clean factory and current synchronized Excel source. Not allowed in batch_execute.")]
    public Task<string> SetRow(string member,string? document=null,CancellationToken ct=default)
        => Call("set_ipart_row",new JObject { ["document"]=document,["member"]=member },ct);
    [McpServerTool(Name="inventor_generate_ipart_members",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Preview/generate 1..50 selected member names in a managed Standard iPart's native local cache. dry_run defaults true. Reuses only verified unchanged files; regenerates changed/pending rows, verifies body geometry and properties, and saves factory/member/checkpoints. Refuses unrelated existing files and dirty documents. Reports partial file effects and completed members on failure; file writes are not undone by Inventor transactions. Does not update assemblies automatically. Not allowed in batch_execute.")]
    public Task<string> GenerateMembers(string[] members,bool dry_run=true,string? document=null,CancellationToken ct=default)
        => Call("generate_ipart_members",new JObject { ["document"]=document,["members"]=new JArray(members),["dry_run"]=dry_run },ct);
    [McpServerTool(Name="inventor_sync_ipart_excel",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("One-way saved .xlsx catalog -> managed Standard iPart table. First call supplies path, sheet and optional header_cell (default A1), mapping={member:ExcelHeader,part_number:ExcelHeader,columnName:ExcelHeader,...}; omitted mapping uses identical headers. Later calls may reuse the persisted binding. Complete contiguous rows, <=1000, <=48 configured columns, <=16 MiB; no macros/external links. Save/recalculate Excel first. dry_run defaults true. Apply verifies native row geometry and persists the source binding/fingerprint; no Excel writeback, no row deletion, no member generation. Native edits replaced by source are included in the preview. Not allowed in batch_execute.")]
    public Task<string> SyncExcel(string? path=null,string? sheet=null,string? header_cell=null,JsonElement? mapping=null,bool dry_run=true,string? document=null,CancellationToken ct=default)
    {
        var p=new JObject { ["document"]=document,["path"]=path,["sheet"]=sheet,["header_cell"]=header_cell,["dry_run"]=dry_run };
        if(mapping.HasValue)p["mapping"]=JToken.Parse(mapping.Value.GetRawText());return Call("sync_ipart_excel",p,ct);
    }
    [McpServerTool(Name="inventor_place_ipart_member",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Place an already generated, verified Standard iPart member in an open assembly. factory_document identifies an already open managed factory; member is its exact row name. position_mm=[x,y,z] defaults origin; grounded defaults false. Verifies occurrence row/reference and assembly constraints. Does not save the assembly or generate files. Not allowed in batch_execute.")]
    public Task<string> PlaceMember(string factory_document,string member,double[]? position_mm=null,bool grounded=false,string? document=null,CancellationToken ct=default)
        => Call("place_ipart_member",new JObject { ["document"]=document,["factory_document"]=factory_document,["member"]=member,["position_mm"]=position_mm is null?null:new JArray(position_mm),["grounded"]=grounded },ct);
    [McpServerTool(Name="inventor_change_ipart_member",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Change one exact top-level assembly occurrence to an already generated verified row of its same managed Standard iPart factory. Rejects ambiguous/suppressed/custom occurrences and unhealthy constraints. Verifies row, reference and assembly constraint health; rolls back pre-commit failures and reports mutation_applied=true for a failure after commit. Does not save assembly or regenerate members. Not allowed in batch_execute.")]
    public Task<string> ChangeMember(string occurrence,string member,string? document=null,CancellationToken ct=default)
        => Call("change_ipart_member",new JObject { ["document"]=document,["occurrence"]=occurrence,["member"]=member },ct);
    private async Task<string> Call(string command,JObject p,CancellationToken ct)
    { try{return ToolResponse.Serialize(await _client.SendAsync(command,p,ct));}catch(InventorGatewayException ex){return ToolResponse.Error(ex);} }
}
