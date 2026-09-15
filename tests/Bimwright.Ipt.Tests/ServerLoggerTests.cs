using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Call journal v2 (improvement spec F1). The <c>finish</c> line keeps <c>success</c> at envelope
/// level and adds the script-level outcome (<c>data_ok</c>/<c>data_error</c>/<c>stdout_bytes</c>),
/// response size, add-in duration and target. New keys are always present (null when not
/// applicable) so journal analysis sees stable keys.
/// </summary>
public sealed class ServerLoggerTests : IDisposable
{
    private const string Session = "server-20260915T040830Z-18244";

    private static readonly string[] NullableFinishKeys =
    {
        "error", "error_code", "target_id", "response_bytes", "plugin_duration_ms",
        "data_ok", "data_error", "stdout_bytes",
    };

    // ---- ExtractDataOutcome ------------------------------------------------------------

    [Fact]
    public void SendCodeScriptFailureYieldsOkFalseErrorAndStdoutBytes()
    {
        var data = JToken.Parse("""{"ok":false,"stdout":"abc","error":"compile error: CS1002"}""");

        var (ok, error, stdoutBytes) = ServerLogger.ExtractDataOutcome(data);

        Assert.False(ok);
        Assert.Equal("compile error: CS1002", error);
        Assert.Equal(3, stdoutBytes);
    }

    [Theory]
    [InlineData("""{"name":"Extrusion1"}""")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("\"plain string\"")]
    [InlineData("""{"ok":"true","error":42,"stdout":["a"]}""")] // wrong JSON types are not an outcome
    public void NonScriptDataYieldsAllNull(string json)
    {
        AssertAllNull(ServerLogger.ExtractDataOutcome(JToken.Parse(json)));
    }

    [Fact]
    public void MissingDataYieldsAllNull()
    {
        AssertAllNull(ServerLogger.ExtractDataOutcome(null));
    }

    [Fact]
    public void BakedToolShapeYieldsOkTrueOnly()
    {
        var data = JToken.Parse("""{"ok":true,"tool_name":"x","results":[]}""");

        var (ok, error, stdoutBytes) = ServerLogger.ExtractDataOutcome(data);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(stdoutBytes);
    }

    [Fact]
    public void StdoutBytesCountsUtf8BytesNotChars()
    {
        // "ống": U+1ED1 encodes to 3 UTF-8 bytes, + "n" + "g" = 5 bytes for 3 chars.
        var data = new JObject { ["ok"] = true, ["stdout"] = "\u1ED1ng" };

        Assert.Equal(5, ServerLogger.ExtractDataOutcome(data).stdoutBytes);
    }

    // ---- TruncateError -----------------------------------------------------------------

    [Fact]
    public void TruncateErrorCapsAt300CharsWithoutSuffix()
    {
        Assert.Equal(new string('x', 300), ServerLogger.TruncateError(new string('x', 1000)));
    }

    [Fact]
    public void TruncateErrorKeepsStringAtLimitUnchanged()
    {
        var atLimit = new string('y', 300);

        Assert.Equal(atLimit, ServerLogger.TruncateError(atLimit));
    }

    [Fact]
    public void TruncateErrorPassesNullThrough()
    {
        Assert.Null(ServerLogger.TruncateError(null));
    }

    // ---- session_id --------------------------------------------------------------------

    [Fact]
    public void SessionIdFormatIsUtcStartStampAndPid()
    {
        var id = ServerLogger.BuildSessionId(new DateTime(2026, 9, 15, 4, 8, 30, DateTimeKind.Utc), 18244);

        Assert.Equal("server-20260915T040830Z-18244", id);
    }

    [Fact]
    public void ProcessSessionIdIsWellFormedAndStable()
    {
        var first = ServerLogger.SessionId;
        var second = ServerLogger.SessionId;

        Assert.Matches(@"^server-\d{8}T\d{6}Z-\d+$", first);
        Assert.EndsWith("-" + Environment.ProcessId, first);
        Assert.Equal(first, second);
    }

    // ---- ResolveLogPath ----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LogPathDefaultsToLocalAppDataBimwright(string? envOverride)
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bimwright", "ipt-mcp-calls.jsonl");

