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
        "assembly", "assembly_query", "drawing", "drawing_query"
    };

    public static HashSet<string> Resolve(InventorMcpConfig config)
    {
        var requested = config.Toolsets;
        var set = requested.Count == 0
            ? new HashSet<string>(KnownToolsets, StringComparer.OrdinalIgnoreCase)
            : requested.Any(t => string.Equals(t, "all", StringComparison.OrdinalIgnoreCase))
                ? new HashSet<string>(KnownToolsets, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);

        set.IntersectWith(KnownToolsets);          // silently drop unknown names

        if (!config.EnableSendCode) set.Remove("code");
        if (!config.EnableToolBaker) { set.Remove("toolbaker"); set.Remove("toolbaker_write"); }
        // Individual read-only methods are selected during registration; mixed toolsets retain their queries.
        return set;
    }
}
