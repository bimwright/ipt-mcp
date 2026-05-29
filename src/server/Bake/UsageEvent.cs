namespace Bimwright.Ipt.Server.Bake;

public sealed class UsageEvent
{
    public string Timestamp { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Tool { get; set; } = "";
    public string? NormalizedKey { get; set; }
    public string? ParamsHash { get; set; }
    public bool Success { get; set; }
    public long DurationMs { get; set; }
}
