using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class McpProtocolSmokeTests
{
    [Fact]
    public async Task Server_exposes_tools_list_over_stdio()
    {
        var responses = await RunProtocolHandshake();

        var initialize = Assert.Single(responses, r => (int?)r["id"] == 1);
        Assert.NotNull(initialize["result"]?["capabilities"]?["tools"]);

        var toolsList = Assert.Single(responses, r => (int?)r["id"] == 2);
        var tools = Assert.IsAssignableFrom<JArray>(toolsList["result"]?["tools"]);
        Assert.NotEmpty(tools);
        Assert.Contains(tools, t => (string?)t["name"] == "inventor_list_available_targets");
        var report = Assert.Single(tools, t => (string?)t["name"] == "inventor_report_task_result");
        Assert.Equal(new[] { "outcome", "summary", "task_id" },
            report["inputSchema"]!["required"]!.Values<string>().OrderBy(x => x).ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Health_schema_and_offline_identity_are_available_over_MCP_including_read_only(
        bool readOnly, bool explicitArgument)
    {
        var responses = await RunProtocolHandshake(checkHealth: true, readOnly: readOnly, explicitHealthArgument: explicitArgument);
        var tools = (JArray)responses[1]["result"]!["tools"]!;
        var health = Assert.Single(tools, tool => (string?)tool["name"] == "inventor_health");
        Assert.Equal("boolean", (string?)health["inputSchema"]?["properties"]?["check_updates"]?["type"]);
        Assert.DoesNotContain("check_updates", health["inputSchema"]?["required"]?.Values<string>() ?? Array.Empty<string>());
        Assert.True(health["annotations"]!.Value<bool>("readOnlyHint"));
        Assert.False(health["annotations"]!.Value<bool>("destructiveHint"));
        Assert.True(health["annotations"]!.Value<bool>("openWorldHint"));
        var result = JObject.Parse((string)responses[2]["result"]!["content"]![0]!["text"]!);
        Assert.False(result.Value<bool>("ok"));
        Assert.False(result.Value<bool>("host_connected"));
        Assert.Equal("NO_TARGET", (string?)result["error"]?["code"]);
        Assert.Equal((string?)responses[0]["result"]?["serverInfo"]?["version"], (string?)result["mcp"]?["server_version"]);
        Assert.NotEmpty(result["mcp"]!["server_build_id"]!.Value<string>()!);
        Assert.Equal("not_checked", (string?)result["update"]?["status"]);
    }

    private sealed class Profile : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ipt-protocol-" + Guid.NewGuid().ToString("N"));
        internal Profile() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static async Task<JObject[]> RunProtocolHandshake(bool checkHealth = false, bool readOnly = false, bool explicitHealthArgument = false)
    {
        using var profile = new Profile();
        var serverAssembly = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(serverAssembly)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.ArgumentList.Add("--local-app-data");
        startInfo.ArgumentList.Add(profile.Root);
        if (readOnly) startInfo.ArgumentList.Add("--read-only");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ipt-mcp server.");
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke-test","version":"0.0"}}}""");
        var initialize = await ReadJsonResponse(process, "initialize");

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
        var toolsList = await ReadJsonResponse(process, "tools/list");
        JObject? ruleCall = null;
        if (checkHealth)
        {
            var arguments = explicitHealthArgument ? new JObject { ["check_updates"] = false } : new JObject();
            await process.StandardInput.WriteLineAsync(new JObject
            {
                ["jsonrpc"] = "2.0", ["id"] = 3, ["method"] = "tools/call",
                ["params"] = new JObject { ["name"] = "inventor_health", ["arguments"] = arguments },
            }.ToString(Newtonsoft.Json.Formatting.None));
            ruleCall = await ReadJsonResponse(process, "health");
        }
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("ipt-mcp server did not exit after stdin closed.");
        }

        var stderr = await stderrTask;
        Assert.True(process.ExitCode == 0, "ipt-mcp server exited with code " + process.ExitCode + ". Stderr: " + stderr);

        var responses = ruleCall is null ? new[] { initialize, toolsList } : new[] { initialize, toolsList, ruleCall };

        Assert.DoesNotContain(responses, r => r["error"]?["code"]?.Value<int>() == -32601);
        Assert.DoesNotContain(responses, r => r["error"] is not null);
        return responses;
    }

    private static async Task<JObject> ReadJsonResponse(Process process, string label)
    {
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("No MCP response received for " + label + ".");
        }

        return JObject.Parse(line);
    }
}
