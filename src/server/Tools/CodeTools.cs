using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.ToolBaker;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// The opt-in <c>send_code</c> escape hatch (toolset <c>code</c>, off by default, never exposed in
/// read-only mode). Runs a C# snippet in-process inside the Inventor add-in against
/// <c>Inventor.Application</c>. Requires both server (<c>--enable-send-code</c> /
/// <c>BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1</c>) and add-in
/// (<c>BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1</c>) opt-in; the dispatcher returns
/// <c>SEND_CODE_DISABLED</c> otherwise. Also owns the saved helper modules (S2) that scripts load
/// with <c>modules</c>, so recurring helpers are written once instead of re-sent on every call.
/// </summary>
[McpServerToolType]
[Toolset("code")]
public sealed class CodeTools
{
    private readonly PluginClient _client;
    private readonly CodeModuleStore _modules;

    public CodeTools(PluginClient client, CodeModuleStore modules)
    {
        _client = client;
        _modules = modules;
    }

    [McpServerTool(Name = "inventor_send_code"),
     Description("Execute a C# script in-process within the Inventor add-in against Inventor.Application (global `app`) for workflows not covered by typed tools — prefer typed tools first. Enabled by default; --disable-send-code or the add-in kill switch disables it. Globals: app (Inventor.Application), doc (active Document, null when none is open). Pass a C# script body with statements and return, with optional class/helper declarations. Imports: System, System.Collections.Generic, System.Linq, Inventor. The active document is wrapped in one undoable transaction; runtime/host errors abort that transaction and host warnings are returned. Keep mutations within doc; document creation/closing, other documents and external file writes are outside that transaction and cannot be rolled back by it. Do not manage the wrapper transaction yourself.  Banned APIs (file/process/network/environment/dynamic-invocation) are rejected; typeof/GetType and fully-qualified System.IO.Path.{GetFileName,GetFileNameWithoutExtension,GetExtension,GetDirectoryName,Combine,ChangeExtension}(…) are allowed. File writes made through the Inventor API (SaveAs, SaveCopyAs, translators) are NOT restricted by the export-root policy. " +
                 "Returns result (the script's last expression / return value; >64 KiB spills to result_file) + captured stdout. modules: names of saved code modules (inventor_save_code_module) compiled in front of the script — call their functions instead of re-sending helper code; the response echoes {name, hash}. silent=true runs under Application.SilentOperation (Inventor answers prompts with defaults — use it for scripts that Save/Open/Close). timeout_ms (max 600000) overrides the per-call STA timeout. " +
                 "Errors carry diagnostics[{source: script|module:<name>, line, code, message, hint}] or, at runtime, location{source, line, text} + hint. " +
                 "COM interop tips: lengths are cm internally (mm/10); collections are 1-based and COM items come back as object — type the loop variable (foreach (Document d in app.Documents)) or cast; look up by name with the indexed property occs.ItemByName[\"Part:1\"] (brackets); indexed properties take brackets or get_X(): body.Volume[0.0001]; UnitVector↔Vector via AsVector()/AsUnitVector(); PartDocument→Document via (Document)(object)part; Matrix has no operators (PostMultiplyBy).")]
    public async Task<string> SendCode(string code, string[]? modules = null, int? timeout_ms = null, bool silent = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["code"] = code };
        var log = new JObject { ["code"] = code };
        if (timeout_ms is { } t) log["timeout_ms"] = t;
        if (silent)
        {
            p["silent"] = true;
            log["silent"] = true;
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
        return await Call("send_code", p, ct, timeout_ms, log);
    }

    [McpServerTool(Name = "inventor_save_code_module", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Save (or replace) a reusable C# helper module for inventor_send_code: declarations only — static methods, classes/records, constants — no top-level statements. It is policy-checked and dry-compiled in the running Inventor add-in before it is stored, so errors surface now with module:<name> line numbers. requires: other saved modules this one calls (loaded automatically, dependencies first). Returns the module hash and its function signatures. name: [a-z][a-z0-9_]*, max 64 KiB.")]
    public async Task<string> SaveCodeModule(string name, string code, string? description = null, string[]? requires = null,
        CancellationToken ct = default)
    {
        var nameError = CodeModuleStore.ValidateName(name);
        if (nameError != null) return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, nameError);
        if (string.IsNullOrWhiteSpace(code)) return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, "code is required");
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

    private async Task<string> Call(string command, JObject p, CancellationToken ct, int? timeoutMs = null, JObject? logParams = null)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct, timeoutMs, logParams);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }
}
