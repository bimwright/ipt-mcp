using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

public sealed class InventorCommandEnvelope
{
    [JsonProperty("id")] public Guid Id { get; set; }
    [JsonProperty("command")] public string Command { get; set; } = "";
    [JsonProperty("params")] public JObject Params { get; set; } = new();
    [JsonProperty("timeout_ms")] public int TimeoutMs { get; set; } = 30000;
    [JsonProperty("auth_token")] public string? AuthToken { get; set; }
    [JsonProperty("read_only")] public bool ReadOnly { get; set; }
    [JsonProperty("record_calls")] public bool RecordCalls { get; set; }
    [JsonProperty("spill_retention_hours")] public int SpillRetentionHours {get;set;}=36;
    [JsonProperty("tool", NullValueHandling = NullValueHandling.Ignore)] public ToolMetadata? Tool { get; set; }
    /// <summary>Present from gateways that confirm documents; an add-in refuses writes without it (PAIRING_REQUIRED).</summary>
    [JsonProperty("document_target", NullValueHandling = NullValueHandling.Ignore)] public DocumentTargetEnvelope? DocumentTarget { get; set; }

    /// <summary>One NDJSON request line → envelope, keeping string parameters exactly as sent (<see cref="WireJson"/>).</summary>
    public static InventorCommandEnvelope Parse(string line) => JsonConvert.DeserializeObject<InventorCommandEnvelope>(line, WireJson.Settings)!;
}
