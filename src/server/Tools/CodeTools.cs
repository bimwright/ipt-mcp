using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Bake;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.ToolBaker;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// The <c>send_code</c> escape hatch (toolset <c>code</c>, on by default, never exposed in
/// read-only mode). Runs a C# snippet in-process inside the Inventor add-in against
/// <c>Inventor.Application</c>. <c>--disable-send-code</c> removes the code tools;
/// <c>BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1</c> disables host execution.
/// Also owns the saved helper modules (S2) that scripts load
/// with <c>modules</c>, so recurring helpers are written once instead of re-sent on every call.
/// </summary>
[McpServerToolType]
[Toolset("code")]
public sealed class CodeTools
{
    private readonly PluginClient _client;
    private readonly CodeModuleStore _modules;
    private readonly ISet<string>? _routingTools;
    private readonly InventorMcpConfig? _config;

    public CodeTools(PluginClient client, CodeModuleStore modules, ServerState? state = null)
    {
        _client = client;
        _modules = modules;
        _config = state?.Config;
        if (state is not null)
            _routingTools = Program.ResolveToolTypesForRegistration(state.Config).SelectMany(t => t.GetMethods())
                .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
                .Where(a => a is not null && (!state.ReadOnly || a.ReadOnly))
                .Select(a => a!.Name!).ToHashSet(StringComparer.Ordinal);
    }

