using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Shared.Contracts;

public sealed class InventorCommandEnvelope
{
    [JsonProperty("id")] public Guid Id { get; set; }
    [JsonProperty("command")] public string Command { get; set; } = "";
    [JsonProperty("params")] public JObject Params { get; set; } = new();
    [JsonProperty("timeout_ms")] public int TimeoutMs { get; set; } = 30000;
    [JsonProperty("auth_token")] public string? AuthToken { get; set; }
}
