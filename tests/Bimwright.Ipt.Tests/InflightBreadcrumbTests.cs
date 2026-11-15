using Bimwright.Ipt.Shared.Logging;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

[Collection("Process-wide privacy state")]
public sealed class InflightBreadcrumbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-inflight-" + Guid.NewGuid().ToString("N"));

    public InflightBreadcrumbTests() => McpLogger.LocalAppDataOverride = _dir;

    public void Dispose()
    {
        McpLogger.LocalAppDataOverride = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void BeginWritesALogSafeMarkerAndEndRemovesIt()
    {
        var crumb = new InflightBreadcrumb(_dir, 4242, new DateTime(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc));

        crumb.Begin("req-1", "send_code", """{"code":"var secretDesign = 1; return secretDesign;"}""");
        var path = Path.Combine(_dir, "inflight-4242.json");
        var marker = JObject.Parse(File.ReadAllText(path));

        Assert.Equal("send_code", (string?)marker["tool"]);
        Assert.Equal("req-1", (string?)marker["request_id"]);
        Assert.Equal(4242, (int)marker["pid"]!);
        Assert.False(string.IsNullOrEmpty((string?)marker["session_id"]));
        Assert.NotNull(marker["params"]!["code_hash"]);
        Assert.DoesNotContain("secretDesign", File.ReadAllText(path));

        crumb.End();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void OrphansOfDeadProcessesAreTakenAndLiveOnesKept()
    {
        Directory.CreateDirectory(_dir);
        Write(111, "export_drawing");   // dead
        Write(222, "send_code");        // still running
        Write(333, "health");           // current process
        File.WriteAllText(Path.Combine(_dir, "inflight-444.json"), "{not json");

        var orphans = InflightBreadcrumb.TakeOrphans(_dir, currentPid: 333, (pid, _) => pid == 222);

        var orphan = Assert.Single(orphans);
        Assert.Equal("export_drawing", (string?)orphan["tool"]);
        Assert.Equal("2026-11-15T00:00:00Z", (string?)orphan["started_utc"]); // ISO text, not a culture-formatted date
        Assert.False(File.Exists(Path.Combine(_dir, "inflight-111.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "inflight-222.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "inflight-333.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "inflight-444.json")));
    }

    [Fact]
    public void CrashSuspectedEntryKeepsTheOriginalSession()
    {
        var marker = new JObject
        {
            ["request_id"] = "r", ["tool"] = "export_drawing", ["started_utc"] = "2026-11-14T13:18:01Z",
            ["session_id"] = "20261114-200001-ab12", ["pid"] = 52620, ["params"] = new JObject { ["format"] = "pdf" },
        };

        var entry = McpLogger.BuildCrashSuspectedEntry(marker);

        Assert.Equal("20261114-200001-ab12", (string?)entry["session_id"]);
        Assert.Equal("crash_suspected", (string?)entry["outcome"]);
        Assert.False((bool)entry["success"]!);
        Assert.Equal("export_drawing", (string?)entry["tool"]);
        Assert.Contains("52620", (string?)entry["error"]);
        Assert.Equal("pdf", (string?)entry["params"]!["format"]);
    }

    private void Write(int pid, string tool) => File.WriteAllText(Path.Combine(_dir, $"inflight-{pid}.json"),
        new JObject { ["pid"] = pid, ["tool"] = tool, ["session_id"] = "s", ["started_utc"] = "2026-11-15T00:00:00Z" }.ToString());
}
