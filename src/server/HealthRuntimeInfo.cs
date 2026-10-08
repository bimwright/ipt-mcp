using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

internal static class HealthRuntimeInfo
{
    internal static void Enrich(JObject health, JObject gateway, bool connected)
    {
        var plugin = connected ? health["setup_plugin"] as JObject : null;
        var serverInfo = Text(gateway, "assembly_informational_version");
        var pluginInfo = Text(plugin, "assembly_informational_version");
        var serverVersion = VersionOf(serverInfo) ?? Program.ServerVersion;
        var pluginVersion = VersionOf(pluginInfo);
        var serverRevision = RevisionOf(serverInfo);
        var pluginRevision = RevisionOf(pluginInfo);
        health["host_connected"] = connected;
        health["setup_gateway"] = gateway.DeepClone();
        health["mcp"] = new JObject
        {
            ["name"] = "ipt-mcp",
            ["server_version"] = serverVersion,
            ["plugin_version"] = pluginVersion,
            ["server_build_id"] = BuildId(serverInfo, gateway),
            ["plugin_build_id"] = BuildId(pluginInfo, plugin),
            ["version_alignment"] = pluginVersion is null ? "unknown"
                : serverVersion == pluginVersion ? "match" : "mismatch",
            ["revision_alignment"] = serverRevision is null || pluginRevision is null ? "unknown"
                : string.Equals(serverRevision, pluginRevision, StringComparison.OrdinalIgnoreCase) ? "match" : "mismatch",
        };
        health["update_guidance"] = new JObject
        {
            ["automatic_update_supported"] = false,
            ["documentation_url"] = "https://github.com/bimwright/ipt-mcp/blob/master/AGENTS.md",
            ["steps"] = new JArray(
                "Use the official published Setup ZIP and verify its checksum. Keep server and add-in from the same release; do not automatically replace a development build.",
                "Preview install.ps1 -WhatIf and obtain user authorization. Close every Inventor instance; never save, close documents or kill processes automatically to enable an update.",
                "Run the installer with rollback protection, preserve client configuration and personal data, then restart the MCP client and Inventor and call inventor_health again to verify the loaded versions and build identities.")
        };
    }

    private static string? Text(JObject? identity, string key)
        => identity?[key] is { Type: JTokenType.String } value ? (string?)value : null;

    private static string? VersionOf(string? informational)
        => string.IsNullOrWhiteSpace(informational) ? null : informational.Split('+')[0];

    private static string? BuildId(string? informational, JObject? identity)
        => informational?.Split('+', 2) is { Length: 2 } parts && parts[1].Length > 0 ? parts[1]
            : Text(identity, "module_version_id") is { } mvid ? "mvid:" + mvid : null;

    private static string? RevisionOf(string? informational)
        => informational?.Split('+', 2) is { Length: 2 } parts
            && Regex.IsMatch(parts[1], "\\A[0-9a-fA-F]{40}([0-9a-fA-F]{24})?\\z") ? parts[1] : null;
}
