using System;
using System.IO;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// <see cref="TargetDescriptor"/> round-trips through JSON with the snake_case wire field names
/// (<c>transport</c>, <c>pipe_name</c>, <c>inventor_year</c>, <c>host_app</c>), and
/// <see cref="TargetDescriptorWriter"/> writes/refreshes/deletes the descriptor file at the expected
/// <c>inventor-&lt;year&gt;-&lt;pid&gt;.json</c> path. All API-free.
/// </summary>
public sealed class DescriptorSerializationTests
{
    [Fact]
    public void Descriptor_round_trips_with_wire_fields()
    {
        var original = new TargetDescriptor
        {
            TargetId = "inventor-2025-4242",
            InventorYear = 2025,
            ProcessId = 4242,
            HostApp = "Inventor",
            Transport = "pipe",
            Port = 0,
            PipeName = "BimwrightInventor-4242",
            AuthToken = "tok-deadbeef",
            DocumentTitle = "Part1.ipt",
            DocumentPath = @"C:\work\Part1.ipt",
            LastHeartbeatUtc = DateTimeOffset.Parse("2026-05-29T12:00:00Z"),
        };

        var json = JsonConvert.SerializeObject(original);
        var o = JObject.Parse(json);

        // Wire field names are snake_case (server-side TargetRegistry depends on these).
        Assert.True(o.ContainsKey("transport"));
        Assert.True(o.ContainsKey("pipe_name"));
        Assert.True(o.ContainsKey("inventor_year"));
        Assert.True(o.ContainsKey("host_app"));
        Assert.Equal("pipe", (string?)o["transport"]);
        Assert.Equal("BimwrightInventor-4242", (string?)o["pipe_name"]);
        Assert.Equal(2025, (int)o["inventor_year"]!);
        Assert.Equal("Inventor", (string?)o["host_app"]);

        var back = JsonConvert.DeserializeObject<TargetDescriptor>(json)!;
        Assert.Equal(original.TargetId, back.TargetId);
        Assert.Equal(original.InventorYear, back.InventorYear);
        Assert.Equal(original.ProcessId, back.ProcessId);
        Assert.Equal(original.HostApp, back.HostApp);
        Assert.Equal(original.Transport, back.Transport);
        Assert.Equal(original.PipeName, back.PipeName);
        Assert.Equal(original.AuthToken, back.AuthToken);
        Assert.Equal(original.DocumentTitle, back.DocumentTitle);
        Assert.Equal(original.DocumentPath, back.DocumentPath);
        Assert.Equal(original.LastHeartbeatUtc, back.LastHeartbeatUtc);
    }

    [Fact]
    public void Tcp_descriptor_round_trips_with_port()
    {
        var original = new TargetDescriptor
        {
            TargetId = "inventor-2024-77",
            InventorYear = 2024,
            ProcessId = 77,
            HostApp = "Inventor",
            Transport = "tcp",
            Port = 49321,
            PipeName = null,
            AuthToken = "tok-tcp",
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };

        var back = JsonConvert.DeserializeObject<TargetDescriptor>(JsonConvert.SerializeObject(original))!;
        Assert.Equal("tcp", back.Transport);
        Assert.Equal(49321, back.Port);
        Assert.Null(back.PipeName);
    }

    [Fact]
    public void Writer_writes_and_deletes_descriptor_at_expected_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "inv-dw-" + Guid.NewGuid().ToString("N"));
        var descriptor = new TargetDescriptor
        {
            TargetId = "inventor-2026-555",
            InventorYear = 2026,
            ProcessId = 555,
            HostApp = "Inventor",
            Transport = "pipe",
            PipeName = "BimwrightInventor-555",
            AuthToken = "tok",
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };

        var expectedPath = TargetDescriptorWriter.GetPath(dir, 2026, 555);
        Assert.EndsWith("inventor-2026-555.json", expectedPath);

        var writer = new TargetDescriptorWriter(dir, descriptor);
        try
        {
            // Write once with a document, then verify the file contains the doc fields.
            writer.Start("Bracket.ipt", @"C:\proj\Bracket.ipt", heartbeatMs: System.Threading.Timeout.Infinite);
            Assert.True(File.Exists(expectedPath));

            var written = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(expectedPath))!;
            Assert.Equal("Bracket.ipt", written.DocumentTitle);
            Assert.Equal(@"C:\proj\Bracket.ipt", written.DocumentPath);
            Assert.Equal("pipe", written.Transport);
        }
        finally
        {
            writer.Dispose();
        }

        // Dispose deletes the descriptor file so the target stops being advertised.
        Assert.False(File.Exists(expectedPath));
        try { Directory.Delete(dir, true); } catch { }
    }
}
