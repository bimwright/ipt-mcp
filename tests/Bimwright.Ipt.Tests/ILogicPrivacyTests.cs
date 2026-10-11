using Bimwright.Ipt.Shared.Logging;
using Bimwright.Ipt.Shared.Security;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

[Collection("Process-wide privacy state")]
public sealed class ILogicPrivacyTests : IDisposable
{
    private const string Body = "Dim privateDesignFormula = 271828";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-ilogic-privacy-" + Guid.NewGuid().ToString("N"));

    public ILogicPrivacyTests() => McpLogger.LocalAppDataOverride = _dir;

    public void Dispose()
    {
        McpLogger.LocalAppDataOverride = null;
        McpSessionLog.ConfigLoader = IptPrivacyConfig.Load;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Theory]
    [InlineData("ilogic_upsert_rule", "text")]
    [InlineData("ilogic_run_snippet", "code")]
    [InlineData("ILOGIC_UPSERT_RULE", "text")]
    [InlineData("ILOGIC_RUN_SNIPPET", "code")]
    public void Host_journal_never_persists_ilogic_source(string command, string field)
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(
            key => key == IptPrivacyConfig.EnvCacheSendCodeBodies ? "1" : null);
        McpLogger.Initialize();
        McpLogger.Log(command, Params(field), true, 1,
            code: field == "code" ? Body : null,
            resultJson: "{\"result\":\"privateComputedResult\"}", enabled: true);

        var line = Assert.Single(File.ReadAllLines(Path.Combine(_dir, "mcp-calls.jsonl")));
        var logged = JObject.Parse(line);
        Assert.DoesNotContain(Body, line);
        Assert.DoesNotContain("privateComputedResult", line);
        Assert.Equal(BakeRedactor.HashBody(Body), (string?)logged["params"]?["code_hash"]);
        Assert.Equal(Body.Length, (int?)logged["params"]?["code_length"]);
    }

    [Theory]
    [InlineData("ilogic_upsert_rule", "text")]
    [InlineData("ilogic_run_snippet", "code")]
    [InlineData("ILOGIC_UPSERT_RULE", "text")]
    [InlineData("ILOGIC_RUN_SNIPPET", "code")]
    public void History_drops_ilogic_source_without_cache_opt_in(string command, string field)
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(_ => null);
        var log = new McpSessionLog();
        log.Add(Entry(command, field));

        var entry = Assert.Single(log.Entries);
        var parameters = JObject.Parse(entry.ParamsJson!);
        Assert.DoesNotContain(Body, entry.ParamsJson);
        Assert.Null(entry.CodeSnippet);
        Assert.True(entry.SourceBodyRedacted);
        Assert.Equal(BakeRedactor.HashBody(Body), (string?)parameters["code_hash"]);
        Assert.Equal(Body.Length, (int?)parameters["code_length"]);
        Assert.Contains(command, entry.Summary);
        Assert.DoesNotContain("privateComputedResult", entry.ResultJson);
    }

    [Theory]
    [InlineData("ilogic_upsert_rule", "text")]
    [InlineData("ilogic_run_snippet", "code")]
    public void Cache_opt_in_keeps_ilogic_parameters_for_rerun(string command, string field)
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(
            key => key == IptPrivacyConfig.EnvCacheSendCodeBodies ? "1" : null);
        var log = new McpSessionLog();
        log.Add(Entry(command, field));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(Body, (string?)JObject.Parse(entry.ParamsJson!)[field]);
        Assert.Equal("Probe", (string?)JObject.Parse(entry.ParamsJson!)["name"]);
        Assert.False(entry.SourceBodyRedacted);
    }

    private static string Params(string field) => new JObject { ["name"] = "Probe", [field] = Body }.ToString();

    private static McpCallEntry Entry(string command, string field) => new()
    {
        ToolName = command, ParamsJson = Params(field),
        CodeSnippet = field == "code" ? Body : null,
        ResultJson = "{\"result\":\"privateComputedResult\"}", Success = true,
    };
}
