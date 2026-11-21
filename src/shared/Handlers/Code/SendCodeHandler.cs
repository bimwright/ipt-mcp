#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Security;
using Bimwright.Ipt.Shared.ToolBaker;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Newtonsoft.Json.Linq;
using InvApi = global::Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Code;

/// <summary>
/// <c>send_code</c> — the opt-in C# scripting escape hatch. Runs a snippet in-process against
/// <c>Inventor.Application</c> (exposed as the <c>app</c> global). Gated two ways: the dispatcher returns
/// <c>SEND_CODE_DISABLED</c> unless the add-in opted in (so this Execute only runs when enabled), and the
/// source must pass <see cref="BakeCompilerPolicy"/> (no file/process/network/environment APIs,
/// no dynamic invocation — type-metadata reads like <c>typeof</c>/<c>GetType</c> are allowed).
///
/// Optional <c>modules</c> ([{name, hash, code}], resolved server-side from saved code modules) are
/// compiled in front of the script as one submission; <see cref="SendCodeSource"/> tags every part with
/// a <c>#line</c> directive so diagnostics and runtime failures name <c>module:&lt;name&gt;</c> or
/// <c>script</c> plus the line. <c>compile_only</c> compiles without running (module dry-compile) and
/// returns the modules' declared signatures. Optional <c>data</c> (any JSON) reaches the script as a
/// <c>data</c> variable (<c>JToken</c>) declared by a prelude part (source <c>data</c>), so inputs travel
/// as data instead of being inlined into source; without <c>data</c> no prelude is added.
/// <c>silent</c> wraps the run in
/// <c>Application.SilentOperation</c> (opt-in: dialogs are auto-answered with their defaults).
/// Mirrors nwd-mcp's SendCodeHandler.
/// </summary>
public sealed class SendCodeHandler : IInventorCommand
{
    public string Name => "send_code";
    public bool IsReadOnly => false;

    private const int MaxDiagnostics = 10;

    public class Globals
    {
        public InvApi.Application app = null!;
        public InvApi.Document? doc;
        /// <summary>
        /// Caller-supplied <c>data</c> as JSON text. A string, not a JToken: Inventor loads its own
        /// Newtonsoft.Json, and a JToken field binds to whichever copy the script resolves at run time
        /// (MissingFieldException). <see cref="DataPrelude"/> parses it with the script's own copy.
        /// </summary>
        public string? __data_json;
    }

