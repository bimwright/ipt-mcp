using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Ipt.Server;

public static class ToolsetFilter
{
    public static readonly string[] KnownToolsets =
    {
        "meta", "query", "document", "parameters", "properties",
        "sketch", "feature", "export", "code", "toolbaker", "toolbaker_write",
        "assembly", "assembly_query"
    };

    // everything except "code" (send-code is opt-in)
    public static readonly string[] DefaultOn =
    {
        "meta", "query", "document", "parameters", "properties",
        "sketch", "feature", "export", "toolbaker", "toolbaker_write",
        "assembly", "assembly_query"
    };

    // Removed in read-only mode. `export` is write-capable here because Phase 1
    // has no explicit output-path policy (see spec Read-Only Mode); read-only
    // `meta`, `query`, and inspection-only `toolbaker` survive.
    public static readonly string[] WriteCapable =
    {
        "document", "parameters", "properties", "sketch", "feature",
        "export", "code", "toolbaker_write", "assembly"
    };

    public static HashSet<string> Resolve(InventorMcpConfig config)
    {
        var requested = config.Toolsets;
        var set = requested.Count == 0
            ? new HashSet<string>(DefaultOn, StringComparer.OrdinalIgnoreCase)
            : requested.Any(t => string.Equals(t, "all", StringComparison.OrdinalIgnoreCase))
                ? new HashSet<string>(KnownToolsets, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);

        set.IntersectWith(KnownToolsets);          // silently drop unknown names

        if (!config.EnableSendCode) set.Remove("code");
        if (!config.EnableToolBaker) { set.Remove("toolbaker"); set.Remove("toolbaker_write"); }
        if (config.ReadOnly) foreach (var w in WriteCapable) set.Remove(w);

        return set;
    }
}
