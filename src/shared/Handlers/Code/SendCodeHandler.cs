#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Security;
using Bimwright.Ipt.Shared.ToolBaker;
using Microsoft.CodeAnalysis.CSharp.Scripting;
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
/// Mirrors nwd-mcp's SendCodeHandler.
/// </summary>
public sealed class SendCodeHandler : IInventorCommand
{
    public string Name => "send_code";
    public bool IsReadOnly => false;

    public class Globals
    {
        public InvApi.Application app = null!;
    }

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };

        // Defense-in-depth: even though the dispatcher gates send_code, refuse to run if not enabled.
        if (!ctx.EnableSendCode)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.SEND_CODE_DISABLED,
                "send_code is disabled; set BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1 on the add-in and pass --enable-send-code to the server", meta);

        var app = ctx.Application as InvApi.Application;
        if (app is null)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.API_ERROR, "Inventor.Application is not available", meta);

        var code = (string?)p["code"];
        if (string.IsNullOrWhiteSpace(code))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "code parameter is required", meta);

        // Banned-API source policy (shared with ToolBaker); the context label keeps the
        // message honest about which surface rejected the source.
        var policy = BakeCompilerPolicy.ValidateSource(code!, "send_code");
        if (!policy.Ok)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, policy.Error ?? "send_code source rejected by policy", meta);

        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);

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

            var options = ScriptOptions.Default
                .WithReferences(refs)
                .WithImports(
                    "System",
                    "System.Collections.Generic",
                    "System.Linq",
                    "Inventor");

            var globals = new Globals { app = app };

            // No handler-side CTS: a CancellationToken cannot interrupt a synchronous script
            // running on the STA thread anyway. The add-in's task.Wait(env.TimeoutMs) is the
            // single owner of the timeout (spec F2-b).
            var script = CSharpScript.Create(code, options, typeof(Globals), loader);
            var state = script.RunAsync(globals).GetAwaiter().GetResult();

            var data = new JObject
            {
                ["ok"] = true,
                ["error"] = null,
                ["result"] = null
            };
            ResponseSpillWriter.AttachStdout("send_code", data, captured.ToString());
            if (state.ReturnValue is { } returnValue)
            {
                var token = ScriptResultToken.ToResultToken(returnValue, out var resultError);
                if (resultError is null)
                {
                    data["result"] = token;
                }
                else
                {
                    data["ok"] = false;
                    data["error"] = resultError;
                }
            }
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (CompilationErrorException ex)
        {
            var data = new JObject
            {
                ["ok"] = false,
                ["error"] = ErrorSanitizer.Sanitize("compile error: " + string.Join("\n", ex.Diagnostics))
            };
            ResponseSpillWriter.AttachStdout("send_code", data, captured.ToString());
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (AggregateException ex) when (ex.InnerException != null)
        {
            var data = new JObject
            {
                ["ok"] = false,
                ["error"] = ErrorSanitizer.Sanitize($"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}")
            };
            ResponseSpillWriter.AttachStdout("send_code", data, captured.ToString());
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (Exception ex)
        {
            var data = new JObject
            {
                ["ok"] = false,
                ["error"] = ErrorSanitizer.Sanitize($"{ex.GetType().Name}: {ex.Message}")
            };
            ResponseSpillWriter.AttachStdout("send_code", data, captured.ToString());
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
#endif
