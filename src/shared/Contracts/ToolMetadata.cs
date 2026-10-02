using Newtonsoft.Json;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Registered MCP identity/display information carried to the add-in; never grants permissions.</summary>
public sealed class ToolMetadata
{
    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("toolset")] public string Toolset { get; set; } = "";
    [JsonProperty("description")] public string Description { get; set; } = "";
    [JsonProperty("timeout_ms")] public int? TimeoutMs { get; set; }
    [JsonProperty("read_only")] public bool ReadOnly { get; set; }
    [JsonProperty("destructive")] public bool Destructive { get; set; }
    [JsonProperty("idempotent")] public bool Idempotent { get; set; }
    [JsonProperty("open_world")] public bool OpenWorld { get; set; }
}
