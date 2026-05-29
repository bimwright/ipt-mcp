using System.IO;

namespace Bimwright.Ipt.Server.Bake;

/// <summary>
/// Resolves on-disk ToolBaker paths under <c>%LOCALAPPDATA%\Bimwright\ipt-mcp\baked</c>
/// (from <see cref="InventorMcpConfig.BakeDirectory"/>). Ported from nwd-mcp.
/// </summary>
public static class BakePaths
{
    private static string Root(InventorMcpConfig c) => c.BakeDirectory; // %LOCALAPPDATA%\Bimwright\ipt-mcp\baked
    public static string Db(InventorMcpConfig c) => Path.Combine(Root(c), "bake.db");
    public static string AuditLog(InventorMcpConfig c) => Path.Combine(Root(c), "audit.jsonl");
    public static void EnsureDir(InventorMcpConfig c) => Directory.CreateDirectory(Root(c));
}
