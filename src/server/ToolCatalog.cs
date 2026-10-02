using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;

namespace Bimwright.Ipt.Server;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ToolsetAttribute : Attribute
{
    internal string Name { get; }
    public ToolsetAttribute(string name) => Name = name;
}

internal static class ToolCatalog
{
    // Drawing and the new design-view tool opt into metadata. Legacy methods keep their
    // current display behavior until their permission hints are explicitly declared.
    private static readonly IReadOnlyDictionary<string, ToolMetadata> Explicit = Build(new[] { typeof(DrawingTools), typeof(DrawingQueryTools), typeof(AssemblyTools) })
        .Where(entry => entry.Value.Toolset.StartsWith("drawing", StringComparison.Ordinal) || entry.Key == "create_design_view")
        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    // Legacy methods do not yet declare permission hints. Use the canonical read-only
    // registration surface for their output policy rather than guessing from missing hints.
    private static readonly IReadOnlyDictionary<string, ToolMetadata> ReadOnly = Build(Program.ResolveToolTypesForRegistration(new InventorMcpConfig { ReadOnly = true }));
    internal static bool IsReadOnly(string command) => ReadOnly.ContainsKey(command) || command == "list_code_modules";
    internal static IReadOnlyDictionary<string, ToolMetadata> Build(IEnumerable<Type> types)
    {
        var catalog = new Dictionary<string, ToolMetadata>(StringComparer.Ordinal);
        foreach (var type in types)
            foreach (var method in type.GetMethods())
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>(); if (attr?.Name == null) continue;
                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var dot = description.IndexOf('.'); if (dot >= 0) description = description.Substring(0, dot + 1);
                var name = attr.Name.StartsWith("inventor_", StringComparison.Ordinal) ? attr.Name.Substring(9) : attr.Name;
                catalog.Add(name, new ToolMetadata { Name = attr.Name, Toolset = type.GetCustomAttribute<ToolsetAttribute>()?.Name ?? "", Description = description, ReadOnly = attr.ReadOnly, Destructive = attr.Destructive, Idempotent = attr.Idempotent, OpenWorld = attr.OpenWorld });
            }
        return catalog;
    }
    internal static ToolMetadata? ForCommand(string command, int timeout)
    {
        if (!Explicit.TryGetValue(command, out var entry)) return null;
        return new ToolMetadata { Name = entry.Name, Toolset = entry.Toolset, Description = entry.Description, TimeoutMs = timeout, ReadOnly = entry.ReadOnly, Destructive = entry.Destructive, Idempotent = entry.Idempotent, OpenWorld = entry.OpenWorld };
    }
}
