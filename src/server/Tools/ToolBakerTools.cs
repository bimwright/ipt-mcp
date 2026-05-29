using System.ComponentModel;
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
public sealed class ToolBakerTools
{
    private readonly InventorMcpConfig _config;

    public ToolBakerTools(InventorMcpConfig config)
    {
        _config = config;
    }

    [McpServerTool(Name = "inventor_list_baked_tools"), Description("List all verified, compiled, and registered baked Inventor tools.")]
    public string ListBakedTools()
    {
        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
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
        return JsonConvert.SerializeObject(new { tools }, Formatting.Indented);
    }

    [McpServerTool(Name = "inventor_list_bake_suggestions"), Description("List active ToolBaker suggestions generated from recurrent Inventor workflows.")]
    public string ListBakeSuggestions()
    {
        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
        return ListBakeSuggestionsHandler.Handle(db);
    }

    [McpServerTool(Name = "inventor_create_bake_issue_draft"), Description("Create a GitHub issue draft for a ToolBaker suggestion without submitting it.")]
    public string CreateBakeIssueDraft([Description("Suggestion id from inventor_list_bake_suggestions.")] string id)
    {
        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
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

        return JsonConvert.SerializeObject(new
        {
            ok = true,
            issue = new { title, body }
        }, Formatting.Indented);
    }
}
