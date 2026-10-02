using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Views.Toast;

public enum ToolActivityKind { Read, Write }

public static class ToolActivityClassifier
{
    private static readonly HashSet<string> Scripts = new(StringComparer.Ordinal)
    {
        "send_code", "batch_execute", "run_baked_tool", "apply_bake",
    };

    /// <param name="handlerIsReadOnly"><c>IInventorCommand.IsReadOnly</c> of the registered handler;
    /// null when the command is unknown (it changed nothing).</param>
    public static ToolActivityKind Classify(string? command, bool? handlerIsReadOnly)
        => handlerIsReadOnly == false ? ToolActivityKind.Write : ToolActivityKind.Read;

    public static string Category(string? command, ToolActivityKind kind, bool success)
    {
        if (!success) return "MCP · Failed";
        if (command != null && Scripts.Contains(command)) return "MCP · Script";
        if (command == "capture_view" || command == "capture_sheet") return "MCP · Snapshot";
        if (command != null && command.StartsWith("export_", StringComparison.Ordinal)) return "MCP · Export";
        return kind == ToolActivityKind.Write ? "MCP · Modified" : "MCP · Query";
    }
}

public static class ToolNameFormatter
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["step"] = "STEP", ["stl"] = "STL", ["dxf"] = "DXF", ["sat"] = "SAT", ["bom"] = "BOM",
        ["bim"] = "BIM", ["id"] = "ID", ["imate"] = "iMate", ["iproperty"] = "iProperty",
    };

    /// <summary><c>export_step</c> → <c>Export STEP</c>; <c>get_iproperty</c> → <c>Get iProperty</c>.</summary>
    public static string Format(string? command)
    {
        if (command == null || command.Trim().Length == 0) return "Unknown command";
        var parts = command.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return command;
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            parts[i] = Words.TryGetValue(p, out var w)
                ? w
                : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant();
        }
        return string.Join(" ", parts);
    }
}
