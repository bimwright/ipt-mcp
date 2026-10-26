using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Bake;
using Bimwright.Ipt.Server.Handlers;
using Bimwright.Ipt.Shared.Contracts;
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
[Toolset("toolbaker_write")]
public sealed class ToolBakerWriteTools
{
    private readonly PluginClient _client;
    private readonly InventorMcpConfig _config;
    private readonly CodeModuleStore? _modules;
    private readonly ServerState? _state;

    public ToolBakerWriteTools(PluginClient client, InventorMcpConfig config, CodeModuleStore? modules = null, ServerState? state = null)
    {
        _client = client;
        _config = config;
        _modules = modules;
        _state = state;
    }

    [McpServerTool(Name = "inventor_run_baked_tool", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Execute a registered baked Inventor tool by name with JSON parameters and update its local usage statistics. A script baked tool (source=script, baked from a send_code script) runs that script with paramsJson as its `data` (any JSON) under the same gates as inventor_send_code: it is refused when send_code is disabled or the connection is read-only.")]
    public async Task<string> RunBakedTool(string name, string paramsJson, CancellationToken ct)
    {
        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
        var record = db.GetRegistryRecord(name);
        if (record == null)
        {
            return ToolResponse.Error("INVALID_ARGUMENT", "baked tool not found: " + name);
        }
        if (record.Source == ScriptBake.Source)
            return await RunScript(record, paramsJson, ct);

        JObject parsed;
        try
        {
            parsed = string.IsNullOrWhiteSpace(paramsJson) ? new JObject() : WireJson.LoadObject(paramsJson);
        }
        catch (JsonException ex)
        {
            return ToolResponse.Error("INVALID_ARGUMENT", "params must be a JSON object: " + ex.Message);
        }

        try
        {
            var data = await _client.SendAsync("run_baked_tool", new { name, @params = parsed, tool_record = JObject.FromObject(record) }, ct);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }

    /// <summary>A script tool is send_code with the params as data — same server and add-in gates, no bake hint.</summary>
    private async Task<string> RunScript(Bimwright.Ipt.Shared.ToolBaker.BakedToolRecord record, string paramsJson, CancellationToken ct)
    {
        if (!_config.EnableSendCode || (_state?.ReadOnly ?? _config.ReadOnly))
            return ToolResponse.Error(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.SEND_CODE_DISABLED,
                $"'{record.Name}' is a script baked tool and runs as send_code, which is disabled on this connection");
        if (_modules == null || !Bimwright.Ipt.Shared.ToolBaker.BakedToolRuntimeSource.TryReadScript(record.SourceCode, out var code, out var modules, out _))
            return ToolResponse.Error("INVALID_ARGUMENT", $"baked tool '{record.Name}' has no runnable script");
        if (!JsonArg.TryData(paramsJson, out var data, out var dataError))
            return ToolResponse.Error("INVALID_ARGUMENT", "paramsJson becomes the script's data: " + dataError);
        return await new CodeTools(_client, _modules, _state).SendCodeCore(code, modules.Length > 0 ? modules : null,
            timeout_ms: null, silent: false, data, compile_only: false, observeBake: false, ct);
    }

    [McpServerTool(Name = "inventor_accept_bake_suggestion", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Accept a suggested workflow to compile it into a verified, registered baked Inventor tool. For a script suggestion (source=script, opened when one send_code script succeeded with several different data inputs) pass code and modules exactly as in those send_code calls: only their hash was recorded, so the server checks the match and stores the script only now. The baked tool is then called with inventor_run_baked_tool(name, paramsJson=<data>).")]
    public async Task<string> AcceptBakeSuggestion(string suggestionId, string desiredName, string? code = null, string[]? modules = null, CancellationToken ct = default)
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
            },
            code: code,
            modules: modules);
    }

    [McpServerTool(Name = "inventor_dismiss_bake_suggestion", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false), Description("Dismiss or snooze an active ToolBaker suggestion.")]
    public Task<string> DismissBakeSuggestion(string suggestionId, CancellationToken ct)
    {
        BakePaths.EnsureDir(_config);
        using var db = new BakeDb(BakePaths.Db(_config));
        db.Migrate();
        return Task.FromResult(DismissBakeSuggestionHandler.Handle(db, suggestionId, "snooze_30d"));
    }
}
