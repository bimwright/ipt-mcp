using System.ComponentModel;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Server.Bake;
using Bimwright.Ipt.Server.Handlers;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Read-only ToolBaker tools (toolset <c>toolbaker</c>, available in read-only mode). These operate
/// purely on the server-side bake database and never round-trip to the add-in. Ported from nwd-mcp.
/// </summary>
[McpServerToolType]
[Toolset("toolbaker")]
public sealed class ToolBakerTools
{
    private readonly InventorMcpConfig _config;

    public ToolBakerTools(InventorMcpConfig config)
    {
        _config = config;
    }

    [McpServerTool(Name = "inventor_list_baked_tools", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List all verified, compiled, and registered baked Inventor tools.")]
    public string ListBakedTools()
    {
        if (!File.Exists(BakePaths.Db(_config))) return ToolResponse.Serialize(new { tools = new object[0] });
        using var db = new BakeDb(BakePaths.Db(_config), readOnly: true);
        var tools = db.ReadRegistryRecords()
            .Select(record => new
            {
                name = record.Name,
                description = record.Description,
                source = record.Source,
                handler_tool = record.HandlerTool,
                usage_count = record.UsageCount,
                created_at = record.CreatedAt
            })
            .ToArray();
        return ToolResponse.Serialize(new { tools });
    }

    [McpServerTool(Name = "inventor_list_bake_suggestions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List active ToolBaker suggestions generated from recurrent Inventor workflows.")]
    public string ListBakeSuggestions()
    {
        if (!File.Exists(BakePaths.Db(_config))) return ToolResponse.Serialize(new { suggestions = new object[0] });
        using var db = new BakeDb(BakePaths.Db(_config), readOnly: true);
        return ListBakeSuggestionsHandler.Handle(db);
    }

    [McpServerTool(Name = "inventor_create_bake_issue_draft", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Create a GitHub issue draft for a ToolBaker suggestion without submitting it.")]
    public string CreateBakeIssueDraft([Description("Suggestion id from inventor_list_bake_suggestions.")] string id)
    {
        if (!File.Exists(BakePaths.Db(_config))) return ToolResponse.Error("not_found", "Bake suggestion was not found.");
        using var db = new BakeDb(BakePaths.Db(_config), readOnly: true);
        var suggestion = db.GetSuggestion(id);
        if (suggestion == null)
        {
            return JsonConvert.SerializeObject(new { ok = false, error_code = "not_found", message = "Bake suggestion was not found." });
        }

        var title = "[ToolBaker] " + (suggestion.Title ?? suggestion.Id);
        var body = string.Join("\n", new[]
        {
            "## Summary",
            suggestion.Description ?? "Repeated Inventor workflow detected.",
            "",
            "## Suggestion",
            "- id: `" + suggestion.Id + "`",
            "- source: `" + suggestion.Source + "`",
            "- score: `" + suggestion.Score + "`",
            "",
            "## Payload",
            "```json",
            suggestion.PayloadJson ?? "{}",
            "```"
        });

        return ToolResponse.Serialize(new
        {
            ok = true,
            issue = new { title, body }
        });
    }
}