        Assert.Equal(expected, ServerLogger.ResolveLogPath(envOverride));
    }

    [Fact]
    public void LogPathHonorsEnvOverride()
    {
        Assert.Equal(@"X:\a\b.jsonl", ServerLogger.ResolveLogPath(@"X:\a\b.jsonl"));
    }

    // ---- BuildFinishEntry --------------------------------------------------------------

    [Fact]
    public void SendCodeScriptFailureIsEnvelopeSuccessWithDataOkFalse()
    {
        var data = JToken.Parse("""{"ok":false,"stdout":"","error":"compile error: CS1525: Invalid expression term ';'"}""");

        var entry = Finish(tool: "send_code", data: data);

        Assert.Equal("finish", (string?)entry["phase"]);
        Assert.Equal(Session, (string?)entry["session_id"]);
        Assert.Equal("req-1", (string?)entry["request_id"]);
        Assert.Equal("send_code", (string?)entry["tool"]);
        Assert.True((bool)entry["success"]!);
        Assert.Equal(120L, (long)entry["duration_ms"]!);
        Assert.Equal(JTokenType.Null, entry["error"]!.Type);
        Assert.Equal(JTokenType.Null, entry["error_code"]!.Type);
        Assert.Equal("inventor-2027-70780", (string?)entry["target_id"]);
        Assert.Equal(512L, (long)entry["response_bytes"]!);
        Assert.Equal(95L, (long)entry["plugin_duration_ms"]!);
        Assert.False((bool)entry["data_ok"]!);
        Assert.StartsWith("compile error", (string?)entry["data_error"]);
        Assert.Equal(0, (int)entry["stdout_bytes"]!);
    }

    [Fact]
    public void TypedToolEntryKeepsStableKeysSerializedAsNull()
    {
        var entry = Finish(tool: "get_document_info", targetId: null, responseBytes: null, pluginDurationMs: null,
            data: JToken.Parse("""{"name":"Part1.ipt"}"""));
        var line = entry.ToString(Formatting.None);

        foreach (var key in NullableFinishKeys)
        {
            Assert.True(entry.ContainsKey(key), $"missing key '{key}'");
            Assert.Equal(JTokenType.Null, entry[key]!.Type);
            Assert.Contains($"\"{key}\":null", line);
        }
    }

    [Fact]
    public void EnvelopeFailureRecordsErrorCodeSeparatelyFromMessage()
    {
        var entry = Finish(success: false, error: "TIMEOUT: STA dispatch timed out", errorCode: "TIMEOUT");

        Assert.False((bool)entry["success"]!);
        Assert.Equal("TIMEOUT", (string?)entry["error_code"]);
        Assert.Equal("TIMEOUT: STA dispatch timed out", (string?)entry["error"]);
        Assert.Equal(JTokenType.Null, entry["data_ok"]!.Type);
    }

    [Fact]
    public void DataErrorIsTruncatedTo300Chars()
    {
        var entry = Finish(data: new JObject { ["ok"] = false, ["error"] = new string('e', 1000) });

        Assert.Equal(new string('e', 300), (string?)entry["data_error"]);
    }

    [Fact]
    public void TimestampKeepsRoundTripUtcStringFormat()
    {
        var entry = Finish();

        Assert.Equal(JTokenType.String, entry["timestamp"]!.Type);
        var parsed = DateTime.ParseExact((string)entry["timestamp"]!, "o", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    // ---- journal integration (F1-R1 / F1-R3) ----------------------------------------------
    // TestJournalIsolation redirects BIMWRIGHT_INVENTOR_CALL_LOG to %TEMP%\ipt-mcp-tests\calls.jsonl
    // before any test runs, so these assertions read the isolated journal.

    private static string JournalPath =>
        ServerLogger.ResolveLogPath(Environment.GetEnvironmentVariable(ServerLogger.LogPathEnvVar));

    // The journal is append-only but shared with parallel tests and spawned server processes;
    // read with ReadWrite share so a concurrent append never trips a sharing violation.
    private static string[] ReadJournalLines()
    {
        using var fs = new FileStream(JournalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines.ToArray();
    }

    private static JObject[] AppendedFinishLines(int skip) =>
        ReadJournalLines().Skip(skip)
            .Where(l => l.Contains("\"phase\":\"finish\""))
            .Select(JObject.Parse)
            .ToArray();

    [Fact]
    public async Task NoTargetCallStillLeavesFinishLineWithErrorCode()
    {
        var dir = NewEmptyDescriptorDir();
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = dir.FullName });
        var skip = File.Exists(JournalPath) ? ReadJournalLines().Length : 0;

        var ex = await Assert.ThrowsAsync<InventorGatewayException>(
            () => client.SendAsync("no_target_probe_f1r1", new { }, CancellationToken.None));

        Assert.Equal("NO_TARGET", ex.Code);
        var entry = Assert.Single(AppendedFinishLines(skip)
            .Where(e => (string?)e["tool"] == "no_target_probe_f1r1"));
        Assert.False((bool)entry["success"]!);
        Assert.Equal("NO_TARGET", (string?)entry["error_code"]);
        Assert.Equal(JTokenType.Null, entry["target_id"]!.Type);
        Assert.Equal(JTokenType.Null, entry["response_bytes"]!.Type);
    }

    [Fact]
    public void MetaToolNoTargetPayloadIsJournaledAsDataOkFalse()
    {
        var dir = NewEmptyDescriptorDir();
        var meta = new MetaTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = dir.FullName }));
        var skip = File.Exists(JournalPath) ? ReadJournalLines().Length : 0;

        var payload = meta.GetCurrentTarget();

        Assert.Contains("NO_TARGET", payload);
        // Other tests may call the same meta tool in parallel and interleave journal lines
        // (a found-target payload carries no `ok`, i.e. data_ok=null), so assert on the
        // expected shape rather than a single positional line.
        var entries = AppendedFinishLines(skip)
            .Where(e => (string?)e["tool"] == "inventor_get_current_target")
            .ToArray();
        Assert.Contains(entries, e => (bool)e["success"]! && (bool?)e["data_ok"] == false);
    }

    // ---- helpers -----------------------------------------------------------------------

    private readonly List<DirectoryInfo> _descriptorDirs = new();

    // An empty descriptor directory means "no live target"; removed again in Dispose.
    private DirectoryInfo NewEmptyDescriptorDir()
    {
        var dir = Directory.CreateTempSubdirectory("ipt-logger-test-");
        _descriptorDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _descriptorDirs)
        {
            try { dir.Delete(recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private static JObject Finish(
        string tool = "send_code",
        bool success = true,
        string? error = null,
        string? errorCode = null,
        string? targetId = "inventor-2027-70780",
        long? responseBytes = 512,
        long? pluginDurationMs = 95,
        JToken? data = null)
        => ServerLogger.BuildFinishEntry(Session, "req-1", tool, success, durationMs: 120, error, errorCode,
            targetId, responseBytes, pluginDurationMs, data);

    private static void AssertAllNull((bool? ok, string? error, int? stdoutBytes) outcome)
    {
        Assert.Null(outcome.ok);
        Assert.Null(outcome.error);
        Assert.Null(outcome.stdoutBytes);
    }
}
