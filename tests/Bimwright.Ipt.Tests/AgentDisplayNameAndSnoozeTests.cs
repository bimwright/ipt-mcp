using System.Globalization;
using System.IO;
using System.Text;
using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

[Collection("ToastSnoozeState")]
public sealed class AgentDisplayNameAndSnoozeTests : IDisposable
{
    private readonly Func<DateTime> _savedNow = ToastSnooze.Now;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ipt-snooze-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        ToastSnooze.Now = _savedNow;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // FC-TST-03
    [Theory]
    [InlineData("claude-code", "Claude")]
    [InlineData("CLAUDE-CODE", "Claude")]
    [InlineData("claude-ai", "Claude")]
    [InlineData("claude-desktop", "Claude")]
    [InlineData("codex-mcp-client", "Codex")]
    [InlineData("Visual Studio Code", "VS Code")]
    [InlineData("cursor-vscode", "Cursor")]
    [InlineData("windsurf", "Windsurf")]
    [InlineData("antigravity", "Antigravity")]
    [InlineData("vscode-insiders", "VS Code")]
    [InlineData("opencode", "opencode")]
    [InlineData("cline", "Cline")]
    [InlineData("gemini-cli", "Gemini")]
    [InlineData("copilot-cli", "Copilot")]
    [InlineData("my-custom-client", "my-custom-client")]
    [InlineData("  spaced\t\nname  ", "spaced name")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("\u202E", null)]
    public void AgentDisplayName_maps_known_clients_and_normalizes_unknowns(string? input, string? expected)
        => Assert.Equal(expected, AgentDisplayName.For(input));

    // FC-TST-03
    [Fact]
    public void AgentDisplayName_caps_length_and_strips_bidi_format_characters()
    {
        var longName = new string('a', 35);
        var mapped = AgentDisplayName.For(longName);
        Assert.Equal(AgentDisplayName.MaxLength, mapped!.Length);
        Assert.EndsWith("…", mapped);
        Assert.Equal("Claude", AgentDisplayName.For("claude\u202E\u200B"));
        Assert.Equal("my-agent", AgentDisplayName.For("my\u202E\u200B-agent"));

        var trickyEmoji = "agent-" + new string('x', 13) + "😀";
        var safe = AgentDisplayName.For(trickyEmoji)!;
        Assert.True(safe.Length <= AgentDisplayName.MaxLength);
        Assert.DoesNotContain(safe, c => char.IsHighSurrogate(c) || char.IsLowSurrogate(c)
            ? false : CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format);
        // Must remain valid UTF-16 without a lone surrogate at the cut boundary.
        _ = Encoding.UTF8.GetBytes(safe);
    }

    // FC-TST-04
    [Fact]
    public void ToastSnooze_pause_and_resume_persist_today_and_expire_at_midnight()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "iptmcp.config.json");
        ToastConfigStore.SaveEnableToast(path, true);

        ToastSnooze.Now = () => new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Local);
        var events = 0;
        Action onChanged = () => events++;
        ToastSnooze.Changed += onChanged;
        try
        {
            Assert.False(ToastSnooze.IsActive(path));

            Assert.True(ToastSnooze.PauseForToday(path));
            Assert.True(ToastSnooze.IsActive(path));
            Assert.Equal("2026-11-21", ToastConfigStore.LoadToastSnoozedDate(path));
            Assert.Equal(1, events);

            // Next local calendar day clears active state automatically even while file keeps old date.
            ToastSnooze.Now = () => new DateTime(2026, 11, 22, 0, 0, 1, DateTimeKind.Local);
            Assert.False(ToastSnooze.IsActive(path));

            ToastSnooze.Now = () => new DateTime(2026, 11, 21, 23, 59, 59, DateTimeKind.Local);
            Assert.True(ToastSnooze.IsActive(path));
            Assert.True(ToastSnooze.ResumeIfPaused(path));
            Assert.False(ToastSnooze.IsActive(path));
            Assert.Null(ToastConfigStore.LoadToastSnoozedDate(path));
            Assert.Equal(2, events);

            // Turning off when already active=false does not touch a valid config or raise spurious change.
            Assert.True(ToastSnooze.ResumeIfPaused(path));
            Assert.Equal(2, events);

            // Unrelated keys such as enableToast stay intact.
            var json = JObject.Parse(File.ReadAllText(path));
            Assert.True(json.Value<bool>("enableToast"));
            Assert.False(json.ContainsKey("toastSnoozedDate"));
        }
        finally
        {
            ToastSnooze.Changed -= onChanged;
        }
    }

    // FC-TST-04
    [Fact]
    public void ToastSnooze_refuses_to_overwrite_malformed_config()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "iptmcp.config.json");
        const string broken = "{ not-json";
        File.WriteAllText(path, broken);

        ToastSnooze.Now = () => new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Local);
        Assert.False(ToastSnooze.PauseForToday(path));
        Assert.False(ToastSnooze.IsActive(path));
        Assert.Equal(broken, File.ReadAllText(path));
    }
}
