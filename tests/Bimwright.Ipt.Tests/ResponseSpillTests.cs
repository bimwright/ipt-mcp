using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Spec F3-b: oversized send_code stdout / run_baked_tool results spill to
/// <c>%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\</c> (24 h TTL, 50-file cap).
/// </summary>
public sealed class ResponseSpillTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-spill-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ShouldSpill_AtThreshold()
    {
        Assert.False(ResponseSpillWriter.ShouldSpill(new string('x', ResponseSpillWriter.SpillThresholdBytes)));
        Assert.True(ResponseSpillWriter.ShouldSpill(new string('x', ResponseSpillWriter.SpillThresholdBytes + 1)));
        Assert.False(ResponseSpillWriter.ShouldSpill(null));
        Assert.False(ResponseSpillWriter.ShouldSpill(""));
    }

    [Fact]
    public void Utf8Prefix_DoesNotSplitMultibyteChar()
    {
        // 'ố' is 3 UTF-8 bytes — a cut landing inside it must back off to the char boundary.
        var text = new string('a', 10) + "ố" + new string('b', 10);
        var prefix = ResponseSpillWriter.Utf8Prefix(text, 12);   // cuts inside 'ố'
        Assert.Equal(new string('a', 10), prefix);

        Assert.Equal(text, ResponseSpillWriter.Utf8Prefix(text, 10_000));
        Assert.Equal("", ResponseSpillWriter.Utf8Prefix(null!, 8));
    }

    [Fact]
    public void Write_PersistsContentUnderNamedFile()
    {
        var w = new ResponseSpillWriter(_dir);
        var path = w.Write("send_code", ".txt", "hello spill");

        Assert.True(File.Exists(path));
        Assert.StartsWith(Path.GetFullPath(_dir), path);
        Assert.Matches(@"send_code-\d{8}-\d{6}-[0-9a-f]{8}\.txt$", Path.GetFileName(path));
        Assert.Equal("hello spill", File.ReadAllText(path));
    }

    [Fact]
    public void AttachStdout_SmallOutputStaysInline()
    {
        var data = new JObject();
        ResponseSpillWriter.AttachStdout("send_code", data, "tiny");
        Assert.Equal("tiny", data["stdout"]!.Value<string>());
        Assert.Null(data["stdout_truncated"]);
        Assert.Null(data["stdout_file"]);
    }

    [Fact]
    public void AttachStdout_LargeOutputSpillsWithPrefix()
    {
        var stdout = new string('y', ResponseSpillWriter.SpillThresholdBytes + 1000);
        var data = new JObject();
        ResponseSpillWriter.AttachStdout("send_code", data, stdout, new ResponseSpillWriter(_dir));

        Assert.Equal(true, data["stdout_truncated"]!.Value<bool>());
        var file = data["stdout_file"]!.Value<string>();
        Assert.True(File.Exists(file));
        Assert.Equal(stdout, File.ReadAllText(file));                  // full text spilled
        var inline = data["stdout"]!.Value<string>();
        Assert.True(inline!.Length <= ResponseSpillWriter.InlineKeepBytes);
        Assert.Equal(stdout.Substring(0, inline.Length), inline);      // inline is a true prefix
    }

    [Fact]
    public void AttachResults_LargeArraySpillsWithPreview()
    {
        // pad uses '.' — a run of [A-Za-z0-9+/=]{24,} would trip the pre-spill secret masker.
        var results = new JArray(Enumerable.Range(0, 3000).Select(i => new JObject { ["n"] = i, ["pad"] = new string('.', 30) }));
        Assert.True(ResponseSpillWriter.ShouldSpill(results.ToString(Newtonsoft.Json.Formatting.None)));

        var data = new JObject();
        ResponseSpillWriter.AttachResults("run_baked_tool", data, results, new ResponseSpillWriter(_dir));

        Assert.Null(data["results"]);
        Assert.Equal(true, data["results_truncated"]!.Value<bool>());
        Assert.Equal(3000, data["results_count"]!.Value<int>());
        var file = data["results_file"]!.Value<string>();
        Assert.EndsWith(".json", file);
        Assert.Equal(results.ToString(Newtonsoft.Json.Formatting.None), File.ReadAllText(file));
        Assert.True(data["results_preview"]!.Value<string>()!.Length <= ResponseSpillWriter.InlineKeepBytes);
    }

    [Fact]
    public void AttachResults_SanitizesErrorFieldsBeforeSpill()
    {
        // Review fix: the dispatcher sanitizes error/message fields only AFTER AttachResults
        // returns, so the spill file and results_preview must be cleaned here — a raw path or
        // secret in a step error must not persist to disk.
        var path = @"C:\Users\Somebody\secret-project\part.ipt";
        var secret = "Z9x8Y7w6V5u4T3s2R1q0P9o8N7m6";   // 28 chars — matches the token heuristic
        var results = new JArray(
            new JObject { ["ok"] = false, ["error"] = $"cannot open {path} auth_token \"{secret}\"" },
            new JObject { ["ok"] = true, ["pad"] = new string('.', ResponseSpillWriter.SpillThresholdBytes + 1000) });

        var data = new JObject();
        ResponseSpillWriter.AttachResults("batch_execute", data, results, new ResponseSpillWriter(_dir));

        Assert.Equal(true, data["results_truncated"]!.Value<bool>());
        Assert.Equal(2, data["results_count"]!.Value<int>());

        var spilled = File.ReadAllText(data["results_file"]!.Value<string>()!);
        var preview = data["results_preview"]!.Value<string>()!;
        foreach (var content in new[] { spilled, preview })
        {
            Assert.DoesNotContain(path, content);
            Assert.DoesNotContain(secret, content);
            Assert.Contains("<path>", content);
        }
        // The in-place error-field pass mirrors what the dispatcher does to inline data.
        Assert.Contains("<path>", (string?)results[0]!["error"]);
    }

    [Fact]
    public void AttachResults_SmallArrayStaysInline()
    {
        var data = new JObject();
        var results = new JArray(1, 2, 3);
        ResponseSpillWriter.AttachResults("run_baked_tool", data, results);
        Assert.Equal(3, data["results"]!.Count());
        Assert.Null(data["results_truncated"]);
    }

    [Fact]
    public void Cleanup_DeletesExpiredButNeverEvictsFreshFiles()
    {
        var w = new ResponseSpillWriter(_dir);
        Directory.CreateDirectory(_dir);

        // 3 files backdated past the 36 h TTL
        for (var i = 0; i < 3; i++)
        {
            var p = Path.Combine(_dir, $"old-{i}.txt");
            File.WriteAllText(p, "x");
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow - TimeSpan.FromHours(37));
        }
        // MaxRetainedFiles + 5 fresh files
        for (var i = 0; i < ResponseSpillWriter.MaxRetainedFiles + 5; i++)
        {
            var p = Path.Combine(_dir, $"new-{i:D4}.txt");
            File.WriteAllText(p, "x");
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow - TimeSpan.FromMinutes(ResponseSpillWriter.MaxRetainedFiles + 5 - i));
        }

        var deleted = w.Cleanup(DateTime.UtcNow);
        Assert.Equal(3, deleted);
        Assert.Equal(ResponseSpillWriter.MaxRetainedFiles + 5, Directory.GetFiles(_dir).Length);
        // Every fresh spill survives, including the oldest, beyond the cleanup batch size.
        var names = Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray();
        Assert.Contains("new-0054.txt", names);
        Assert.Contains("new-0000.txt", names);
    }

    [Fact]
    public void Cleanup_EmptyOrMissingDirIsNoOp()
    {
        var w = new ResponseSpillWriter(Path.Combine(_dir, "does-not-exist"));
        Assert.Equal(0, w.Cleanup(DateTime.UtcNow));
    }

    [Theory]
    [InlineData(36, 35, false)]
    [InlineData(36, 37, true)]
    [InlineData(48, 47, false)]
    [InlineData(48, 49, true)]
    public void Cleanup_uses_configured_age_without_count_eviction(int retention, int age, bool expired)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "age.txt"); File.WriteAllText(path, "fixture");
        var now = DateTime.UtcNow; File.SetLastWriteTimeUtc(path, now.AddHours(-age));
        Assert.Equal(expired ? 1 : 0, new ResponseSpillWriter(_dir, retention).Cleanup(now));
        Assert.Equal(!expired, File.Exists(path));
    }
}
