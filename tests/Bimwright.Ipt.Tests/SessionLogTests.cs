using System;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Shared.Logging;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Session-log / journal / history-loader unit tests — the ipt-mcp port of rvt-mcp's
/// MCP Command History plumbing (no Inventor, no WPF).
/// </summary>
public class SessionLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-log-" + Guid.NewGuid().ToString("N"));

    public SessionLogTests()
    {
        McpLogger.LocalAppDataOverride = _dir;
        SendCodeJournal.LocalAppDataOverride = _dir;
    }

    public void Dispose()
    {
        McpLogger.LocalAppDataOverride = null;
        SendCodeJournal.LocalAppDataOverride = null;
        McpSessionLog.ConfigLoader = IptPrivacyConfig.Load;
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- McpSessionLog ----------

    [Fact]
    public void SendCode_body_is_redacted_to_hash_by_default()
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(_ => null);
        var log = new McpSessionLog();
        log.Add(new McpCallEntry
        {
            ToolName = "send_code",
            ParamsJson = "{\"code\":\"app.ActiveDocument.Save()\"}",
            CodeSnippet = "app.ActiveDocument.Save()",
            Success = true,
        });

        var e = log.Entries.Single();
        var p = JObject.Parse(e.ParamsJson!);
        Assert.Null(e.CodeSnippet);
        Assert.Equal("send_code", e.ToolName);
        Assert.True(p.Value<string>("code_hash")!.Length == 40);
        Assert.Equal(25, p.Value<int>("code_length"));
        Assert.Contains("code_hash", e.Summary);
    }

    [Fact]
    public void SendCode_body_is_kept_when_cache_enabled()
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(
            n => n == IptPrivacyConfig.EnvCacheSendCodeBodies ? "1" : null);
        var log = new McpSessionLog();
        log.Add(new McpCallEntry
        {
            ToolName = "send_code",
            ParamsJson = "{\"code\":\"return 1;\"}",
            CodeSnippet = "return 1;",
            Success = true,
        });

        var e = log.Entries.Single();
        Assert.Equal("return 1;", e.CodeSnippet);
        Assert.Contains("return 1;", e.ParamsJson);
    }

    [Fact]
    public void Oversized_params_are_truncated_and_flagged()
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(_ => null);
        var log = new McpSessionLog();
        log.Add(new McpCallEntry
        {
            ToolName = "batch_execute",
            ParamsJson = new string('x', 70 * 1024),
            Success = true,
        });

        var e = log.Entries.Single();
        Assert.True(e.ParamsTruncated);
        Assert.True(e.ParamsJson!.Length < 70 * 1024);
        Assert.EndsWith("... (truncated)", e.ParamsJson);
    }

    [Fact]
    public void Entries_are_capped_at_1000_evicting_oldest()
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(_ => null);
        var log = new McpSessionLog();
        for (var i = 0; i < 1010; i++)
            log.Add(new McpCallEntry { ToolName = "health", Success = true });

        Assert.Equal(1000, log.Entries.Count);
        Assert.Equal(11, log.Entries[0].Index);   // first 10 evicted; indexes still count up
    }

    [Fact]
    public void Clear_drops_live_rows_but_keeps_historical()
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(_ => null);
        var log = new McpSessionLog();
        log.Entries.Add(new McpCallEntry { ToolName = "old", IsHistorical = true, Index = -1 });
        log.Add(new McpCallEntry { ToolName = "live", Success = true });

        log.Clear();

        Assert.Single(log.Entries);
        Assert.True(log.Entries[0].IsHistorical);
        // next live entry restarts at index 1
        log.Add(new McpCallEntry { ToolName = "live2", Success = true });
        Assert.Equal(1, log.Entries[1].Index);
    }

    [Fact]
    public void Add_marshals_collection_mutation_through_the_post_delegate()
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(_ => null);
        var marshalled = 0;
        var log = new McpSessionLog(a => { marshalled++; a(); });
        log.Add(new McpCallEntry { ToolName = "t", Success = true });
        Assert.Equal(1, marshalled);
        Assert.Single(log.Entries);
    }

    [Fact]
    public void Error_messages_are_sanitized_and_redacted()
    {
        McpSessionLog.ConfigLoader = () => new IptPrivacyConfig(_ => null);
        var log = new McpSessionLog();
        log.Add(new McpCallEntry
        {
            ToolName = "open_document",
            Success = false,
            ErrorMessage = "cannot open D:\\secret\\model.ipt\nline2",
        });

        var e = log.Entries.Single();
        Assert.DoesNotContain("D:\\secret", e.ErrorMessage);
        Assert.DoesNotContain("\n", e.ErrorMessage);
    }

    // ---------- SummaryGenerator ----------

    [Fact]
    public void Summary_uses_first_line_of_send_code_result()
    {
        var s = SummaryGenerator.Generate("send_code", "{}", "{\"result\":\"made a box\\nsecond line\"}", true, null);
        Assert.Equal("made a box", s);
    }

    [Fact]
    public void Summary_for_task_result_reads_summary_param()
    {
        var s = SummaryGenerator.Generate("report_task_result",
            "{\"task_id\":\"t1\",\"outcome\":\"completed\",\"summary\":\"extrude done\"}",
            "{\"ok\":true}", true, null);
        Assert.Equal("extrude done", s);
    }

    [Fact]
    public void Summary_falls_back_to_error_and_generic_counts()
    {
        Assert.Equal("boom",
            SummaryGenerator.Generate("extrude", "{}", null, false, "boom"));
        Assert.Equal("5 items",
            SummaryGenerator.Generate("list_bodies", "{}", "{\"count\":5}", true, null));
    }

    // ---------- PersistSendCodeTtl / IptPrivacyConfig ----------

    [Theory]
    [InlineData("4h", 4 * 60)]
    [InlineData("30m", 30)]
    [InlineData("2d", 2 * 24 * 60)]
    [InlineData("bogus", -1)]
    [InlineData("", -1)]
    public void Ttl_parses_h_m_d_shapes(string input, int expectedMinutes)
    {
        var ok = PersistSendCodeTtl.TryParse(input, out var v);
        if (expectedMinutes < 0) { Assert.False(ok); return; }
        Assert.True(ok);
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), v);
    }

    [Fact]
    public void Persist_window_is_ttl_after_activation()
    {
        var cfg = new IptPrivacyConfig(n =>
            n == IptPrivacyConfig.EnvPersistSendCodeBodies ? "1" :
            n == IptPrivacyConfig.EnvPersistSendCodeBodiesTtl ? "4h" : null);
        Assert.True(cfg.IsPersistSendCodeBodiesActive(DateTimeOffset.UtcNow));

        var off = new IptPrivacyConfig(_ => null);
        Assert.False(off.IsPersistSendCodeBodiesActive(DateTimeOffset.UtcNow));
    }

    // ---------- McpLogger + SendCodeJournal + HistoryLoader (file-backed) ----------

    [Fact]
    public void Journal_writes_sanitized_lines_and_loader_reads_them_back()
    {
        McpLogger.Initialize();
        McpLogger.Log("extrude", "{\"distance_mm\":10}", true, 42, resultJson: "{\"count\":1}");
        McpLogger.Log("send_code", "{\"code\":\"var x = 'D:\\\\a\\\\p.ipt';\"}", false, 5, "kaboom");

        var lines = File.ReadAllLines(Path.Combine(_dir, "mcp-calls.jsonl"));
        Assert.Equal(2, lines.Length);

        var sendLine = JObject.Parse(lines[1]);
        // send_code params collapse to hash+length — the body never lands in the file
        Assert.NotNull(sendLine["params"]!["code_hash"]);
        Assert.DoesNotContain("a\\\\p.ipt", lines[1]);

        var sessionId = McpLogger.CurrentSessionId;
        // Loader hides the last liveSessionCount rows of the current session
        var entries = SessionLogHistoryLoader.LoadPastSessions(_dir, sessionId, liveSessionCount: 1);
        var historical = entries.Where(e => e.SessionTag != null).ToList();
        Assert.Single(historical);                 // second live row hidden, first becomes history
        Assert.Equal("extrude", historical[0].ToolName);
        Assert.True(historical[0].IsHistorical);
        Assert.True(historical[0].Index < 0);      // negative index space
    }

    [Fact]
    public void Send_code_journal_round_trips_a_body_by_hash_when_active()
    {
        var active = new IptPrivacyConfig(n =>
            n == IptPrivacyConfig.EnvPersistSendCodeBodies ? "1" : null);
        var inactive = new IptPrivacyConfig(_ => null);

        Assert.False(SendCodeJournal.TryAppend(inactive, "s1", "return 1;", true, 1, null, "{}"));
        Assert.True(SendCodeJournal.TryAppend(active, "s1", "return 1;", true, 1, null, "{}"));

        var hash = Bimwright.Ipt.Shared.Security.BakeRedactor.HashBody("return 1;");
        var body = SendCodeJournal.TryFindCodeByHash(hash);
        Assert.Equal("return 1;", body);
        Assert.Null(SendCodeJournal.TryFindCodeByHash("deadbeef"));
    }
}
