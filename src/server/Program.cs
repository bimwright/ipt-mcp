using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

if (args.Any(a => a == "--help" || a == "-h"))
{
    Program.PrintHelp();
    return;
}

// Hold the setup barrier before binding runtime paths or starting any data writer.
var cfg = InventorMcpConfig.Load(args);
Bimwright.Setup.RuntimeLayout layout;
if (cfg.LocalAppDataRoot is { } isolatedRoot)
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Inventor requires Windows.");
    layout = Bimwright.Setup.RuntimeLayout.Start(isolatedRoot, System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, "ipt-mcp");
}
else
    layout = Bimwright.Setup.RuntimeLayout.StartForCurrentUser("ipt-mcp");
cfg.BindRuntimeLayout(layout);
ServerLogger.Configure(cfg.EnableCallLog, cfg.LocalAppDataRoot == null ? null : layout.GatewayLogPath);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton<ServerState>();
builder.Services.AddSingleton<PluginClient>();
builder.Services.AddSingleton(_ => new CodeModuleStore(System.IO.Path.Combine(cfg.DataDirectory, "modules")));

var mcp = builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInstructions = ServerInstructions.Text;
        o.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name = "ipt-mcp",
            Title = "Inventor MCP",
            Version = Program.ServerVersion,
            Description = "Model Context Protocol gateway for Autodesk Inventor 2022-2027",
            WebsiteUrl = "https://github.com/bimwright/ipt-mcp"
        };
    })
    .WithStdioServerTransport()
    .WithRequestFilters(f => f.AddCallToolFilter(next => async (ctx, ct) =>
    {
        // Journal v3: stamp the calling client once it is known (initialize has completed by now).
        if (ServerLogger.ClientName is null && ctx.Server.ClientInfo is { } ci)
            ServerLogger.SetClient(ci.Name, ci.Version);
        var journaled = ServerLogger.BeginCall();
        var forwarded = PluginClient.BeginActivity(ctx.Params?.Name);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ModelContextProtocol.Protocol.CallToolResult? result = null;
        string? thrown = null;
        try
        {
            result = await next(ctx, ct);
            return result = AgentOutputGuard.Apply(ctx.Params?.Name ?? "?", result, cfg);
        }
        catch (Exception ex)
        {
            thrown = ex.Message;
            throw;
        }
        finally
        {
            // Tools that never reach the add-in (code modules, ToolBaker DB, …) are journaled here.
            if (!journaled.Value)
                ServerLogger.LogServerOnlyCall(ctx.Params?.Name ?? "?", ctx.Params?.Arguments, result, thrown, sw.ElapsedMilliseconds);
            if (!forwarded.Value)
                await ctx.Services!.GetRequiredService<PluginClient>().ReportServerActivityAsync(
                    ctx.Params?.Name ?? "?", ctx.Params?.Arguments, result, thrown, sw.ElapsedMilliseconds);
        }
    }));
mcp = Program.RegisterToolsets(mcp, Program.ResolveToolTypesForRegistration(cfg), cfg.ReadOnly);

await builder.Build().RunAsync();

