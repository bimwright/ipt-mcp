using System.IO.Pipes;
using System.Net.Sockets;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Transport;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// 13/11 log: a long send_code held the add-in's only pipe connection, so every later call (health
/// included) failed with "connect timed out". The add-in now serves several clients at once, and the
/// gateway reports a refused connection as TARGET_BUSY with "nothing was sent".
/// </summary>
public sealed class TransportConcurrencyTests
{
    private static string PipeName() => "ipt-concurrency-" + Guid.NewGuid().ToString("N");

    private static Stream Connect(ITransportServer server, out IDisposable owner)
    {
        if (server is PipeTransportServer pipe)
        {
            var named = new NamedPipeClientStream(".", pipe.PipeName, PipeDirection.InOut);
            named.Connect(2000);
            owner = named;
            return named;
        }
        var tcp = new TcpClient();
        tcp.Connect("127.0.0.1", ((TcpTransportServer)server).Port);
        owner = tcp;
        return tcp.GetStream();
    }

    private static string? Ask(Stream stream, string line)
    {
        var writer = new StreamWriter(stream) { AutoFlush = true };
        writer.WriteLine(line);
        return new StreamReader(stream).ReadLine();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_long_call_does_not_block_the_next_connection(bool pipe)
    {
        using ITransportServer server = pipe ? new PipeTransportServer(PipeName(), "token") : new TcpTransportServer("token");
        using var release = new ManualResetEventSlim();
        using var longEntered = new ManualResetEventSlim();
        server.Start((line, tcs) =>
        {
            if (line.Contains("\"long\""))
            {
                longEntered.Set();
                release.Wait(10000);   // the production callback blocks this connection's thread for the whole command
            }
            tcs.TrySetResult(line.Contains("\"long\"") ? "{\"long\":true}" : "{\"health\":true}");
        });

        var first = Connect(server, out var firstOwner);
        try
        {
            var longCall = Task.Run(() => Ask(first, "{\"auth_token\":\"token\",\"long\":1}"));
            Assert.True(longEntered.Wait(2000));

            var second = Connect(server, out var secondOwner);   // was "connect timed out" for the pipe
            using (secondOwner)
                Assert.Equal("{\"health\":true}", Ask(second, "{\"auth_token\":\"token\"}"));
            Assert.True(server.IsClientConnected);   // the long call's connection is still attached

            release.Set();
            Assert.True(longCall.Wait(2000));
            Assert.Equal("{\"long\":true}", longCall.Result);
        }
        finally
        {
            release.Set();
            firstOwner.Dispose();
            server.Stop();
        }
    }

    [Fact]
    public void Pipe_serves_at_most_the_declared_number_of_clients()
    {
        using var server = new PipeTransportServer(PipeName(), "token");
        using var release = new ManualResetEventSlim();
        var entered = 0;
        server.Start((_, tcs) => { Interlocked.Increment(ref entered); release.Wait(10000); tcs.TrySetResult("{}"); });
        var owners = new List<IDisposable>();
        try
        {
            for (var i = 0; i < PipeTransportServer.MaxConnections; i++)
            {
                var stream = Connect(server, out var owner);
                owners.Add(owner);
                new StreamWriter(stream) { AutoFlush = true }.WriteLine("{\"auth_token\":\"token\"}");
            }
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref entered) == PipeTransportServer.MaxConnections, 2000));
            using var extra = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut);
            Assert.Throws<TimeoutException>(() => extra.Connect(300));
        }
        finally
        {
            release.Set();
            foreach (var owner in owners) owner.Dispose();
            server.Stop();
        }
    }

    [Fact]
    public async Task Gateway_reports_a_refused_connection_as_TARGET_BUSY_with_nothing_sent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-concurrency-gateway-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var name = PipeName();
        // An old add-in: one pipe instance, held by another client.
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accepted = server.WaitForConnectionAsync();
        using var holder = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        holder.Connect(2000);
        await accepted;
        File.WriteAllText(Path.Combine(root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = name,
            AuthToken = "test-token", Capabilities = DocumentTargetFixture.Capabilities, LastHeartbeatUtc = DateTimeOffset.UtcNow }));
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = root });
        try
        {
            var error = await Assert.ThrowsAsync<InventorGatewayException>(async () =>
                await client.SendAsync("health", new { }, CancellationToken.None, timeoutMs: 1).WaitAsync(TimeSpan.FromSeconds(9)));
            Assert.Equal(InventorErrorCodes.TARGET_BUSY, error.Code);
            Assert.Contains("Nothing was sent", error.Message);
            Assert.DoesNotContain("may still complete", error.Message);
        }
        finally { Directory.Delete(root, true); }
    }
}
