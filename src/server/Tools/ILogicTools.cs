using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// Toolset <c>ilogic</c> — the iLogic bridge: list/get/run customer rules in the target document,
/// read the file-backed iLogic log, and upsert rule text, plus generic Inventor command
/// enumeration/execution and undo. Read tools keep <c>ReadOnly=true</c>; run/upsert tools are
/// writes. Arbitrary-VB <c>ilogic_run_snippet</c> deliberately lives in the <c>code</c> toolset so
/// it rides the exact same send-code gate as <c>inventor_send_code</c>. Degrades to
/// UNSUPPORTED_HOST on Inventor LT (no iLogic).
/// </summary>
[McpServerToolType]
[Toolset("ilogic")]
public sealed class ILogicTools
{
    private readonly PluginClient _client;

    public ILogicTools(PluginClient client) { _client = client; }

    [McpServerTool(Name = "inventor_ilogic_list_rules", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List iLogic rules stored in the target document: {name, is_active, is_codeblocks, scope:\"internal\"}. External file-backed rules are not enumerable — run them by name with inventor_ilogic_run_external_rule. document: name/path of a document already in memory (default: active). UNSUPPORTED_HOST on Inventor LT.")]
    public Task<string> ListRules(string? document = null, CancellationToken ct = default)
        => Call("ilogic_list_rules", Doc(document), ct);

    [McpServerTool(Name = "inventor_ilogic_get_rule", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Read one internal iLogic rule's VB source: {name, scope, text}. name required; missing rule → INVALID_ARGUMENT. document scopes to a document already in memory.")]
    public Task<string> GetRule(string name, string? document = null, CancellationToken ct = default)
        => Call("ilogic_get_rule", Doc(document, ("name", name)), ct);

    [McpServerTool(Name = "inventor_ilogic_read_log", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Read the tail of the file-backed iLogic log (LogControl.FullFileName, session-wide — not document-scoped). max_chars: tail length (default 65536, 0 = whole file). Text is secret-masked. The file log is only populated when this machine's iLogicLogger.config enables file output — when empty, rule diagnostics still arrive in the returned exceptions[] array (iLogic's structured RuleExceptionsLog: rule, line, context, message).")]
    public Task<string> ReadLog(int? max_chars = null, CancellationToken ct = default)
    {
        var p = new JObject();
        if (max_chars is { } m) p["max_chars"] = m;
        return Call("ilogic_read_log", p, ct);
    }

    [McpServerTool(Name = "inventor_ilogic_run_rule", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true),
     Description("Run an internal iLogic rule already stored in the target document (the rule's own side effects apply — parameters, features, saves). name required; arguments: flat object → Inventor NameValueMap for RunRuleWithArguments (readable in the rule via RuleArguments(\"key\")); document scopes to a document in memory; silent=true suppresses host prompts. Rule errors surface in exceptions[] (iLogic RuleExceptionsLog).")]
    public Task<string> RunRule(string name, string? document = null, JObject? arguments = null, bool silent = false, CancellationToken ct = default)
        => Call("ilogic_run_rule", Doc(document, ("name", name), ("arguments", arguments), ("silent", silent)), ct);

    [McpServerTool(Name = "inventor_ilogic_run_external_rule", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true),
     Description("Run a file-backed (external) iLogic rule by name — iLogic resolves it via the document's external-rule search order. Same arguments/document/silent parameters as inventor_ilogic_run_rule.")]
    public Task<string> RunExternalRule(string name, string? document = null, JObject? arguments = null, bool silent = false, CancellationToken ct = default)
        => Call("ilogic_run_external_rule", Doc(document, ("name", name), ("arguments", arguments), ("silent", silent)), ct);

    [McpServerTool(Name = "inventor_ilogic_upsert_rule", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Create or replace an internal iLogic rule's VB text in the target document. Existing rule: sets text without running (auto_ran:false). New rule: iLogic's AddRule compiles AND runs it once (auto_ran:true — cannot be suppressed). run=true additionally runs the rule after upsert. Compile/runtime errors surface in exceptions[] (iLogic RuleExceptionsLog). Requires: name, text.")]
    public Task<string> UpsertRule(string name, string text, string? document = null, bool run = false, bool silent = false, CancellationToken ct = default)
        => Call("ilogic_upsert_rule", Doc(document, ("name", name), ("text", text), ("run", run), ("silent", silent)), ct);

    [McpServerTool(Name = "inventor_list_commands", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Enumerate Inventor's live CommandManager.ControlDefinitions — internal_name (for inventor_run_command), display_name, type. filter: case-insensitive substring on either name; max_items caps output (default 200); truncated reports overflow.")]
    public Task<string> ListCommands(string? filter = null, int? max_items = null, CancellationToken ct = default)
    {
        var p = new JObject();
        if (!string.IsNullOrWhiteSpace(filter)) p["filter"] = filter;
        if (max_items is { } m) p["max_items"] = m;
        return Call("list_commands", p, ct);
    }

    [McpServerTool(Name = "inventor_run_command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Execute an Inventor control definition by internal name (Execute2), e.g. AppNewPartCmd — find names with inventor_list_commands. Many commands are inherently modal; use silent=true to let Inventor answer prompts with defaults.")]
    public Task<string> RunCommand(string name, bool silent = false, CancellationToken ct = default)
        => Call("run_command", Doc(null, ("name", name), ("silent", silent)), ct);

    [McpServerTool(Name = "inventor_undo", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Undo one or more Inventor operations on the active document (AppUndoCmd, Ctrl+Z equivalent). steps: 1-20 (default 1). Returns undone count — an empty undo stack reports 0, not an error.")]
    public Task<string> Undo(int steps = 1, CancellationToken ct = default)
        => Call("undo", new JObject { ["steps"] = steps }, ct);

    private static JObject Doc(string? document, params (string Key, object? Value)[] extra)
    {
        var p = new JObject();
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        foreach (var (k, v) in extra)
        {
            if (v is null) continue;
            if (v is bool b) { if (b) p[k] = true; }
            else p[k] = JToken.FromObject(v);
        }
        return p;
    }

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