    [McpServerTool(Name = "inventor_send_code"),
     Description("Execute a C# script in-process within the Inventor add-in against Inventor.Application (global `app`) for workflows not covered by typed tools — prefer typed tools first. Enabled by default; --disable-send-code or the add-in kill switch disables it. Globals: app (Inventor.Application), doc (active Document, null when none is open). Pass a C# script body with statements and return, with optional class/helper declarations. Imports: System, System.Collections.Generic, System.Linq, Inventor. The active document is wrapped in one undoable transaction; runtime/host errors abort that transaction and host warnings are returned. Keep mutations within doc; document creation/closing, other documents and external file writes are outside that transaction and cannot be rolled back by it. Do not manage the wrapper transaction yourself.  Banned APIs (file/process/network/environment/dynamic-invocation) are rejected; typeof/GetType and fully-qualified System.IO.Path.{GetFileName,GetFileNameWithoutExtension,GetExtension,GetDirectoryName,Combine,ChangeExtension}(…) are allowed. File writes made through the Inventor API (SaveAs, SaveCopyAs, translators) are NOT restricted by the export-root policy. " +
        "Some Inventor calls have killed the whole Inventor process (try/catch cannot help; unsaved edits are lost, the call returns HOST_CRASHED): " +
        "reading DrawingViewAnnotation.SecondFormattedText on a detail view (section views are fine), and " +
        "MeasureTools.GetMinimumDistance between a TransientBRep body and an occurrence body (copy both with TransientBRep.Copy first; " +
        "two transient bodies measure fine). Save first; prefer inventor_measure_min_distance / inventor_check_interference. " +
                 "Returns result (the script's last expression / return value; >64 KiB spills to result_file) + captured stdout. modules: names of saved code modules (inventor_save_code_module) compiled in front of the script — call their functions instead of re-sending helper code; the response echoes {name, hash}. " +
                 "data: any JSON (max 512 KiB); when given, the script sees it as the variable `data` (Newtonsoft JToken; Newtonsoft.Json.Linq is imported) — do not declare your own `data` then. Pass coordinates, paths, member lists, specs and expected values here instead of inlining them into code, e.g. foreach (JObject m in data[\"members\"]) Tube((string)m[\"name\"], ...). compile_only=true compiles script + modules (+ data) without running or touching the document and returns diagnostics — use it before a long run and after fixing a compile error. Scripts over 4000 characters that inline data or helpers get an advisory size_hint. A script run with data may get a bake_hint: accept the suggestion to call it later with only the data, or use the baked tool it names.silent=true runs under Application.SilentOperation (Inventor answers prompts with defaults — use it for scripts that Save/Open/Close). timeout_ms (max 600000) overrides the per-call STA timeout. " +
                 "Errors carry diagnostics[{source: script|module:<name>, line, code, message, hint}] or, at runtime, location{source, line, text} + hint. " +
                 "result_contract=\"task_v1\" (default \"legacy\"): the script must return McpTaskResult.Pass(value) or McpTaskResult.Fail(code, message, details?) (imported automatically); the response carries _task{contract, status: passed|failed|not_reported, error{code, message}}, Fail sets ok=false, and no McpTaskResult fails with TASK_RESULT_REQUIRED. Fail does not undo edits the script already made. Use it when the script checks its own work. " +
                 SendCodeTaskGuide.Text +
                 "COM interop tips: lengths are cm internally (mm/10); collections are 1-based and COM items come back as object — type the loop variable (foreach (Document d in app.Documents)) or cast; look up by name with the indexed property occs.ItemByName[\"Part:1\"] (brackets); indexed properties take brackets or get_X(): body.Volume[0.0001]; UnitVector↔Vector via AsVector()/AsUnitVector(); PartDocument→Document via (Document)(object)part; Matrix has no operators (PostMultiplyBy).")]
    public Task<string> SendCode(string code, string[]? modules = null, int? timeout_ms = null, bool silent = false,
        [Description("JSON input; the script sees it as the variable `data` (JToken).")] System.Text.Json.JsonElement? data = null,
        bool compile_only = false,
        [Description("\"legacy\" (default) or \"task_v1\": return McpTaskResult.Pass/Fail and read _task.")] string result_contract = "legacy",
        [Description("\"project\" for real project work, \"test\" when testing or developing ipt-mcp itself, \"unknown\" (default). Recorded in the call journal only; does not change how the script runs.")] string usage_context = "unknown",
        CancellationToken ct = default)
        => result_contract is not ("legacy" or "task_v1")
            ? Task.FromResult(ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, "result_contract must be \"legacy\" or \"task_v1\""))
            : usage_context is not ("project" or "test" or "unknown")
            ? Task.FromResult(ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, "usage_context must be \"project\", \"test\" or \"unknown\""))
            : JsonArg.TryData(data, out var input, out var dataError)
            ? SendCodeCore(code, modules, timeout_ms, silent, input, compile_only, observeBake: true, ct, result_contract, usage_context)
            : Task.FromResult(ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, dataError!));

    /// <summary>send_code with a Newtonsoft data token; <paramref name="observeBake"/> is off when a baked script tool runs.</summary>
    internal async Task<string> SendCodeCore(string code, string[]? modules, int? timeout_ms, bool silent, JToken? input,
        bool compile_only, bool observeBake, CancellationToken ct, string resultContract = "legacy", string usageContext = "unknown")
    {
        var p = new JObject { ["code"] = code };
        var log = new JObject { ["code"] = code };
        // rvt-mcp parity: usage_context marks project work vs ipt-mcp testing in the journal. Only sent when set, so a
        // default request keeps its wire shape for older add-ins.
        if (usageContext != "unknown")
        {
            p["usage_context"] = usageContext;
            log["usage_context"] = usageContext;
        }
        var taskV1 = resultContract == "task_v1";
        if (taskV1)
        {
            // Only sent when set, so a legacy request keeps its wire shape for older add-ins.
            p["result_contract"] = resultContract;
            log["result_contract"] = resultContract;
        }
        if (timeout_ms is { } t) log["timeout_ms"] = t;
        if (silent)
        {
            p["silent"] = true;
            log["silent"] = true;
        }
        if (compile_only)
        {
            p["compile_only"] = true;
            log["compile_only"] = true;
        }
        if (input is not null)
        {
            var text = input.ToString(Newtonsoft.Json.Formatting.None);
            if (Encoding.UTF8.GetByteCount(text) > MaxDataBytes)
                return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT,
                    $"data exceeds {MaxDataBytes} bytes; split the work into several calls");
            // As text, so no Newtonsoft parse between here and the script prelude can re-type or round the values.
            p["data_json"] = text;
            log["data_length"] = text.Length;
            log["data_hash"] = SendCodeSource.Hash(text);
        }
        if (modules is { Length: > 0 })
        {
            if (!_modules.Resolve(modules, out var ordered, out var error))
                return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, error!);
            p["modules"] = new JArray(ordered.Select(m => new JObject
            {
                ["name"] = m.Name, ["hash"] = SendCodeSource.Hash(m.Code), ["code"] = m.Code,
            }));
            log["modules"] = new JArray(ordered.Select(m => new JObject
            {
                ["name"] = m.Name, ["hash"] = SendCodeSource.Hash(m.Code),
            }));
        }
        var started = System.Diagnostics.Stopwatch.StartNew();
        var response = await Call("send_code", p, ct, timeout_ms, log);
        var hint = SendCodeRouting.Match(code, modules is { Length: > 0 }, _routingTools);
        var sizeHint = SizeHint(code, input is not null, modules is { Length: > 0 });
        JObject? payload = null;
        try { payload = JToken.Parse(response) as JObject; } catch (Newtonsoft.Json.JsonException) { }
        // Baked script tools run as legacy send_code, so a task_v1 script is not observed for baking.
        var bakeHint = observeBake && !taskV1 && input is not null && !compile_only && _config is not null
            ? ScriptBake.Observe(_config, code, modules, input, payload?["ok"]?.Type == JTokenType.Boolean && (bool)payload["ok"]!, started.ElapsedMilliseconds)
            : null;
        if ((hint is null && sizeHint is null && bakeHint is null) || payload is null) return response;
        if (hint is not null) payload["routing_hint"] = hint;
        if (sizeHint is not null) payload["size_hint"] = sizeHint;
        if (bakeHint is not null) payload["bake_hint"] = bakeHint;
        return ToolResponse.Serialize(payload);
    }

    internal const int MaxDataBytes = 512 * 1024;
    internal const int SizeHintChars = 4000;

    /// <summary>Advisory only: large scripts that inline inputs or helpers are slow to send, retry and verify.</summary>
    internal static string? SizeHint(string? code, bool hasData, bool hasModules)
    {
        if (code is null || code.Length <= SizeHintChars || (hasData && hasModules)) return null;
        var missing = !hasData && !hasModules ? "data and modules" : hasData ? "modules" : "data";
        return $"Script is {code.Length} characters. Use {missing}: pass inputs (coordinates, paths, member lists, expected values) " +
               "as data, save reusable helpers once with inventor_save_code_module, check with compile_only=true, " +
               "and split long builds into smaller steps so a failure resends one step, not the whole script.";
    }

    [McpServerTool(Name = "inventor_save_code_module", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Save (or replace) a reusable C# helper module for inventor_send_code: declarations only — static methods, classes/records, constants — no top-level statements. Credential-like source values are rejected before storage. It is policy-checked and dry-compiled in the running Inventor add-in before it is stored, so errors surface now with module:<name> line numbers. requires: other saved modules this one calls (loaded automatically, dependencies first). Returns the module hash and its function signatures. name: [a-z][a-z0-9_]*, max 64 KiB.")]
    public async Task<string> SaveCodeModule(string name, string code, string? description = null, string[]? requires = null,
        CancellationToken ct = default)
    {
        var nameError = CodeModuleStore.ValidateName(name);
        if (nameError != null) return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, nameError);
        if (string.IsNullOrWhiteSpace(code)) return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, "code is required");
        var privacyError = CodeModuleStore.ValidateSourceForPersistence(code);
        if (privacyError != null) return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, privacyError);
        if (Encoding.UTF8.GetByteCount(code) > CodeModuleStore.MaxModuleBytes)
            return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, $"module exceeds {CodeModuleStore.MaxModuleBytes} bytes; split it");
        var policy = BakeCompilerPolicy.ValidateSource(code, "code module");
        if (!policy.Ok) return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, policy.Error ?? "module rejected by policy");

        var reqs = (requires ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        if (reqs.Contains(name, StringComparer.Ordinal))
            return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, "a module cannot require itself");
        if (!_modules.Resolve(new[] { name }, out var ordered, out var resolveError, (name, code, reqs)))
            return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, resolveError!);

        // Dry compile in the add-in: the module plus its dependencies, no script body.
        var p = new JObject
        {
            ["code"] = "",
            ["compile_only"] = true,
            ["modules"] = new JArray(ordered.Select(m => new JObject { ["name"] = m.Name, ["code"] = m.Code })),
        };
        var log = new JObject
        {
            ["compile_only"] = true,
            ["module"] = name,
            ["code"] = code,
            ["modules"] = new JArray(ordered.Select(m => new JObject { ["name"] = m.Name, ["hash"] = SendCodeSource.Hash(m.Code) })),
        };
        JToken data;
        try
        {
            data = await _client.SendAsync("send_code", p, ct, null, log);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, "module not saved — the add-in must dry-compile it first: " + ex.Message);
        }

        if (data is not JObject result || result["ok"]?.Value<bool>() != true)
        {
            var failed = data as JObject ?? new JObject();
            failed["saved"] = false;
            return ToolResponse.Serialize(failed);
        }

        var signatures = (result["signatures"] as JArray)?
            .OfType<JObject>().FirstOrDefault(s => (string?)s["module"] == name)?["signatures"];
        var entry = _modules.Save(name, code, description, reqs, signatures);
        return ToolResponse.Serialize(new JObject
        {
            ["ok"] = true,
            ["saved"] = true,
            ["name"] = entry.Name,
            ["hash"] = entry.Hash,
            ["bytes"] = entry.Bytes,
            ["requires"] = new JArray(entry.Requires),
            ["signatures"] = entry.Signatures ?? new JArray(),
        });
    }

    [McpServerTool(Name = "inventor_list_code_modules", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List saved send_code helper modules: name, hash, description, requires, bytes, saved_utc and the function/type signatures they declare — enough to call them from a script without reading the code. include_code=true also returns the source.")]
    public Task<string> ListCodeModules(bool include_code = false, CancellationToken ct = default)
    {
        var arr = new JArray();
        foreach (var e in _modules.List())
        {
            var item = new JObject
            {
                ["name"] = e.Name,
                ["hash"] = e.Hash,
                ["description"] = e.Description,
                ["requires"] = new JArray(e.Requires),
                ["bytes"] = e.Bytes,
                ["saved_utc"] = e.SavedUtc,
                ["signatures"] = e.Signatures ?? new JArray(),
            };
            if (include_code && _modules.TryGet(e.Name, out _, out var code)) item["code"] = code;
            arr.Add(item);
        }
        return Task.FromResult(ToolResponse.Serialize(new JObject { ["count"] = arr.Count, ["modules"] = arr }));
    }

    [McpServerTool(Name = "inventor_delete_code_module", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Deletes the stored module file; this has no Inventor undo. Delete a saved send_code helper module by name. Refused while another saved module lists it in requires.")]
    public Task<string> DeleteCodeModule(string name, CancellationToken ct = default)
    {
        var nameError = CodeModuleStore.ValidateName(name);
        if (nameError != null) return Task.FromResult(ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, nameError));
        if (_modules.Delete(name, out var blockedBy))
            return Task.FromResult(ToolResponse.Serialize(new JObject { ["ok"] = true, ["deleted"] = name }));
        return Task.FromResult(ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT,
            blockedBy != null ? $"module '{name}' is required by: {blockedBy}" : $"no saved module named '{name}'"));
    }

    [McpServerTool(Name = "inventor_ilogic_upsert_rule", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true),
     Description("Create or replace an internal iLogic rule's VB source. Requires send-code permission. Existing rule: replace text; run=true executes it once. New rule: AddRule compiles and runs it once, regardless of run, without a second invocation. Compile/runtime failures return an error with exceptions[]; rule side effects are not rolled back. Requires name and text; document selects a document already in memory.")]
    public Task<string> UpsertRule(string name, string text, string? document = null, bool run = false, bool silent = false, CancellationToken ct = default)
    {
        var p = new JObject { ["name"] = name, ["text"] = text, ["run"] = run, ["silent"] = silent };
        if (!string.IsNullOrWhiteSpace(document)) p["document"] = document;
        var log = (JObject)p.DeepClone();
        log.Remove("text");
        log["code"] = text;
        return Call("ilogic_upsert_rule", p, ct, null, log);
    }

    [McpServerTool(Name = "inventor_ilogic_run_snippet", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true),
     Description("Run arbitrary iLogic VB source under the send-code gate. Creates and deletes only this invocation\'s temporary rule; existing rules are never swept by name. Return a string via iProperties.Value(\"Custom\",\"MCP_RESULT\") = \"...\". Existing string values are restored; a non-string property collision fails before execution. Results over 65536 characters fail instead of being truncated. Historical log markers are not used as results. Native status, exceptions and cleanup failures produce an error with diagnostics. Rule side effects are not rolled back. document selects a document already in memory; silent=true suppresses host prompts. UNSUPPORTED_HOST without iLogic.")]
    public async Task<string> ILogicRunSnippet(string code, string? document = null, bool silent = false, int? timeout_ms = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["code"] = code };
        var log = new JObject { ["code"] = code };
        if (!string.IsNullOrWhiteSpace(document)) { p["document"] = document; log["document"] = document; }
        if (silent) { p["silent"] = true; log["silent"] = true; }
        return await Call("ilogic_run_snippet", p, ct, timeout_ms, log);
    }

    private async Task<string> Call(string command, JObject p, CancellationToken ct, int? timeoutMs = null, JObject? logParams = null)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct, timeoutMs, logParams);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex);
        }
    }
}
