// BOOTSTRAP PLACEHOLDER — replaced in Phase 1 by WS1-A
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Inventor.Server;

public static class ToolsetFilter
{
    public static readonly string[] KnownToolsets =
    {
        "meta", "query", "document", "parameters", "properties",
        "sketch", "feature", "export", "code", "toolbaker", "toolbaker_write"
    };

    /// <summary>Bootstrap behavior: return all known toolsets except <c>code</c>.</summary>
    public static HashSet<string> Resolve(InventorMcpConfig config)
        => new(KnownToolsets.Where(t => !string.Equals(t, "code", StringComparison.OrdinalIgnoreCase)),
               StringComparer.OrdinalIgnoreCase);
}
