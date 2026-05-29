using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Inventor.Server;
using Bimwright.Inventor.Server.Tools;
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

builder.Services
    .AddMcpServer(o => o.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .WithTools(Program.ResolveToolTypesForRegistration(cfg).ToArray());

await builder.Build().RunAsync();

internal static partial class Program
{
    internal static IReadOnlyList<Type> ResolveToolTypesForRegistration(InventorMcpConfig cfg)
    {
        var ts = ToolsetFilter.Resolve(cfg);
        var types = new List<Type>();
        void Add(string toolset, Type t)
        {
            if (ts.Contains(toolset) && !types.Contains(t)) types.Add(t);
        }

        Add("meta",            typeof(MetaTools));
        Add("query",           typeof(DocumentTools));   // read-only doc/query methods live here
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