    private const string DataSource = "data";
    // Same reader settings as the server's JsonArg.TryData: ISO date strings stay strings (JToken.Parse would make them
    // Date tokens and lose format/offset), floats are doubles.
    private const string DataPrelude = "Newtonsoft.Json.Linq.JToken data = Newtonsoft.Json.Linq.JToken.Load(new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(__data_json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None, FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Double });";

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };

        // Defense-in-depth: even though the dispatcher gates send_code, refuse to run if not enabled.
        if (!ctx.EnableSendCode)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.SEND_CODE_DISABLED,
                "send_code is disabled by the add-in kill switch BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE", meta);

        var app = ctx.Application as InvApi.Application;
        if (app is null)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.API_ERROR, "Inventor.Application is not available", meta);

        var compileOnly = p["compile_only"]?.Type == JTokenType.Boolean && (bool)p["compile_only"]!;
        var silent = p["silent"]?.Type == JTokenType.Boolean && (bool)p["silent"]!;
        var rawNumbers = p["raw_numbers"]?.Type == JTokenType.Boolean && (bool)p["raw_numbers"]!;
        var code = (string?)p["code"] ?? "";
        if (string.IsNullOrWhiteSpace(code) && !compileOnly)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "code parameter is required", meta);

        if (!TryReadModules(p["modules"], out var modules, out var moduleEcho, out var moduleError))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, moduleError!, meta);
        if (!SendCodeTask.TryReadContract(p["result_contract"], out var contract, out var contractError))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, contractError!, meta);

        // Banned-API source policy (shared with ToolBaker), per part so the message names the source.
        foreach (var m in modules)
        {
            var mp = BakeCompilerPolicy.ValidateSource(m.Code, "send_code " + m.Source);
            if (!mp.Ok)
                return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, mp.Error ?? "module rejected by policy", meta);
        }
        var policy = BakeCompilerPolicy.ValidateSource(code, "send_code");
        if (!policy.Ok)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, policy.Error ?? "send_code source rejected by policy", meta);

        // data_json is the exact text from the server; a data object comes from an older server (already re-parsed on the way).
        var dataJson = p["data_json"]?.Type == JTokenType.String ? (string?)p["data_json"]
            : p["data"] is { Type: not JTokenType.Null } d ? d.ToString(Newtonsoft.Json.Formatting.None) : null;
        var parts = new Dictionary<string, string>(StringComparer.Ordinal) { [SendCodeSource.ScriptSource] = code };
        foreach (var m in modules) parts[m.Source] = m.Code;
        var sourceParts = modules;
        if (dataJson != null)
        {
            parts[DataSource] = DataPrelude;
            sourceParts = new List<SendCodeSource.Part> { new(DataSource, DataPrelude) };
            sourceParts.AddRange(modules);
        }
        // Every loop body starts with an IptGuard.Check (same line, so line numbers hold): loops stop at the time budget.
        var source = ScriptLoopGuard.Instrument(SendCodeSource.Build(sourceParts, code));

        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        var previousSilent = false;
        var silentApplied = false;
        InvApi.Transaction? transaction = null;
        InvApi.MessageSection? messages = null;
        var rolledBack = false;
        string? hostMessages = null;
        var hostWarnings = false;
        var skipped = Array.Empty<SkippedScriptReference>();
        Script<object>? script = null;

        try
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .ToArray();
            // rvt-mcp #38: a loaded DLL whose metadata Roslyn cannot read is left out instead of failing every compile with CS0009.
            var refs = ScriptReferences.FromPaths(loaded.Select(a => a.Location).Distinct(StringComparer.OrdinalIgnoreCase), out skipped);

            // The add-in loads into a private AssemblyLoadContext (EnableDynamicLoading), so the
            // Globals type and the Inventor interop live in that context. Roslyn's default loader
            // would reload those assemblies from disk into its own context, binding the compiled
            // script to a *different* Globals type than the instance we pass in -> InvalidCastException.
            // Register the already-loaded assemblies so the script binds to the very same types.
            var loader = new InteractiveAssemblyLoader();
            foreach (var asm in loaded)
            {
                try { loader.RegisterDependency(asm); } catch { /* skip identity collisions */ }
            }

            // Debug info makes runtime stack frames carry the #line source + line (S1.4).
            // Bimwright.Ipt.Scripting brings the Ipt helpers and McpTaskResult into every script (script_helpers_v1).
            var imports = new List<string> { "System", "System.Collections.Generic", "System.Linq", "Newtonsoft.Json.Linq", "Inventor", SendCodeTask.ScriptNamespace };
            var options = ScriptOptions.Default
                .WithReferences(refs)
                .WithImports(imports)
                .WithEmitDebugInformation(true)
                .WithFileEncoding(Encoding.UTF8);

            script = CSharpScript.Create(source, options, typeof(Globals), loader);

            if (compileOnly)
            {
                var diags = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                var shapeError = modules.Select(m => ModuleShapeError(m)).FirstOrDefault(e => e != null);
                var data = new JObject
                {
                    ["ok"] = diags.Length == 0 && shapeError == null,
                    ["error"] = diags.Length > 0 ? CompileErrorText(diags) + SkippedNote(skipped) : shapeError,
                    ["compile_only"] = true,
                    ["modules"] = moduleEcho,
                };
                if (diags.Length > 0) AttachDiagnostics(data, diags, script.GetCompilation());
                else data["signatures"] = Signatures(modules);
                AttachSkipped(data, skipped);
                SendCodeTask.Apply(data, contract, completed: false, null);
                return InventorCommandResult.Success(Guid.Empty, data, meta);
            }

            if (silent)
            {
                previousSilent = app.SilentOperation;
                app.SilentOperation = true;
                silentApplied = true;
            }

            // No handler-side CTS: a CancellationToken cannot interrupt a synchronous script
            // running on the STA thread anyway. The add-in's task.Wait(env.TimeoutMs) is the
            // single owner of the timeout (spec F2-b).
            // Compile before opening a transaction; a compile failure cannot dirty the model.
            var compileDiagnostics = script.Compile();
            if (compileDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new CompilationErrorException("Script compilation failed.", compileDiagnostics);
            messages = app.ErrorManager.StartMessageSection();
            var document = app.ActiveDocument;
            if (document != null) transaction = app.TransactionManager.StartTransaction(document, "MCP: send_code");
            global::Bimwright.Ipt.Scripting.Ipt.App = app;
            global::Bimwright.Ipt.Scripting.IptGuard.Begin(SendCodeHelpers.BudgetMs(ctx.DeadlineUtc, DateTime.UtcNow));
            var state = script.RunAsync(globals: new Globals { app = app, doc = document, __data_json = dataJson }).GetAwaiter().GetResult();
            // A script that caught the guard's stop still ran out of time: roll back and report it like an uncaught stop.
            global::Bimwright.Ipt.Scripting.IptGuard.ThrowIfTripped();
            hostWarnings = messages.HasWarnings;
            if (hostWarnings || messages.HasErrors) hostMessages = SecretMasker.Mask(app.ErrorManager.AllMessages);
            if (messages.HasErrors)
                throw new InvalidOperationException("Inventor reported a host error: " + ErrorSanitizer.Sanitize(app.ErrorManager.LastMessage));
            transaction?.End();
            transaction = null;
            messages.ClearMessages();
            messages = null;

            var ok = new JObject
            {
                ["ok"] = true,
                ["error"] = null,
                ["result"] = null,
                ["warnings"] = hostWarnings ? new JArray(hostMessages) : new JArray(),
                ["transaction_scope"] = "active_document",
                ["mutation_applied"] = JValue.CreateNull()
            };
            ResponseSpillWriter.AttachStdout("send_code", ok, captured.ToString(), ResponseSpillWriterFactory.ForContext(ctx));
            if (SendCodeTask.ResultValue(contract, state.ReturnValue) is { } returnValue)
            {
                var token = ScriptResultToken.ToResultToken(returnValue, out var resultError);
                if (resultError is null)
                {
                    if (!rawNumbers) token = ScriptNumbers.Clean(token);
                    ok["numbers"] = rawNumbers ? "raw" : ScriptNumbers.Mode;
                    ResponseSpillWriter.AttachResult("send_code", ok, token, ResponseSpillWriterFactory.ForContext(ctx));
                }
                else
                {
                    ok["ok"] = false;
                    ok["error"] = resultError;
                }
            }
            SendCodeTask.Apply(ok, contract, completed: true, state.ReturnValue);
            Echo(ok, moduleEcho, silent);
            AttachSkipped(ok, skipped);
            return InventorCommandResult.Success(Guid.Empty, ok, meta);
        }
        catch (CompilationErrorException ex)
        {
            var errors = ex.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            var data = new JObject
            {
                ["ok"] = false,
                ["error"] = ErrorSanitizer.Sanitize(CompileErrorText(errors.Length > 0 ? errors : ex.Diagnostics.ToArray()) + SkippedNote(skipped))
            };
            AttachDiagnostics(data, errors, script?.GetCompilation());
            ResponseSpillWriter.AttachStdout("send_code", data, captured.ToString(), ResponseSpillWriterFactory.ForContext(ctx));
            SendCodeTask.Apply(data, contract, completed: false, null);
            Echo(data, moduleEcho, silent);
            AttachSkipped(data, skipped);
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (Exception ex)
        {
            try { if (messages != null && (messages.HasErrors || messages.HasWarnings)) hostMessages = SecretMasker.Mask(app.ErrorManager.AllMessages); } catch { }
            if (transaction != null)
            {
                try { transaction.Abort(); rolledBack = true; } catch { }
                transaction = null;
            }
            // Once the guard stopped the run, report that stop even when the script swallowed it and failed otherwise.
            var inner = (Exception?)global::Bimwright.Ipt.Scripting.IptGuard.Tripped ?? ex.GetBaseException();
            var text = $"{inner.GetType().Name}: {inner.Message}";
            var data = new JObject
            {
                ["ok"] = false,
                ["error"] = ErrorSanitizer.Sanitize(text),
                ["rolled_back"] = rolledBack,
                ["mutation_applied"] = JValue.CreateNull(),
                ["host_messages"] = hostMessages
            };
            AttachRuntimeLocation(data, inner, parts);
            var rule = SendCodeHints.Match("runtime", text);
            if (rule != null) data["hint"] = rule.Hint;
            ResponseSpillWriter.AttachStdout("send_code", data, captured.ToString(), ResponseSpillWriterFactory.ForContext(ctx));
            SendCodeTask.Apply(data, contract, completed: false, null);
            Echo(data, moduleEcho, silent);
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        finally
        {
            global::Bimwright.Ipt.Scripting.IptGuard.End();
            global::Bimwright.Ipt.Scripting.Ipt.Detach();
            try { transaction?.Abort(); } catch { }
            try { messages?.ClearMessages(); } catch { }
            if (silentApplied)
            {
                try { app.SilentOperation = previousSilent; } catch { /* best effort */ }
            }
            Console.SetOut(originalOut);
        }
    }

    private static void Echo(JObject data, JArray moduleEcho, bool silent)
    {
        if (moduleEcho.Count > 0) data["modules"] = moduleEcho;
        if (silent) data["silent"] = true;
    }

    private static void AttachSkipped(JObject data, SkippedScriptReference[] skipped)
    {
        if (skipped.Length == 0) return;
        data["skipped_references"] = new JArray(skipped.Select(s =>
            new JObject { ["name"] = s.Name, ["reason"] = ErrorSanitizer.Sanitize(s.Reason) }));
    }

    private static string SkippedNote(SkippedScriptReference[] skipped) =>
        skipped.Length == 0 ? string.Empty
            : "\nSkipped references (unreadable metadata): " + string.Join(", ", skipped.Select(s => s.Name));

    private static bool TryReadModules(JToken? token, out List<SendCodeSource.Part> modules, out JArray echo, out string? error)
    {
        modules = new List<SendCodeSource.Part>();
        echo = new JArray();
        error = null;
        if (token is null || token.Type == JTokenType.Null) return true;
        if (token is not JArray arr)
        {
            error = "modules must be an array of {name, hash, code}";
            return false;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in arr)
        {
            var name = (string?)item?["name"] ?? "";
            var mcode = (string?)item?["code"];
            if (!SendCodeSource.ModuleNamePattern.IsMatch(name) || mcode is null)
            {
                error = "each module needs a valid name ([a-z][a-z0-9_]*) and code";
                return false;
            }
            if (!seen.Add(name))
            {
                error = "module listed twice: " + name;
                return false;
            }
            modules.Add(new SendCodeSource.Part(SendCodeSource.ModulePrefix + name, mcode));
            echo.Add(new JObject { ["name"] = name, ["hash"] = SendCodeSource.Hash(mcode) });
        }
        return true;
    }

    private static string CompileErrorText(IEnumerable<Diagnostic> diags)
        => "compile error: " + string.Join("\n", diags.Take(MaxDiagnostics));

    /// <summary>Structured diagnostics with the #line-mapped source + line and a repair hint when one matches.</summary>
    private static void AttachDiagnostics(JObject data, IReadOnlyList<Diagnostic> diags, Compilation? compilation)
    {
        var arr = new JArray();
        string? firstHint = null;
        foreach (var d in diags.Take(MaxDiagnostics))
        {
            var span = d.Location.GetMappedLineSpan();
            var message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            var rule = SendCodeHints.Match(d.Id, message);
            var item = new JObject
            {
                ["source"] = string.IsNullOrEmpty(span.Path) ? SendCodeSource.ScriptSource : span.Path,
                ["line"] = span.StartLinePosition.Line + 1,
                ["column"] = span.StartLinePosition.Character + 1,
                ["code"] = d.Id,
                ["message"] = message,
            };
            var hint = ComputedHint(d, message, rule != null, compilation);
            if (rule != null) hint = hint is null ? rule.Hint : hint + " " + rule.Hint;
            if (hint != null)
            {
                item["hint"] = hint;
                firstHint ??= hint;
            }
            arr.Add(item);
        }
        data["diagnostics"] = arr;
        if (firstHint != null) data["hint"] = firstHint;
    }

    /// <summary>The Inventor interop types and the Ipt helpers, for hints computed from what the add-in actually loads.</summary>
    internal static readonly Lazy<IReadOnlyList<Type>> ApiTypes = new(() =>
        typeof(InvApi.Application).Assembly.GetExportedTypes().Where(t => t.Namespace == "Inventor")
            .Concat(new[] { typeof(global::Bimwright.Ipt.Scripting.Ipt) })
            .Concat(typeof(global::Bimwright.Ipt.Scripting.Ipt).GetNestedTypes().Where(t => t.IsPublic || t.IsNestedPublic))
            .ToArray());

    /// <summary>
    /// A missing member lists the members that exist (and related types that have it); an argument/ref error shows the
    /// called member's real signature. Advisory: a curated rule for a missing member wins, and any failure yields no hint.
    /// </summary>
    private static string? ComputedHint(Diagnostic d, string message, bool hasRule, Compilation? compilation)
    {
        try
        {
            switch (d.Id)
            {
                case "CS1061" or "CS0117":
                    return hasRule ? null : ApiMemberHints.ForMissingMember(d.Id, message, ApiTypes.Value);
                case "CS1620" or "CS1503" or "CS1615" or "CS1501" or "CS7036":
                    return compilation is null ? null : ScriptSignatureHints.ForDiagnostic(d, compilation);
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static void AttachRuntimeLocation(JObject data, Exception ex, IReadOnlyDictionary<string, string> parts)
    {
        string? trace = null;
        try { trace = ex.StackTrace; } catch { }
        if (!SendCodeSource.TryGetFailingLocation(trace, out var src, out var line)) return;
        var loc = new JObject { ["source"] = src, ["line"] = line };
        if (parts.TryGetValue(src, out var partCode) && SendCodeSource.LineText(partCode, line) is { } text)
            loc["text"] = text;
        data["location"] = loc;
    }

    private static CSharpParseOptions ScriptParse => new CSharpParseOptions(kind: SourceCodeKind.Script);

    /// <summary>
    /// Modules are libraries: declarations (methods, types, fields/locals, local functions) only.
    /// A top-level statement would run — or return — in front of every script that loads the module.
    /// </summary>
    private static string? ModuleShapeError(SendCodeSource.Part m)
    {
        var root = CSharpSyntaxTree.ParseText(m.Code, ScriptParse).GetCompilationUnitRoot();
        foreach (var g in root.Members.OfType<GlobalStatementSyntax>())
        {
            if (g.Statement is LocalFunctionStatementSyntax or LocalDeclarationStatementSyntax) continue;
            var line = g.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            return $"{m.Source} line {line}: modules may only declare functions, types and variables (found a top-level {g.Statement.Kind()})";
        }
        return null;
    }

    private static JArray Signatures(IReadOnlyList<SendCodeSource.Part> modules)
    {
        var arr = new JArray();
        foreach (var m in modules)
        {
            var root = CSharpSyntaxTree.ParseText(m.Code, ScriptParse).GetCompilationUnitRoot();
            var sigs = new JArray();
            foreach (var member in root.Members)
            {
                switch (member)
                {
                    case MethodDeclarationSyntax md:
                        sigs.Add(MethodSig(md.ReturnType, md.Identifier.Text, md.TypeParameterList, md.ParameterList));
                        break;
                    case GlobalStatementSyntax { Statement: LocalFunctionStatementSyntax lf }:
                        sigs.Add(MethodSig(lf.ReturnType, lf.Identifier.Text, lf.TypeParameterList, lf.ParameterList));
                        break;
                    case BaseTypeDeclarationSyntax td:
                        sigs.Add(td.Keyword().Text + " " + td.Identifier.Text);
                        if (td is TypeDeclarationSyntax typed)
                            foreach (var tm in typed.Members.OfType<MethodDeclarationSyntax>()
                                         .Where(x => x.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword) || mod.IsKind(SyntaxKind.InternalKeyword))))
                                sigs.Add("  " + td.Identifier.Text + "." + MethodSig(tm.ReturnType, tm.Identifier.Text, tm.TypeParameterList, tm.ParameterList));
                        break;
                }
            }
            arr.Add(new JObject { ["module"] = m.Source.Substring(SendCodeSource.ModulePrefix.Length), ["signatures"] = sigs });
        }
        return arr;
    }

    private static string MethodSig(TypeSyntax ret, string name, TypeParameterListSyntax? tps, ParameterListSyntax ps)
        => $"{ret} {name}{tps}{Collapse(ps.ToString())}";

    private static string Collapse(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
}

internal static class BaseTypeDeclarationExtensions
{
    public static SyntaxToken Keyword(this BaseTypeDeclarationSyntax td) => td switch
    {
        TypeDeclarationSyntax t => t.Keyword,
        EnumDeclarationSyntax e => e.EnumKeyword,
        _ => default,
    };
}
#endif
