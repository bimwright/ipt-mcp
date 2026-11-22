using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

[Collection("CallLog")]
public sealed class ClientNameFilterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ipt-clientname-" + Guid.NewGuid().ToString("N"));
    private readonly string? _savedClientName = PluginClient.ClientName;

    public void Dispose()
    {
        PluginClient.ClientName = _savedClientName;
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    // FC-TST-03
    [Fact]
    public async Task CallToolFilter_captures_client_info_per_call_on_forwarded_and_server_only_envelopes()
    {
        Directory.CreateDirectory(_root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        File.WriteAllText(
            Path.Combine(_root, $"inventor-2027-{Environment.ProcessId}.json"),
            JsonConvert.SerializeObject(new TargetDescriptor
            {
                TargetId = "inventor-2027-" + Environment.ProcessId,
                InventorYear = 2027,
                ProcessId = Environment.ProcessId,
                HostApp = "Inventor",
                Transport = "tcp",
                Port = port,
                AuthToken = "test-token",
                Capabilities = DocumentTargetFixture.Capabilities,
                LastHeartbeatUtc = DateTimeOffset.UtcNow,
            }));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var received = new ConcurrentQueue<JObject>();
        var acceptLoop = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(cts.Token); }
                catch { break; }
                _ = Task.Run(async () =>
                {
                    using (client)
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                    {
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) return;
                        var req = JObject.Parse(line);
                        received.Enqueue(req);
                        await writer.WriteLineAsync(new JObject
                        {
                            ["id"] = req["id"],
                            ["ok"] = true,
                            ["data"] = new JObject { ["ok"] = true },
                        }.ToString(Formatting.None));
                    }
                });
            }
        });

        try
        {
            await RunClientSessionAsync("claude-code", calls: async client =>
            {
                await client.CallToolAsync("inventor_health", cancellationToken: cts.Token);
                await client.CallToolAsync("inventor_list_available_targets", cancellationToken: cts.Token);
            }, cts.Token);

            await RunClientSessionAsync("codex-mcp-client", calls: async client =>
            {
                await client.CallToolAsync("inventor_health", cancellationToken: cts.Token);
            }, cts.Token);

            var envelopes = received.ToArray();
            Assert.Contains(envelopes, e =>
                (string?)e["command"] == "health"
                && (string?)e["client_name"] == "claude-code");
            Assert.Contains(envelopes, e =>
                (string?)e["command"] == "report_tool_activity"
                && (string?)e["params"]?["command"] == "list_available_targets"
                && (string?)e["client_name"] == "claude-code");
            Assert.Contains(envelopes, e =>
                (string?)e["command"] == "health"
                && (string?)e["client_name"] == "codex-mcp-client");
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await acceptLoop;
        }
    }

    private async Task RunClientSessionAsync(string clientName, Func<McpClient, Task> calls, CancellationToken ct)
    {
        var cfg = new InventorMcpConfig
        {
            DescriptorDirectory = _root,
            ReadOnly = true,
            TimeoutMs = 5000,
        };
        var pluginClient = new PluginClient(cfg);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(cfg);
        builder.Services.AddSingleton<ServerState>();
        builder.Services.AddSingleton(pluginClient);

        var c2s = new Pipe();
        var s2c = new Pipe();
        var mcp = builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(c2s.Reader.AsStream(), s2c.Writer.AsStream());
        mcp = Program.RegisterCallToolFilter(mcp, cfg);
        Program.RegisterToolsets(mcp, new[] { typeof(QueryTools), typeof(MetaTools) }, readOnly: true, pluginClient.GatewayIdentity);

        using var host = builder.Build();
        await host.StartAsync(ct);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(c2s.Writer.AsStream(), s2c.Reader.AsStream()),
                new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = clientName, Version = "1.0.0" },
                },
                cancellationToken: ct);
            await calls(client);
        }
        finally
        {
            c2s.Writer.Complete();
            s2c.Reader.Complete();
            await host.StopAsync(CancellationToken.None);
        }
    }
}
