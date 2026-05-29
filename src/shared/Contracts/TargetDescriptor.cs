using System;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Shared.Contracts;

public sealed class TargetDescriptor
{
    [JsonProperty("target_id")] public string TargetId { get; set; } = "";
    [JsonProperty("inventor_year")] public int InventorYear { get; set; }
    [JsonProperty("process_id")] public int ProcessId { get; set; }
    [JsonProperty("host_app")] public string HostApp { get; set; } = "";
    [JsonProperty("transport")] public string Transport { get; set; } = "";
    [JsonProperty("port")] public int Port { get; set; }
    [JsonProperty("pipe_name")] public string? PipeName { get; set; }
    [JsonProperty("auth_token")] public string AuthToken { get; set; } = "";
    [JsonProperty("document_title")] public string? DocumentTitle { get; set; }
    [JsonProperty("document_path")] public string? DocumentPath { get; set; }
    [JsonProperty("last_heartbeat_utc")] public DateTimeOffset LastHeartbeatUtc { get; set; }
}
