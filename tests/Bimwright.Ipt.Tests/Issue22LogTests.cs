using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Logging;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;

namespace Bimwright.Ipt.Tests;

[Collection("CallLog")]
public sealed class Issue22LogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ipt-issue22-logs-" + Guid.NewGuid().ToString("N"));
    private const int FileBudget = 5 * 1024 * 1024;

    public Issue22LogTests()
    {
        Directory.CreateDirectory(_directory);
        McpLogger.LocalAppDataOverride = _directory;
        SendCodeJournal.LocalAppDataOverride = _directory;
    }

    [Fact]
    public void Plugin_rotates_before_append_during_a_long_session()
    {
        McpLogger.Initialize();
        var path = Path.Combine(_directory, "mcp-calls.jsonl");
        File.WriteAllText(path, new string(' ', FileBudget - 8));
        McpLogger.Log("health", "{}", true, 1);
        Assert.True(new FileInfo(path).Length <= FileBudget, "The running session exceeded the per-file budget.");
        Assert.Single(Directory.GetFiles(_directory, "mcp-calls-*.jsonl"));
        Assert.Equal("health", JObject.Parse(File.ReadAllText(path)).Value<string>("tool"));
    }

    [Fact]
    public void Gateway_rotates_before_append_at_an_override_path()
    {
        var path = Path.Combine(_directory, "custom.jsonl");
        File.WriteAllText(path, new string(' ', FileBudget - 8));
        ServerLogger.Configure(true, path);
        ServerLogger.LogStart("r1", "health", null);
        Assert.True(new FileInfo(path).Length <= FileBudget, "The gateway journal exceeded the per-file budget.");
        Assert.Single(Directory.GetFiles(_directory, "custom-*.jsonl"));
        Assert.Equal("r1", JObject.Parse(File.ReadAllText(path)).Value<string>("request_id"));
    }

    [Fact]
    public void Send_code_rejects_one_body_larger_than_the_file_budget()
    {
        var config = ActiveConfig();
        Assert.False(SendCodeJournal.TryAppend(config, "s", new string('!', FileBudget), true, 1, null, null));
        Assert.False(File.Exists(SendCodeJournal.JournalPath));
    }

    [Fact]
    public void History_reads_only_owned_archive_names()
    {
        File.WriteAllText(Path.Combine(_directory, "mcp-calls-user-notes.jsonl"), "{\"tool\":\"not-owned\"}\n");
        var rows = SessionLogHistoryLoader.LoadPastSessions(_directory, "", 0);
        Assert.Empty(rows);
    }

    [Fact]
    public void Projected_utf8_bytes_rotate_and_unique_archives_preserve_all_records()
    {
        var path = Path.Combine(_directory, "records.jsonl");
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 20; i++)
            BoundedLogFile.Append(path, "{\"i\":" + i + ",\"s\":\"ống\"}", 30, 2000, now: now);
        var files = BoundedLogFile.FilesForPath(path);
        Assert.Equal(20, files.Length);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 30));
        Assert.Equal(20, files.SelectMany(file => BoundedLogFile.ReadLines(file)).Select(JObject.Parse).Count());
    }

    [Fact]
    public void Startup_maintenance_prunes_age_and_group_budget_without_touching_neighbors()
    {
        var path = Path.Combine(_directory, "custom.log");
        var unrelated = Path.Combine(_directory, "custom-user-notes.log");
        File.WriteAllText(unrelated, "keep");
        var old = Path.Combine(_directory, "custom-20200101-010101.log");
        File.WriteAllText(old, "expired");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));
        for (var i = 0; i < 12; i++)
            BoundedLogFile.Append(path, new string('!', 90), 100, 400);
        BoundedLogFile.Maintain(path, 100, 400);
        Assert.False(File.Exists(old));
        Assert.Equal("keep", File.ReadAllText(unrelated));
        Assert.InRange(BoundedLogFile.FilesForPath(path).Sum(p => new FileInfo(p).Length), 1, 400);
    }

    [Fact]
    public void Migration_deletes_only_owned_legacy_files_and_preserves_version_privacy()
    {
        File.WriteAllText(Path.Combine(_directory, "mcp-calls.version"), "0");
        File.WriteAllText(Path.Combine(_directory, "mcp-calls.jsonl"), "legacy body");
        File.WriteAllText(Path.Combine(_directory, "mcp-calls-20200101-010101.jsonl"), "legacy body");
        var unrelated = Path.Combine(_directory, "mcp-calls-user-notes.jsonl");
        File.WriteAllText(unrelated, "user data");
        McpLogger.Initialize();
        Assert.Empty(BoundedLogFile.Files(_directory, "mcp-calls"));
        Assert.Equal("user data", File.ReadAllText(unrelated));
        Assert.Equal("1", File.ReadAllText(Path.Combine(_directory, "mcp-calls.version")));
    }

    [Fact]
    public async Task Lock_timeout_never_falls_through_to_unlocked_send_code_append()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => BoundedLogFile.WithLock(SendCodeJournal.JournalPath, () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return true;
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        try
        {
            Assert.False(SendCodeJournal.TryAppend(ActiveConfig(), "s", "return 1;", true, 1, null, null));
            Assert.False(File.Exists(SendCodeJournal.JournalPath));
        }
        finally { release.Set(); await holder; }
        Assert.True(SendCodeJournal.TryAppend(ActiveConfig(), "s", "return 1;", true, 1, null, null));
    }

    [Fact]
    public void Disabled_body_persistence_retains_seven_day_purge_and_lookup()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(SendCodeJournal.TryAppend(ActiveConfig(), "s", "return 42;", true, 1, null, null, now));
        var hash = Bimwright.Ipt.Shared.Security.BakeRedactor.HashBody("return 42;");
        var off = new IptPrivacyConfig(_ => null);
        SendCodeJournal.RunMaintenance(off, now);
        Assert.Equal("return 42;", SendCodeJournal.TryFindCodeByHash(hash));
        SendCodeJournal.RunMaintenance(off, now.AddDays(6));
        Assert.True(File.Exists(SendCodeJournal.JournalPath));
        SendCodeJournal.RunMaintenance(off, now.AddDays(7));
        Assert.Empty(BoundedLogFile.Files(_directory, "send-code-journal"));
        Assert.False(File.Exists(Path.Combine(_directory, "send-code-journal.disabled-at")));
    }

    [Fact]
    public void Expired_body_persistence_and_default_off_do_not_append()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(SendCodeJournal.TryAppend(new IptPrivacyConfig(_ => null), "s", "return 1;", true, 1, null, null));
        Assert.False(SendCodeJournal.TryAppend(ActiveConfig(), "s", "return 1;", true, 1, null, null, now.AddDays(2)));
        Assert.False(File.Exists(SendCodeJournal.JournalPath));
    }

    [Fact]
    public void Bounded_readers_skip_giant_lines_and_history_keeps_newest_visible_entries()
    {
        var path = Path.Combine(_directory, "mcp-calls.jsonl");
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            writer.Write(new string('!', FileBudget + 1));
            writer.WriteLine();
            for (var i = 0; i < 200; i++)
                writer.WriteLine(new JObject { ["tool"] = "tool" + i, ["session_id"] = i >= 195 ? "live" : "past" }.ToString(Newtonsoft.Json.Formatting.None));
        }
        var rows = SessionLogHistoryLoader.LoadPastSessions(_directory, "live", 3, 4);
        Assert.Equal(new[] { "tool193", "tool194", "tool195", "tool196" }, rows.Select(row => row.ToolName));
        Assert.Equal(new[] { -4, -3, -2, -1 }, rows.Select(row => row.Index));
        Assert.Empty(SessionLogHistoryLoader.LoadPastSessions(_directory, "", 0, -1));
        Assert.Equal(new[] { "ok" }, BoundedLogFile.ReadLines(WriteFixture("bounded.txt", "oversized\r\nok\r\n"), 3));
    }

    [Fact]
    public void Gateway_large_params_keep_start_receipt_and_privacy_with_a_bounded_preview()
    {
        var path = Path.Combine(_directory, "gateway.jsonl");
        ServerLogger.Configure(true, path);
        ServerLogger.LogStart("large", "query", new JObject { ["password"] = "keep-secret", ["text"] = new string('!', FileBudget + 1) });
        var record = JObject.Parse(File.ReadAllText(path));
        Assert.Equal("large", record.Value<string>("request_id"));
        Assert.Equal("start", record.Value<string>("phase"));
        Assert.True(record["params"]!.Value<bool>("truncated"));
        Assert.DoesNotContain("keep-secret", File.ReadAllText(path));
        Assert.InRange(new FileInfo(path).Length, 1, 100 * 1024);
    }

    [Fact]
    public void Active_file_and_directory_links_are_refused_and_archive_links_are_untouched()
    {
        var target = WriteFixture("user-data.txt", "preserve");
        var linked = Path.Combine(_directory, "linked.jsonl");
        File.CreateSymbolicLink(linked, target);
        Assert.Throws<IOException>(() => BoundedLogFile.Append(linked, "{}"));
        var archive = Path.Combine(_directory, "records-20200101-010101.jsonl");
        File.CreateSymbolicLink(archive, target);
        var path = Path.Combine(_directory, "records.jsonl");
        BoundedLogFile.Maintain(path);
        Assert.Empty(BoundedLogFile.FilesForPath(path));
        Assert.True(File.Exists(archive));
        var linkedDirectory = Path.Combine(_directory, "linked-directory");
        Directory.CreateSymbolicLink(linkedDirectory, _directory);
        Assert.Throws<IOException>(() => BoundedLogFile.Append(Path.Combine(linkedDirectory, "other.jsonl"), "{}"));
        Assert.Equal("preserve", File.ReadAllText(target));
    }

    [Fact]
    public async Task Four_process_writers_rotate_under_one_mutex_without_losing_json_records()
    {
        var script = WriteFixture("writer.ps1", """
            param($assemblyPath, $journalPath, $writerId)
            $ErrorActionPreference = 'Stop'
            [void][Reflection.Assembly]::LoadFrom($assemblyPath)
            for ($i = 0; $i -lt 40; $i++) {
                $line = '{"writer":' + $writerId + ',"i":' + $i + ',"padding":"' + ('!' * 100) + '"}'
                [Bimwright.Ipt.Shared.Logging.BoundedLogFile]::Append($journalPath, $line, 4096, 65536, 30, $null)
            }
            """);
        var path = Path.Combine(_directory, "multi.jsonl");
        var processes = new List<Process>();
        try
        {
            for (var i = 0; i < 4; i++)
                processes.Add(StartPowerShell(script, typeof(BoundedLogFile).Assembly.Location, path, i.ToString()));
            foreach (var process in processes)
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            }
        }
        finally { foreach (var process in processes) { if (!process.HasExited) process.Kill(true); process.Dispose(); } }
        var files = BoundedLogFile.FilesForPath(path);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 4096));
        var records = files.SelectMany(file => BoundedLogFile.ReadLines(file)).Select(JObject.Parse).ToArray();
        Assert.Equal(160, records.Length);
        Assert.Equal(160, records.Select(row => (row.Value<int>("writer"), row.Value<int>("i"))).Distinct().Count());
    }

    [Fact]
    public void Locked_archive_prevents_append_instead_of_exceeding_the_group_budget()
    {
        var path = Path.Combine(_directory, "locked.jsonl");
        BoundedLogFile.Append(path, new string('!', 90), 100, 100);
        BoundedLogFile.Append(path, new string('?', 90), 100, 200);
        var archive = Assert.Single(BoundedLogFile.FilesForPath(path).Where(file => file != path));
        using (var locked = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => BoundedLogFile.Append(path, new string('+', 90), 100, 100));
        Assert.InRange(BoundedLogFile.FilesForPath(path).Sum(file => new FileInfo(file).Length), 1, 200);
        Assert.DoesNotContain(BoundedLogFile.FilesForPath(path).SelectMany(file => BoundedLogFile.ReadLines(file)), line => line.Contains('+'));
    }

    [Fact]
    public void Utf8_oversized_record_is_rejected_before_any_file_is_created()
    {
        var path = Path.Combine(_directory, "utf8.jsonl");
        Assert.Throws<InvalidDataException>(() => BoundedLogFile.Append(path, new string('\u1ed1', 40), 100, 200));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Old_creation_time_does_not_rotate_a_file_written_today()
    {
        var path = WriteFixture("tunneled.jsonl", "{\"first\":true}\n");
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        BoundedLogFile.Append(path, "{\"second\":true}");
        Assert.Single(BoundedLogFile.FilesForPath(path));
        Assert.Equal(2, BoundedLogFile.ReadLines(path).Count());
    }

    [Fact]
    public void Maintenance_retains_a_recent_oversized_legacy_journal_and_usable_body_lookup()
    {
        var path = SendCodeJournal.JournalPath;
        var body = "return 42;";
        var hash = Bimwright.Ipt.Shared.Security.BakeRedactor.HashBody(body);
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            writer.WriteLine(new JObject { ["code_hash"] = hash, ["code"] = body }.ToString(Newtonsoft.Json.Formatting.None));
            writer.WriteLine(new string('!', 6 * 1024 * 1024));
        }
        var originalLength = new FileInfo(path).Length;
        SendCodeJournal.RunMaintenance(ActiveConfig());
        Assert.False(File.Exists(path));
        var archive = Assert.Single(BoundedLogFile.FilesForPath(path));
        Assert.Equal(originalLength, new FileInfo(archive).Length);
        Assert.Equal(body, SendCodeJournal.TryFindCodeByHash(hash));
    }

    [Fact]
    public async Task Abandoned_process_mutex_is_acquired_safely_by_the_next_writer()
    {
        var script = WriteFixture("holder.ps1", """
            param($assemblyPath, $journalPath, $signalPath)
            $ErrorActionPreference = 'Stop'
            [void][Reflection.Assembly]::LoadFrom($assemblyPath)
            [Bimwright.Ipt.Shared.Logging.BoundedLogFile]::WithLock($journalPath, [Func[bool]] {
                [IO.File]::WriteAllText($signalPath, 'held')
                Start-Sleep -Seconds 30
                return $true
            }, 2000)
            """);
        var path = Path.Combine(_directory, "abandoned.jsonl");
        var signal = Path.Combine(_directory, "held.txt");
        using (var process = StartPowerShell(script, typeof(BoundedLogFile).Assembly.Location, path, signal))
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(signal) && !process.HasExited && DateTime.UtcNow < deadline) await Task.Delay(30);
                Assert.True(File.Exists(signal), process.HasExited ? await process.StandardError.ReadToEndAsync() : "Child did not acquire the lock.");
            }
            finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
        }
        BoundedLogFile.Append(path, "{\"after\":true}");
        Assert.True(JObject.Parse(File.ReadAllText(path)).Value<bool>("after"));
    }

    private string WriteFixture(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Process StartPowerShell(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private static IptPrivacyConfig ActiveConfig() => new IptPrivacyConfig(
        key => key == IptPrivacyConfig.EnvPersistSendCodeBodies ? "1" : null);

    public void Dispose()
    {
        McpLogger.LocalAppDataOverride = null;
        SendCodeJournal.LocalAppDataOverride = null;
        ServerLogger.Configure(false, ServerLogger.ResolveLogPath(Environment.GetEnvironmentVariable(ServerLogger.LogPathEnvVar)));
        try { Directory.Delete(_directory, true); } catch { }
    }
}
