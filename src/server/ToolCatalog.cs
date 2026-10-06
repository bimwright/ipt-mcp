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
    private static readonly IReadOnlyDictionary<string, ToolMetadata> Explicit = Build(
        Program.ResolveToolTypesForRegistration(new InventorMcpConfig { EnableSendCode = true, Toolsets = { "all" } }));
    internal static bool IsReadOnly(string command) => Explicit.TryGetValue(command, out var tool) && tool.ReadOnly;
    internal static IReadOnlyDictionary<string, ToolMetadata> Build(IEnumerable<Type> types)
    {
        var catalog = new Dictionary<string, ToolMetadata>(StringComparer.Ordinal);
        foreach (var type in types)
            foreach (var method in type.GetMethods())
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>(); if (attr?.Name == null) continue;
                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var dot = description.IndexOf(". ", StringComparison.Ordinal);
                if (dot >= 0) description = description.Substring(0, dot + 1);
                if (description.Length > 200) description = description.Substring(0, 197) + "...";
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
