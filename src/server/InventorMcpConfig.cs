// BOOTSTRAP PLACEHOLDER — replaced in Phase 1 by WS1-A
using System;
using System.IO;
using System.Collections.Generic;

namespace Bimwright.Inventor.Server;

public sealed class InventorMcpConfig
{
    public List<string> Toolsets { get; set; } = new();
    public bool ReadOnly { get; set; }
    public bool EnableSendCode { get; set; }
    public bool EnableToolBaker { get; set; } = true;
    public int TimeoutMs { get; set; } = 30000;
    public string? TargetId { get; set; }

    public string DescriptorDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "inventor-mcp");

    public int MaxResponseBytes { get; set; } = 5_000_000;

    /// <summary>Returns defaults for now; WS1-A will add CLI/env/JSON parsing.</summary>
    public static InventorMcpConfig Load(string[] args) => new();
}
