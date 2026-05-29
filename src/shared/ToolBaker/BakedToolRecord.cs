using System;

namespace Bimwright.Ipt.Shared.ToolBaker;

/// <summary>
/// Persistable definition of a baked tool (preset = fixed args over one read-only handler; macro =
/// a sequence of read-only handler calls). API-agnostic; ported from nwd-mcp.
/// </summary>
public sealed class BakedToolRecord
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public string ParamsSchema { get; set; } = "{}";
    public string CompatMap { get; set; } = "{}";
    public byte[]? DllBytes { get; set; }
    public string SourceCode { get; set; } = "";
    public string HandlerTool { get; set; } = "";
    public string FixedArgs { get; set; } = "{}";
    public string Sequence { get; set; } = "[]";
    public string? CreatedFromSuggestionId { get; set; }
    public bool ReviewedByUser { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");
    public string? LastUsedAt { get; set; }
    public int UsageCount { get; set; }
    public double FailureRate { get; set; }
    public string LifecycleState { get; set; } = "accepted";
    public string VersionHistoryBlob { get; set; } = "[]";
}
