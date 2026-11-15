using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

public sealed class InventorCommandResult
{
    [JsonProperty("id")] public Guid Id { get; set; }
    [JsonProperty("ok")] public bool Ok { get; set; }
    [JsonProperty("data")] public JToken? Data { get; set; }
    [JsonProperty("error")] public InventorError? Error { get; set; }
    [JsonProperty("meta")] public InventorResponseMeta Meta { get; set; } = new();

    public static InventorCommandResult Success(Guid id, JToken? data, InventorResponseMeta meta)
        => new() { Id = id, Ok = true, Data = data, Meta = meta };

    public static InventorCommandResult Fail(Guid id, string code, string message, InventorResponseMeta meta)
        => new() { Id = id, Ok = false, Error = new InventorError { Code = code, Message = message }, Meta = meta };
}

public sealed class InventorError
{
    [JsonProperty("code")] public string Code { get; set; } = "";
    [JsonProperty("message")] public string Message { get; set; } = "";
}

public sealed class InventorResponseMeta
{
    [JsonProperty("target_id")] public string? TargetId { get; set; }
    [JsonProperty("inventor_year")] public int? InventorYear { get; set; }
    [JsonProperty("duration_ms")] public long DurationMs { get; set; }
    [JsonProperty("read_only_enforced")] public bool? ReadOnlyEnforced { get; set; }
    /// <summary>The active document after the command (document_target_v1), or <see cref="DocumentTargetPolicy.NoDocument"/>.</summary>
    [JsonProperty("document_id", NullValueHandling = NullValueHandling.Ignore)] public string? DocumentId { get; set; }
    [JsonProperty("document_title", NullValueHandling = NullValueHandling.Ignore)] public string? DocumentTitle { get; set; }
    /// <summary>Set when an open/create command moved the confirmed document to the one it activated (owner decision Q-B).</summary>
    [JsonProperty("repin_document_id", NullValueHandling = NullValueHandling.Ignore)] public string? RepinDocumentId { get; set; }
}
