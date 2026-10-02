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
/// returns the modules' declared signatures. <c>silent</c> wraps the run in
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
    }

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
        var code = (string?)p["code"] ?? "";
        if (string.IsNullOrWhiteSpace(code) && !compileOnly)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "code parameter is required", meta);

        if (!TryReadModules(p["modules"], out var modules, out var moduleEcho, out var moduleError))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, moduleError!, meta);

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

        var parts = new Dictionary<string, string>(StringComparer.Ordinal) { [SendCodeSource.ScriptSource] = code };
        foreach (var m in modules) parts[m.Source] = m.Code;
        var source = SendCodeSource.Build(modules, code);

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

        try
        {
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .ToArray();

            // The add-in loads into a private AssemblyLoadContext (EnableDynamicLoading), so the
            // Globals type and the Inventor interop live in that context. Roslyn's default loader
            // would reload those assemblies from disk into its own context, binding the compiled
            // script to a *different* Globals type than the instance we pass in -> InvalidCastException.
            // Register the already-loaded assemblies so the script binds to the very same types.
            var loader = new InteractiveAssemblyLoader();
            foreach (var asm in refs)
            {
                try { loader.RegisterDependency(asm); } catch { /* skip identity collisions */ }
            }

            // Debug info makes runtime stack frames carry the #line source + line (S1.4).
            var options = ScriptOptions.Default
                .WithReferences(refs)
                .WithImports(
                    "System",
                    "System.Collections.Generic",
                    "System.Linq",
                    "Inventor")
                .WithEmitDebugInformation(true)
                .WithFileEncoding(Encoding.UTF8);

            var script = CSharpScript.Create(source, options, typeof(Globals), loader);

            if (compileOnly)
            {
                var diags = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                var shapeError = modules.Select(m => ModuleShapeError(m)).FirstOrDefault(e => e != null);
                var data = new JObject
                {
                    ["ok"] = diags.Length == 0 && shapeError == null,
                    ["error"] = diags.Length > 0 ? CompileErrorText(diags) : shapeError,
                    ["compile_only"] = true,
                    ["modules"] = moduleEcho,
                };
                if (diags.Length > 0) AttachDiagnostics(data, diags);
                else data["signatures"] = Signatures(modules);
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
            var state = script.RunAsync(globals: new Globals { app = app, doc = document }).GetAwaiter().GetResult();
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
            if (state.ReturnValue is { } returnValue)
            {
                var token = ScriptResultToken.ToResultToken(returnValue, out var resultError);
                if (resultError is null)
                {
                    ResponseSpillWriter.AttachResult("send_code", ok, token, ResponseSpillWriterFactory.ForContext(ctx));
                }
                else
                {
                    ok["ok"] = false;
                    ok["error"] = resultError;
                }
            }
            Echo(ok, moduleEcho, silent);
            return InventorCommandResult.Success(Guid.Empty, ok, meta);
        }
        catch (CompilationErrorException ex)
        {
            var errors = ex.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            var data = new JObject
            {
                ["ok"] = false,
                ["error"] = ErrorSanitizer.Sanitize(CompileErrorText(errors.Length > 0 ? errors : ex.Diagnostics.ToArray()))
            };
            AttachDiagnostics(data, errors);
            ResponseSpillWriter.AttachStdout("send_code", data, captured.ToString(), ResponseSpillWriterFactory.ForContext(ctx));
            Echo(data, moduleEcho, silent);
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
            var inner = ex.GetBaseException();
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
            Echo(data, moduleEcho, silent);
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        finally
        {
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
    private static void AttachDiagnostics(JObject data, IReadOnlyList<Diagnostic> diags)
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
            if (rule != null)
            {
                item["hint"] = rule.Hint;
                firstHint ??= rule.Hint;
            }
            arr.Add(item);
        }
        data["diagnostics"] = arr;
        if (firstHint != null) data["hint"] = firstHint;
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
