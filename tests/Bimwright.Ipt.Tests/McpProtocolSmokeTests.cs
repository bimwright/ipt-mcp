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
    }

    private static async Task<JObject[]> RunProtocolHandshake()
    {
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ipt-mcp server.");
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke-test","version":"0.0"}}}""");
        var initialize = await ReadJsonResponse(process, "initialize");

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
        var toolsList = await ReadJsonResponse(process, "tools/list");
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

        var responses = new[] { initialize, toolsList };

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
