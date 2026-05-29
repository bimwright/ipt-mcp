namespace Bimwright.Ipt.Server.Bake;

public sealed class BakeSuggestionRecord
{
    public string Id { get; set; } = "";
    public string ClusterKey { get; set; } = "";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string State { get; set; } = "";
    public double Score { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? SnoozeUntil { get; set; }
    public string? NeverReason { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string VersionHistoryBlob { get; set; } = "[]";
}
