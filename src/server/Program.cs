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

// Hold the setup barrier before resolving paths or starting any data writer.
Bimwright.Setup.RuntimeLayout.StartForCurrentUser("ipt-mcp");
var cfg = InventorMcpConfig.Load(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton<ServerState>();
builder.Services.AddSingleton<PluginClient>();
builder.Services.AddSingleton(_ => new CodeModuleStore());

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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ModelContextProtocol.Protocol.CallToolResult? result = null;
        string? thrown = null;
        try
        {
            return result = await next(ctx, ct);
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
        }
    }));
mcp = Program.RegisterToolsets(mcp, Program.ResolveToolTypesForRegistration(cfg));

await builder.Build().RunAsync();

internal static partial class Program
{
    // InformationalVersion carries "+githash"; report clean semver to MCP clients.
    internal static readonly string ServerVersion =
        (Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0").Split('+')[0];

    internal static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, IEnumerable<Type> toolTypes)
    {
        foreach (var toolType in toolTypes)
        {
            mcp = RegisterToolType(mcp, toolType);
        }

        return mcp;
    }

    private static IMcpServerBuilder RegisterToolType(IMcpServerBuilder mcp, Type toolType)
    {
        if (toolType == typeof(MetaTools)) return mcp.WithTools<MetaTools>();
        if (toolType == typeof(QueryTools)) return mcp.WithTools<QueryTools>();
        if (toolType == typeof(DocumentTools)) return mcp.WithTools<DocumentTools>();
        if (toolType == typeof(ParameterTools)) return mcp.WithTools<ParameterTools>();
        if (toolType == typeof(PropertyTools)) return mcp.WithTools<PropertyTools>();
        if (toolType == typeof(SketchTools)) return mcp.WithTools<SketchTools>();
        if (toolType == typeof(FeatureTools)) return mcp.WithTools<FeatureTools>();
        if (toolType == typeof(ExportTools)) return mcp.WithTools<ExportTools>();
        if (toolType == typeof(CodeTools)) return mcp.WithTools<CodeTools>();
        if (toolType == typeof(ToolBakerTools)) return mcp.WithTools<ToolBakerTools>();
        if (toolType == typeof(ToolBakerWriteTools)) return mcp.WithTools<ToolBakerWriteTools>();
        if (toolType == typeof(AssemblyTools)) return mcp.WithTools<AssemblyTools>();
        if (toolType == typeof(AssemblyQueryTools)) return mcp.WithTools<AssemblyQueryTools>();

        throw new InvalidOperationException("Unsupported MCP tool type: " + toolType.FullName);
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
            "  --read-only             Strip every write-capable toolset.",
            "",
            "ToolBaker:",
            "  --disable-toolbaker     Disable ToolBaker tools (default ON).",
            "  --enable-adaptive-bake  Enable adaptive ToolBaker suggestions (default OFF).",
            "",
            "Safety:",
            "  --enable-send-code      Expose inventor_send_code (also requires the add-in opt-in",
            "                          BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1).",
            "",
            "Tuning:",
            "  --timeout-ms <ms>       Per-command timeout (default 30000).",
            "  --max-response-bytes <n>  Response size cap (default 5000000).",
            "",
            "Env vars (override JSON, overridden by CLI):",
            "  BIMWRIGHT_INVENTOR_TARGET, BIMWRIGHT_INVENTOR_TOOLSETS,",
            "  BIMWRIGHT_INVENTOR_READ_ONLY, BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE,",
            "  BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER, BIMWRIGHT_INVENTOR_ENABLE_ADAPTIVE_BAKE,",
            "  BIMWRIGHT_INVENTOR_TIMEOUT_MS, BIMWRIGHT_INVENTOR_MAX_RESPONSE_BYTES",
            "",
            "Config file (lowest precedence, via --config <path>): JSON with readOnly,",
            "  enableSendCode, enableToolBaker, enableAdaptiveBake, timeoutMs,",
            "  maxResponseBytes, target, toolsets.",
            "",
            "Other:",
            "  --config <path>         Load a JSON config file (lowest precedence).",
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
        return types;
    }
}