internal static partial class Program
{
    // InformationalVersion carries "+githash"; report clean semver to MCP clients.
    internal static readonly string ServerVersion =
        (Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0").Split('+')[0];

    internal static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, IEnumerable<Type> toolTypes, bool readOnly = false)
    {
        foreach (var type in toolTypes)
            foreach (var method in type.GetMethods())
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute == null || (readOnly && !attribute.ReadOnly)) continue;
                mcp.WithTools(new[] { McpServerTool.Create(method,
                    context => ActivatorUtilities.CreateInstance(context.Services!, type)) });
            }
        return mcp;
    }

    internal static void PrintHelp()
    {
        var usage = string.Join("\n", new[]
        {
            "ipt-mcp — Inventor MCP server (bimwright)",
            "",
            "Usage: ipt-mcp [options]",
            "",
            "Routing:",
            "  --target <id|year>      Pin to a target descriptor id or 4-digit Inventor year",
            "                          (2022-2027). Default: auto-detect via inventor-*.json",
            "                          descriptors in %LOCALAPPDATA%\\Bimwright\\ipt-mcp\\.",
            "",
            "Tool exposure (progressive disclosure):",
            "  --toolsets <csv>        Comma list of toolsets to enable. 'all' exposes every",
            "                          known toolset.",
            "  --read-only             Register only tools annotated read-only (28 tools).",
            "",
            "ToolBaker:",
            "  --disable-toolbaker     Disable ToolBaker tools (default ON).",
            "  --enable-adaptive-bake  Enable adaptive ToolBaker suggestions (default OFF).",
            "",
            "Safety:",
            "  --enable-send-code      Expose code tools (default ON; read-only still excludes execution).",
            "  --disable-send-code     Hide all four code tools.",
            "  --enable-call-log       Record redacted tool calls (default OFF).",
            "  --disable-call-log      Disable server and plug-in call logging.",
            "",
            "Tuning:",
            "  --timeout-ms <ms>       Per-command timeout (default 30000).",
            "  --max-response-bytes <n>  Response size cap (default 5000000).",
            "  --spill-retention-hours <n>  Spill lifetime (default 36 hours).",
            "  --disable-output-guard  Disable the agent budget; transport cap still applies.",
            "  --output-warning-bytes <n>  Warning threshold (default 65536).",
            "  --output-strong-warning-bytes <n>  Strong warning threshold (default 262144).",
            "  --output-budget-bytes <n>  Final MCP result budget (default 1048576).",
            "",
            "Env vars (override JSON, overridden by CLI):",
            "  BIMWRIGHT_INVENTOR_TARGET, BIMWRIGHT_INVENTOR_TOOLSETS,",
            "  BIMWRIGHT_INVENTOR_READ_ONLY, BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE,",
            "  BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER, BIMWRIGHT_INVENTOR_ENABLE_ADAPTIVE_BAKE,",
            "  BIMWRIGHT_INVENTOR_TIMEOUT_MS, BIMWRIGHT_INVENTOR_MAX_RESPONSE_BYTES,",
            "  BIMWRIGHT_INVENTOR_SPILL_RETENTION_HOURS, BIMWRIGHT_INVENTOR_OUTPUT_GUARD,",
            "  BIMWRIGHT_INVENTOR_OUTPUT_WARNING_BYTES, BIMWRIGHT_INVENTOR_OUTPUT_STRONG_WARNING_BYTES,",
            "  BIMWRIGHT_INVENTOR_OUTPUT_BUDGET_BYTES",
            "",
            "Config file (lowest precedence, via --config <path>): JSON with readOnly,",
            "  enableSendCode, enableCallLog, enableToolBaker, enableAdaptiveBake, timeoutMs,",
            "  maxResponseBytes, spillRetentionHours, enableOutputGuard, outputWarningBytes,",
            "  outputStrongWarningBytes, outputBudgetBytes, target, toolsets.",
            "",
            "Other:",
            "  --config <path>         Load a JSON config file (lowest precedence).",
            "  --local-app-data <path> Isolate descriptors, logs, modules and bake data for a probe.",
            "  -h, --help              Show this help and exit.",
        });
        Console.WriteLine(usage);
    }

    internal static IReadOnlyList<Type> ResolveToolTypesForRegistration(InventorMcpConfig cfg)
    {
        var ts = ToolsetFilter.Resolve(cfg);
        var types = new List<Type>();
        void Add(string toolset, Type t)
        {
            if (ts.Contains(toolset) && !types.Contains(t)) types.Add(t);
        }

        Add("meta",            typeof(MetaTools));
        Add("query",           typeof(QueryTools));
        Add("document",        typeof(DocumentTools));
        Add("parameters",      typeof(ParameterTools));
        Add("properties",      typeof(PropertyTools));
        Add("sketch",          typeof(SketchTools));
        Add("feature",         typeof(FeatureTools));
        Add("export",          typeof(ExportTools));
        Add("code",            typeof(CodeTools));
        Add("toolbaker",       typeof(ToolBakerTools));
        Add("toolbaker_write", typeof(ToolBakerWriteTools));
        Add("assembly",        typeof(AssemblyTools));
        Add("assembly_query",  typeof(AssemblyQueryTools));
        Add("drawing_query", typeof(DrawingQueryTools));
        Add("drawing", typeof(DrawingTools));
        return cfg.ReadOnly ? types.Where(t => t.GetMethods().Any(m => m.GetCustomAttribute<McpServerToolAttribute>()?.ReadOnly == true)).ToArray() : types;
    }
}
