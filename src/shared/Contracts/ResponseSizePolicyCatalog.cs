using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Agent-facing narrowing hints for wire commands whose inline response can grow large
/// (spec F3-a — small catalog; rvt-mcp carries the full version). Keys are wire command
/// names (snake_case, unprefixed); hints name the real MCP parameters the agent can use.
/// </summary>
public static class ResponseSizePolicyCatalog
{
    private const string FallbackHint =
        "Narrow the query or fetch a subset via send_code, and retry.";

    private static readonly Dictionary<string, string> NarrowingHints =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["get_assembly_bom"] = "Retry with a smaller max_rows.",
            ["list_interfaces"] = "Retry with an exact occurrence=<name>.",
            ["list_constraints"] = "No filter yet — fetch a subset via send_code.",
            ["list_parameters"] = "No filter yet — fetch a subset via send_code.",
            ["send_code"] = "Return less: a smaller result DTO or shorter stdout (stdout over 64 KiB auto-spills to a local file).",
            ["run_baked_tool"] = "Narrow the baked tool's params; oversized results auto-spill to a local file.",
        };

    public static string GetNarrowingHint(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return FallbackHint;
        return NarrowingHints.TryGetValue(commandName!, out var hint) ? hint : FallbackHint;
    }
}
