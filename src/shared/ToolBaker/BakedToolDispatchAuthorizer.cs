using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.ToolBaker;

/// <summary>
/// Governs which wire commands a baked tool may invoke. Baked tools are restricted to the read-only
/// Inventor query commands and are explicitly forbidden from re-entering the platform layer
/// (<c>send_code</c>, <c>batch_execute</c>, <c>run_baked_tool</c>, the bake-suggestion commands, etc.)
/// to prevent privilege escalation / recursion. Ported from nwd-mcp's <c>BakedToolDispatchAuthorizer</c>
/// with the Inventor read-only command set.
/// </summary>
public static class BakedToolDispatchAuthorizer
{
    // The read-only Inventor query commands a baked tool is permitted to invoke.
    private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "health",
        "get_document_info",
        "list_open_documents",
        "list_parameters",
        "get_parameter",
        "get_iproperty",
        "get_mass_properties",
        "list_interfaces",
        "check_interference",
        "measure_min_distance",
        "get_assembly_bom",
        "list_constraints"
    };

    // Platform / mutating commands a baked tool must never reach (recursion + escalation guard).
    private static readonly HashSet<string> Denied = new HashSet<string>(StringComparer.Ordinal)
    {
        "send_code",
        "batch_execute",
        "run_baked_tool",
        "apply_bake",
        "accept_bake_suggestion",
        "dismiss_bake_suggestion",
        "list_baked_tools"
    };

    public static bool IsAllowed(string command)
        => !string.IsNullOrWhiteSpace(command) && !Denied.Contains(command) && Allowed.Contains(command);
}
