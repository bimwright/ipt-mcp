using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

[Collection("CallLog")]
public sealed class Issue22GatewayTests
{
    [Fact]
    public async Task Bounded_transport_sends_only_to_the_explicitly_selected_target()
    {
        var root = Directory.CreateTempSubdirectory("ipt-merged-binding-").FullName;
        var selectedName = "ipt-selected-" + Guid.NewGuid().ToString("N");
        var otherName = "ipt-other-" + Guid.NewGuid().ToString("N");
        using var selected = new NamedPipeServerStream(selectedName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var other = new NamedPipeServerStream(otherName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        void WriteDescriptor(int year, string name) => File.WriteAllText(Path.Combine(root, year + ".json"),
            JsonConvert.SerializeObject(new TargetDescriptor {
                TargetId = $"inventor-{year}-{Environment.ProcessId}", ProcessId = Environment.ProcessId,
                InventorYear = year, HostApp = "Inventor", Transport = "pipe", PipeName = name,
                AuthToken = "token-" + year, LastHeartbeatUtc = DateTimeOffset.UtcNow }));
        try
        {
            WriteDescriptor(2025, selectedName);
            WriteDescriptor(2027, otherName);
            var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = root });
            var ambiguous = await Assert.ThrowsAsync<InventorGatewayException>(
                () => client.SendAsync("create_part", new { }, timeout.Token));
            Assert.Equal(InventorErrorCodes.AMBIGUOUS_TARGET, ambiguous.Code);
            Assert.False(selected.IsConnected);
            Assert.False(other.IsConnected);

            var target = client.SwitchTarget("id:inventor-2025-" + Environment.ProcessId);
            var connected = selected.WaitForConnectionAsync(timeout.Token);
            var call = client.SendAsync("create_part", new { }, timeout.Token);
            await connected;
            using var reader = new StreamReader(selected, leaveOpen: true);
            using var writer = new StreamWriter(selected, leaveOpen: true) { AutoFlush = true };
            var envelope = JObject.Parse((await reader.ReadLineAsync(timeout.Token))!);
            Assert.Equal("create_part", (string?)envelope["command"]);
            Assert.False(other.IsConnected);
            await writer.WriteLineAsync(new JObject {
                ["id"] = envelope["id"], ["ok"] = true, ["data"] = new JObject { ["accepted"] = true }
            }.ToString(Formatting.None));
            Assert.True((await call).Value<bool>("accepted"));
            Assert.Equal(target.TargetId, client.SelectedTargetId);
        }
        finally { selected.Dispose(); other.Dispose(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Caller_cancellation_during_response_preserves_cancellation_and_never_resends()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-issue22-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var name = "ipt-issue22-cancel-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        File.WriteAllText(Path.Combine(root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = name,
            AuthToken = "test-token", LastHeartbeatUtc = DateTimeOffset.UtcNow }));
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = root });
        using var cancellation = new CancellationTokenSource();
        try
        {
            var connected = server.WaitForConnectionAsync();
            var call = client.SendAsync("create_part", new { }, cancellation.Token);
            await connected.WaitAsync(TimeSpan.FromSeconds(2));
            using var reader = new StreamReader(server);
            Assert.Contains("create_part", (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)))!);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await call.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Null(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { server.Dispose(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Peer_that_does_not_read_request_has_a_bounded_write_deadline()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-issue22-gateway-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var name = "ipt-issue22-gateway-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024, 1024);
        File.WriteAllText(Path.Combine(root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = name,
            AuthToken = "test-token", LastHeartbeatUtc = DateTimeOffset.UtcNow }));
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = root });
        Task? call = null;
        try
        {
            var connected = server.WaitForConnectionAsync();
            call = client.SendAsync("health", new { payload = new string('x', 8 * 1024 * 1024) }, CancellationToken.None, timeoutMs: 1);
            await connected.WaitAsync(TimeSpan.FromSeconds(2));
            var error = await Assert.ThrowsAsync<InventorGatewayException>(async () => await call.WaitAsync(TimeSpan.FromSeconds(7)));
            Assert.Equal(InventorErrorCodes.TIMEOUT, error.Code);
        }
        finally
        {
            server.Dispose();
            if (call != null) { try { await call.WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
            Directory.Delete(root, true);
        }
    }
}
