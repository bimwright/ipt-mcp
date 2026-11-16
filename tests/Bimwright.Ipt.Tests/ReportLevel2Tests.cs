using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Reports;
using Bimwright.Ipt.Server.Tools;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>Family contract FC-RPT-04 (saved reports, history, body) and FC-RPT-05 (_report reminder).</summary>
public sealed class ReportLevel2Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ipt-reports-" + Guid.NewGuid().ToString("N"));
    private readonly PluginClient _client;
    private readonly QueryTools _tools;

    public ReportLevel2Tests()
    {
        Directory.CreateDirectory(_root);
        // No descriptors: Inventor is not reachable, so the toast cannot show but the report is still saved.
        _client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root, ReportDirectory = Path.Combine(_root, "reports"), TimeoutMs = 1000 });
        _tools = new QueryTools(_client);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private JObject Report(string taskId, string outcome = "completed", string summary = "Done", string? body = null)
        => JObject.Parse(_tools.ReportTaskResult(taskId, outcome, summary, body).GetAwaiter().GetResult());

    private string[] Files() => Directory.Exists(Path.Combine(_root, "reports")) ? Directory.GetFiles(Path.Combine(_root, "reports"), "*.json") : Array.Empty<string>();

    [Fact]
    public void A_report_is_saved_with_its_body_even_when_inventor_is_not_reachable()
    {
        var r = Report("job-1", body: "## Changed\n- Flange thickness 8 mm");
        Assert.True((bool)r["report_saved"]!);
        Assert.False((bool)r["toast_shown"]!);
        Assert.Equal("NO_TARGET", (string?)r["toast_error"]!["code"]);
        Assert.True((bool)r["agent_reported"]!);
        var id = (string)r["report_id"]!;
        Assert.Matches("^[0-9a-f]{32}$", id);
        var file = Assert.Single(Files());
        Assert.EndsWith("-" + id + ".json", file);

        var saved = JObject.Parse(_tools.GetReportHistory(id));
        Assert.Equal("job-1", (string?)saved["task_id"]);
        Assert.Equal("## Changed\n- Flange thickness 8 mm", (string?)saved["body"]);
        Assert.Equal(1, (int)saved["schema"]!);
        Assert.NotNull(saved["source"]!["server_session"]);
    }

    [Fact]
    public void Invalid_reports_save_nothing()
    {
        Assert.Equal("INVALID_ARGUMENT", (string?)Report("job", outcome: "running")["error"]!["code"]);
        Assert.Equal("INVALID_ARGUMENT", (string?)Report("job", summary: "two\nlines")["error"]!["code"]);
        Assert.Equal("INVALID_ARGUMENT", (string?)Report(new string('x', 81))["error"]!["code"]);
        Assert.Equal("INVALID_ARGUMENT", (string?)Report("job", body: new string('x', 32 * 1024 + 1))["error"]!["code"]);
        Assert.Null(Report("job-ok", body: new string('x', 32 * 1024))["error"]);
        Assert.Single(Files());
    }

    [Fact]
    public void Resending_a_task_id_replaces_its_report_and_keeps_its_recorded_writes()
    {
        _client.Reports.Record("extrude", new JObject(), "req-1", "Bracket.ipt");
        var first = Report("job-2", summary: "First try");
        Assert.Equal(1, (int)first["call_count"]!);
        var second = Report("job-2", outcome: "failed", summary: "Second try");
        Assert.Equal((string)first["report_id"]!, (string)second["report_id"]!);
        Assert.True((bool)second["replaced"]!);
        Assert.Single(Files());
        var saved = JObject.Parse(_tools.GetReportHistory((string)first["report_id"]!));
        Assert.Equal("failed", (string?)saved["outcome"]);
        Assert.Equal("inventor_extrude", (string?)((JArray)saved["calls"]!).Single()["tool"]);
    }

    [Fact]
    public void History_lists_newest_first_with_paging()
    {
        Report("job-a");
        Thread.Sleep(5);
        Report("job-b", body: "x");
        var page = JObject.Parse(_tools.GetReportHistory(limit: 1));
        Assert.Equal(2, (int)page["total"]!);
        var newest = ((JArray)page["reports"]!).Single();
        Assert.Equal("job-b", (string?)newest["task_id"]);
        Assert.True((bool)newest["has_body"]!);
        Assert.Null(newest["body"]);
        Assert.Equal("job-a", (string?)((JArray)JObject.Parse(_tools.GetReportHistory(offset: 1))["reports"]!).Single()["task_id"]);
    }

    [Theory]
    [InlineData("nothex", 0, 10)]
    [InlineData(null, -1, 10)]
    [InlineData(null, 0, 0)]
    [InlineData(null, 0, 51)]
    [InlineData("0123456789abcdef0123456789abcdef", 0, 10)]   // well-formed but not saved
    public void History_arguments_are_checked(string? id, int offset, int limit)
        => Assert.Equal("INVALID_ARGUMENT", (string?)JObject.Parse(_tools.GetReportHistory(id, offset, limit))["error"]!["code"]);

    [Fact]
    public void Only_the_newest_reports_are_kept()
    {
        var store = new ReportStore(Path.Combine(_root, "many"));
        for (var i = 0; i < ReportStore.Keep + 3; i++) store.Save(new JObject { ["task_id"] = "t" + i });
        Assert.Equal(ReportStore.Keep, Directory.GetFiles(Path.Combine(_root, "many"), "*.json").Length);
    }

    // ---- FC-RPT-05 ----

    private static ReportTracker Tracker() => new(command => command switch
    {
        "list_parameters" => true,
        "extrude" or "open_document" or "export_step" or "send_code" or "close_document" => false,
        _ => null,
    });

    [Fact]
    public void Model_writes_carry_a_reminder_until_the_report_full_then_short()
    {
        var t = Tracker();
        Assert.Equal(ReportTracker.Text, t.Record("extrude", null, "1", "A.ipt"));
        Assert.Equal(ReportTracker.ShortText, t.Record("extrude", null, "2", "A.ipt"));
        var (calls, truncated) = t.TakeCalls();
        Assert.Equal(2, calls.Count);
        Assert.False(truncated);
        Assert.Equal(ReportTracker.Text, t.Record("extrude", null, "3", "A.ipt"));   // a new job starts over
    }

    [Fact]
    public void Reads_files_exports_and_commands_without_a_tool_carry_no_reminder()
    {
        var t = Tracker();
        Assert.Null(t.Record("list_parameters", null, "1", null));
        Assert.Null(t.Record("open_document", null, "2", null));
        Assert.Null(t.Record("close_document", null, "3", null));
        Assert.Null(t.Record("export_step", null, "4", null));
        Assert.Null(t.Record("health", null, "5", null));
        Assert.Empty(t.TakeCalls().Calls);
    }

    [Fact]
    public void A_script_gets_one_conditional_reminder_and_compile_only_none()
    {
        var t = Tracker();
        Assert.Null(t.Record("send_code", new JObject { ["compile_only"] = true }, "1", null));
        Assert.Equal(ReportTracker.ScriptText, t.Record("send_code", new JObject(), "2", null));
        Assert.Null(t.Record("send_code", new JObject(), "3", null));
        Assert.Equal(ReportTracker.Text, t.Record("extrude", null, "4", null));   // a definite change still reminds
        Assert.Null(t.Record("send_code", new JObject(), "5", null));
        var calls = t.TakeCalls().Calls;
        Assert.Equal(4, calls.Count);
        Assert.True((bool)calls[0]["unconfirmed_change"]!);
    }

    [Fact]
    public void Recorded_writes_are_bounded()
    {
        var t = Tracker();
        for (var i = 0; i < ReportTracker.MaxCalls + 5; i++) t.Record("extrude", null, i.ToString(), null);
        var (calls, truncated) = t.TakeCalls();
        Assert.Equal(ReportTracker.MaxCalls, calls.Count);
        Assert.True(truncated);
    }

    [Fact]
    public void The_gateway_classifies_commands_by_their_tool_annotation()
    {
        var t = _client.Reports;
        Assert.Null(t.Record("list_parameters", null, "1", null));          // ReadOnly = true
        Assert.Equal(ReportTracker.Text, t.Record("create_parameter", null, "2", null));
    }
}
