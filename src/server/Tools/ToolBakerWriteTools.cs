using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Bake;
using Bimwright.Ipt.Server.Handlers;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Write-capable ToolBaker tools (toolset <c>toolbaker_write</c>, hidden in read-only mode). These run
/// baked tools through the add-in, accept suggestions (validate + compile + apply + persist), and
/// dismiss/snooze suggestions. Ported from nwd-mcp.
/// </summary>
[McpServerToolType]
public sealed class ToolBakerWriteTools
{
    private readonly PluginClient _client;
    private readonly InventorMcpConfig _config;

    public ToolBakerWriteTools(PluginClient client, InventorMcpConfig config)
    {
        _client = client;
        _config = config;
    }

    [McpServerTool(Name = "inventor_run_baked_tool"), Description("Execute a registered baked Inventor tool by name with JSON parameters.")]
    public async Task<string> RunBakedTool(string name, string paramsJson, CancellationToken ct)
    {
        JObject parsed;
        try
        {
            parsed = string.IsNullOrWhiteSpace(paramsJson) ? new JObject() : JObject.Parse(paramsJson);
        }
        catch (JsonException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = "INVALID_ARGUMENT", message = "params must be a JSON object: " + ex.Message } }, Formatting.Indented);
        }

        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
        var record = db.GetRegistryRecord(name);
        if (record == null)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = "INVALID_ARGUMENT", message = "baked tool not found: " + name } }, Formatting.Indented);
        }

        try
        {
            var data = await _client.SendAsync("run_baked_tool", new { name, @params = parsed, tool_record = JObject.FromObject(record) }, ct);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        catch (InventorGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.Indented);
        }
    }

    [McpServerTool(Name = "inventor_accept_bake_suggestion"), Description("Accept a suggested workflow to compile it into a verified, registered baked Inventor tool.")]
    public async Task<string> AcceptBakeSuggestion(string suggestionId, string desiredName, CancellationToken ct)
    {
        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
        return await AcceptBakeSuggestionHandler.HandleAsync(
            db,
            suggestionId,
            desiredName,
            outputChoice: "mcp_only",
            paramsSchema: null,
            pluginApply: async request =>
            {
                var data = await _client.SendAsync("apply_bake", request, ct);
                return data as JObject ?? new JObject();
            });
    }

    [McpServerTool(Name = "inventor_dismiss_bake_suggestion"), Description("Dismiss or snooze an active ToolBaker suggestion.")]
    public Task<string> DismissBakeSuggestion(string suggestionId, CancellationToken ct)
    {
        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
        return Task.FromResult(DismissBakeSuggestionHandler.Handle(db, suggestionId, "snooze_30d"));
    }
}
