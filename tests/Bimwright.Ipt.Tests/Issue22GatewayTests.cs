using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Tests;

[Collection("CallLog")]
public sealed class Issue22GatewayTests
{
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
