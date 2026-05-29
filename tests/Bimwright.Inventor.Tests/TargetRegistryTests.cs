using System;
using System.IO;
using Bimwright.Inventor.Shared.Contracts;

namespace Bimwright.Inventor.Tests;

/// <summary>
/// <see cref="TargetRegistry.List"/> reads descriptor JSON from a directory and returns
/// only live Inventor targets: it drops descriptors with a dead process id, a stale
/// heartbeat, a non-Inventor host, or an out-of-range year, and keeps live ones.
/// </summary>
public sealed class TargetRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inv-reg-" + Guid.NewGuid().ToString("N"));

    public TargetRegistryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private void WriteDescriptor(string name, int year, int pid, string host, DateTimeOffset heartbeat,
                                 string transport = "tcp")
        => File.WriteAllText(Path.Combine(_dir, name), $$"""
        {
          "target_id": "inventor-{{year}}-{{pid}}",
          "inventor_year": {{year}},
          "process_id": {{pid}},
          "host_app": "{{host}}",
          "transport": "{{transport}}",
          "port": 49500,
          "pipe_name": null,
          "auth_token": "secret-token",
          "document_title": "sample.ipt",
          "last_heartbeat_utc": "{{heartbeat.UtcDateTime:O}}"
        }
        """);

    private static int AliveProcessId() => System.Diagnostics.Process.GetCurrentProcess().Id;

    // A pid that is essentially never a running process.
    private const int DeadProcessId = 0x7FFFFFFE;

    [Fact]
    public void IgnoresDeadProcess()
    {
        WriteDescriptor("dead.json", 2025, DeadProcessId, "Inventor", DateTimeOffset.UtcNow);
        Assert.Empty(new TargetRegistry(_dir).List());
    }

    [Fact]
    public void IgnoresStaleHeartbeat()
    {
        WriteDescriptor("stale.json", 2025, AliveProcessId(), "Inventor", DateTimeOffset.UtcNow.AddSeconds(-300));
        Assert.Empty(new TargetRegistry(_dir).List());
    }

    [Fact]
    public void IgnoresNonInventorHost()
    {
        WriteDescriptor("revit.json", 2025, AliveProcessId(), "Revit", DateTimeOffset.UtcNow);
        Assert.Empty(new TargetRegistry(_dir).List());
    }

    [Fact]
    public void IgnoresOutOfRangeYear()
    {
        WriteDescriptor("old.json", 2019, AliveProcessId(), "Inventor", DateTimeOffset.UtcNow);
        Assert.Empty(new TargetRegistry(_dir).List());
    }

    [Fact]
    public void ReturnsLiveInventorTarget()
    {
        WriteDescriptor("live.json", 2026, AliveProcessId(), "Inventor", DateTimeOffset.UtcNow);
        var list = new TargetRegistry(_dir).List();
        Assert.Single(list);
        Assert.Equal(2026, list[0].InventorYear);
        Assert.Equal("Inventor", list[0].HostApp);
    }

    [Fact]
    public void DropsDeadAndStaleButKeepsLive_inOneDirectory()
    {
        WriteDescriptor("dead.json", 2025, DeadProcessId, "Inventor", DateTimeOffset.UtcNow);
        WriteDescriptor("stale.json", 2024, AliveProcessId(), "Inventor", DateTimeOffset.UtcNow.AddSeconds(-300));
        WriteDescriptor("live.json", 2027, AliveProcessId(), "Inventor", DateTimeOffset.UtcNow, transport: "pipe");

        var list = new TargetRegistry(_dir).List();

        Assert.Single(list);
        Assert.Equal(2027, list[0].InventorYear);
        Assert.Equal("pipe", list[0].Transport);
    }
}
