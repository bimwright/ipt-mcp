using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var cfg = InventorMcpConfig.Load(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton<ServerState>();
builder.Services.AddSingleton<PluginClient>();

var mcp = builder.Services
    .AddMcpServer(o => o.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport();
mcp = Program.RegisterToolsets(mcp, Program.ResolveToolTypesForRegistration(cfg));

await builder.Build().RunAsync();

internal static partial class Program
{
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

        throw new InvalidOperationException("Unsupported MCP tool type: " + toolType.FullName);
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
        return types;
    }
}
