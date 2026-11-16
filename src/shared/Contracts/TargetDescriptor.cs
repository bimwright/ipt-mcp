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
    [JsonProperty("setup_identity", NullValueHandling = NullValueHandling.Ignore)] public Newtonsoft.Json.Linq.JObject? SetupIdentity { get; set; }
    /// <summary>Wire protocols the add-in supports, e.g. <see cref="DocumentTargetPolicy.Protocol"/>; older add-ins omit it.</summary>
    [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore)] public string[]? Capabilities { get; set; }
    /// <summary>
    /// Start time of the Inventor process (family contract FC-TGT-04): pid plus start time names one instance,
    /// since Windows reuses pids. Older add-ins omit it and the gateway reads the process instead.
    /// </summary>
    [JsonProperty("process_start_utc", NullValueHandling = NullValueHandling.Ignore)] public DateTimeOffset? ProcessStartUtc { get; set; }
}
