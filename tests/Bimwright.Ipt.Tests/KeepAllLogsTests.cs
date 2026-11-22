using Bimwright.Ipt.Server.Reports;
using Bimwright.Ipt.Shared.Logging;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// BIMWRIGHT_KEEP_ALL_LOGS: on stops every automatic deletion of logs, journals and reports; rotation
/// still archives. The value is pinned through <see cref="LogRetentionPolicy.ReadValue"/>, never read from
/// the real environment. The collection runs alone, so pinning it on cannot reach parallel retention tests.
/// </summary>
[Collection("Process-wide privacy state")]
public sealed class KeepAllLogsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ipt-keep-all-" + Guid.NewGuid().ToString("N"));

    public KeepAllLogsTests()
    {
        Directory.CreateDirectory(_directory);
        McpLogger.LocalAppDataOverride = _directory;
        SendCodeJournal.LocalAppDataOverride = _directory;
    }

    public void Dispose()
    {
        LogRetentionPolicy.ReadValue = () => null;
        McpLogger.LocalAppDataOverride = null;
        SendCodeJournal.LocalAppDataOverride = null;
        try { Directory.Delete(_directory, true); } catch { }
    }

    private static void KeepAll(bool on) => LogRetentionPolicy.ReadValue = () => on ? "1" : null;

    // 28 UTF-8 bytes with the newline: one record per 30-byte file.
    private static string Record(int i) => "{\"i\":" + (10 + i) + ",\"pad\":\"xxxxxxxxxx\"}";

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData(" Yes ", true)]
    [InlineData("on", true)]
    [InlineData("ON", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("2", false)]
    [InlineData("enabled", false)]
    public void Keep_all_switch_accepts_only_1_true_yes_on(string? value, bool expected)
    {
        Assert.Equal(expected, LogRetentionPolicy.IsOn(value));
        LogRetentionPolicy.ReadValue = () => value;
        Assert.Equal(expected, LogRetentionPolicy.KeepAll);
    }

    [Fact]
    public void Keep_all_on_append_keeps_expired_and_over_budget_archives_and_still_rotates()
    {
        KeepAll(true);
        var path = Path.Combine(_directory, "records.jsonl");
        var expired = Path.Combine(_directory, "records-20200101-010101.jsonl");
        File.WriteAllText(expired, "{\"old\":true}\n");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-40));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 20; i++)
            BoundedLogFile.Append(path, Record(i), 30, 100, now: now); // group budget far exceeded, no throw

        var files = BoundedLogFile.FilesForPath(path);
        Assert.True(File.Exists(expired));
        Assert.Equal(21, files.Length);
        Assert.True(files.Sum(f => new FileInfo(f).Length) > 100);
        Assert.Equal(20, files.Where(f => f != expired).SelectMany(f => BoundedLogFile.ReadLines(f)).Select(JObject.Parse).Count());
    }

    [Fact]
    public void Keep_all_off_append_still_prunes_expired_and_over_budget_archives()
    {
        KeepAll(false);
        var path = Path.Combine(_directory, "records.jsonl");
        var expired = Path.Combine(_directory, "records-20200101-010101.jsonl");
        File.WriteAllText(expired, "{\"old\":true}\n");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-40));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 20; i++)
            BoundedLogFile.Append(path, Record(i), 30, 100, now: now);

        Assert.False(File.Exists(expired));
        Assert.InRange(BoundedLogFile.FilesForPath(path).Sum(f => new FileInfo(f).Length), 1, 100);
    }

    [Fact]
    public void Keep_all_on_startup_maintenance_rotates_an_expired_active_file_but_deletes_nothing()
    {
        KeepAll(true);
        var path = Path.Combine(_directory, "custom.log");
        File.WriteAllText(path, "active");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-40));
        var expired = Path.Combine(_directory, "custom-20200101-010101.log");
        File.WriteAllText(expired, new string('!', 500));
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-41));

        BoundedLogFile.Maintain(path, 100, 400);

        Assert.False(File.Exists(path)); // rotated to a timestamped archive
        Assert.True(File.Exists(expired));
        var files = BoundedLogFile.FilesForPath(path);
        Assert.Equal(2, files.Length);
        Assert.Contains(files, f => File.ReadAllText(f) == "active");
    }

    [Fact]
    public void Keep_all_off_startup_maintenance_still_deletes_expired_archives()
    {
        KeepAll(false);
        var path = Path.Combine(_directory, "custom.log");
        File.WriteAllText(path, "active");
        var expired = Path.Combine(_directory, "custom-20200101-010101.log");
        File.WriteAllText(expired, "expired");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-31));

        BoundedLogFile.Maintain(path, 100, 400);

        Assert.False(File.Exists(expired));
        Assert.Equal("active", File.ReadAllText(path));
    }

    [Fact]
    public void Keep_all_on_send_code_journal_is_not_purged_seven_days_after_persistence_is_off()
    {
        KeepAll(true);
        var now = DateTimeOffset.UtcNow;
        var active = new IptPrivacyConfig(key => key == IptPrivacyConfig.EnvPersistSendCodeBodies ? "1" : null);
        Assert.True(SendCodeJournal.TryAppend(active, "s", "return 42;", true, 1, null, null, now));
        var hash = Bimwright.Ipt.Shared.Security.BakeRedactor.HashBody("return 42;");
        var off = new IptPrivacyConfig(_ => null);

        SendCodeJournal.RunMaintenance(off, now);
        SendCodeJournal.RunMaintenance(off, now.AddDays(8));
        Assert.False(SendCodeJournal.TryAppend(off, "s", "return 1;", true, 1, null, null, now.AddDays(30)));

        Assert.True(File.Exists(SendCodeJournal.JournalPath));
        Assert.Equal("return 42;", SendCodeJournal.TryFindCodeByHash(hash));
        Assert.True(File.Exists(Path.Combine(_directory, "send-code-journal.disabled-at")));
    }

    [Fact]
    public void Keep_all_on_report_store_keeps_every_report_past_the_cap()
    {
        KeepAll(true);
        var directory = Path.Combine(_directory, "reports");
        var store = new ReportStore(directory);
        for (var i = 0; i < ReportStore.Keep + 3; i++) store.Save(new JObject { ["task_id"] = "t" + i });
        Assert.Equal(ReportStore.Keep + 3, Directory.GetFiles(directory, "*.json").Length);
    }

    [Fact]
    public void Keep_all_on_call_log_version_migration_keeps_legacy_files_and_writes_the_version()
    {
        KeepAll(true);
        File.WriteAllText(Path.Combine(_directory, "mcp-calls.version"), "0");
        File.WriteAllText(Path.Combine(_directory, "mcp-calls.jsonl"), "legacy body\n");
        File.WriteAllText(Path.Combine(_directory, "mcp-calls-20200101-010101.jsonl"), "legacy body\n");

        McpLogger.Initialize();

        Assert.Equal(2, BoundedLogFile.Files(_directory, "mcp-calls").Length);
        Assert.Equal("1", File.ReadAllText(Path.Combine(_directory, "mcp-calls.version")));
    }
}
