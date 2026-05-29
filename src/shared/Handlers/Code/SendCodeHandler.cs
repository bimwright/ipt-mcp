#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;
using Bimwright.Inventor.Shared.ToolBaker;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json.Linq;
using InvApi = global::Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Code;

/// <summary>
/// <c>send_code</c> — the opt-in C# scripting escape hatch. Runs a snippet in-process against
/// <c>Inventor.Application</c> (exposed as the <c>app</c> global). Gated two ways: the dispatcher returns
/// <c>SEND_CODE_DISABLED</c> unless the add-in opted in (so this Execute only runs when enabled), and the
/// source must pass <see cref="BakeCompilerPolicy"/> (no file/process/network/environment/reflection
/// APIs). Mirrors nwd-mcp's SendCodeHandler.
/// </summary>
public sealed class SendCodeHandler : IInventorCommand
{
    private const int ExecutionTimeoutMilliseconds = 30000;
    private const int AbortGraceMilliseconds = 5000;

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

        // Banned-API source policy (shared with ToolBaker).
        var policy = BakeCompilerPolicy.ValidateSource(code);
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
            var options = ScriptOptions.Default
                .WithReferences(refs)
                .WithImports(
                    "System",
                    "System.Collections.Generic",
                    "System.Linq",
                    "Inventor");

            var globals = new Globals { app = app };

            Exception? executionError = null;
            using (var cts = new CancellationTokenSource())
            using (var completed = new ManualResetEventSlim(false))
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        CSharpScript.EvaluateAsync(code, options, globals, cancellationToken: cts.Token)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (Exception ex)
                    {
                        executionError = ex;
                    }
                    finally
                    {
                        completed.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "Bimwright.Inventor.SendCode"
                };

                worker.Start();

                if (!completed.Wait(ExecutionTimeoutMilliseconds))
                {
                    cts.Cancel();
                    try
                    {
#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024
                        worker.Abort();
#endif
                    }
                    catch (ThreadStateException) { }
                    catch (PlatformNotSupportedException) { }

                    if (!completed.Wait(AbortGraceMilliseconds))
                        return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.TIMEOUT, "execution timeout after 30s; script did not stop", meta);

                    return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.TIMEOUT, "execution cancelled after 30s", meta);
                }

                if (executionError != null)
                    throw executionError;
            }

            var data = new JObject
            {
                ["ok"] = true,
                ["stdout"] = captured.ToString(),
                ["error"] = null
            };
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (CompilationErrorException ex)
        {
            var data = new JObject
            {
                ["ok"] = false,
                ["stdout"] = captured.ToString(),
                ["error"] = "compile error: " + string.Join("\n", ex.Diagnostics)
            };
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (OperationCanceledException)
        {
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.TIMEOUT, "execution cancelled after 30s", meta);
        }
        catch (AggregateException ex) when (ex.InnerException != null)
        {
            var data = new JObject
            {
                ["ok"] = false,
                ["stdout"] = captured.ToString(),
                ["error"] = $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}"
            };
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (Exception ex)
        {
            var data = new JObject
            {
                ["ok"] = false,
                ["stdout"] = captured.ToString(),
                ["error"] = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
            };
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
#endif
