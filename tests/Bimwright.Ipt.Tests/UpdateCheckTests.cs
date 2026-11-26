using Bimwright.Ipt.Shared.Update;

namespace Bimwright.Ipt.Tests;

/// <summary>Add-in update notice (rvt-mcp parity): daily, silent on failure, stable releases only, skippable.</summary>
public sealed class UpdateCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-update-" + Guid.NewGuid().ToString("N"));
    private string State => Path.Combine(_dir, "update-check.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string Release(string tag, bool prerelease = false, string url = "https://github.com/bimwright/ipt-mcp/releases/tag/", string? body = null) =>
        $"{{\"tag_name\":\"{tag}\",\"prerelease\":{prerelease.ToString().ToLowerInvariant()},\"draft\":false,\"html_url\":\"{url}{tag}\"" +
        (body == null ? "" : $",\"body\":{Newtonsoft.Json.JsonConvert.SerializeObject(body)}") + "}";

    [Theory]
    [InlineData("0.2.1", "0.2.1")]
    [InlineData("v1.10.0", "1.10.0")]
    [InlineData("0.2.1+a175ea5", "0.2.1")]
    [InlineData("0.3.0-rc.1", null)]
    [InlineData("0.3", null)]
    [InlineData("x.1.2", null)]
    [InlineData("", null)]
    public void Only_stable_versions_parse(string text, string? expected)
        => Assert.Equal(expected, UpdateChecker.Stable(text)?.ToString(3));

    [Fact]
    public async Task A_newer_release_gives_a_notice_with_the_agent_prompt()
    {
        var info = await UpdateChecker.CheckAsync("0.2.1", State, _ => Task.FromResult<string?>(Release("v0.2.2")));
        Assert.NotNull(info);
        Assert.Equal("v0.2.1", info!.CurrentVersion);
        Assert.Equal("v0.2.2", info.LatestVersion);
        Assert.Equal("https://github.com/bimwright/ipt-mcp/releases/tag/v0.2.2", info.ReleaseUrl);
        var prompt = UpdateNoticeText.BuildAgentPrompt(info);
        Assert.Contains("IptMcp.Setup-v0.2.2-win-x64.zip", prompt);
        Assert.Contains("Ask me before running any command", prompt);
    }

    [Theory]
    [InlineData("v0.2.1", false)]   // same version
    [InlineData("v0.2.0", false)]   // older
    [InlineData("v0.3.0-rc.1", false)]
    [InlineData("v0.3.0", true)]
    public async Task Only_a_newer_stable_release_prompts(string tag, bool notice)
        => Assert.Equal(notice, await UpdateChecker.CheckAsync("0.2.1", State, _ => Task.FromResult<string?>(Release(tag))) != null);

    [Fact]
    public async Task A_prerelease_flag_or_foreign_url_is_handled()
    {
        Assert.Null(await UpdateChecker.CheckAsync("0.2.1", State, _ => Task.FromResult<string?>(Release("v0.3.0", prerelease: true))));
        var info = await UpdateChecker.CheckAsync("0.2.1", Path.Combine(_dir, "b.json"),
            _ => Task.FromResult<string?>(Release("v0.3.0", url: "https://example.com/")));
        Assert.Equal(UpdateChecker.ReleasesLatestUrl, info!.ReleaseUrl);
    }

    [Fact]
    public async Task At_most_one_request_a_day_even_when_it_fails()
    {
        var calls = 0;
        var now = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
        Task<string?> Failing(CancellationToken _) { calls++; throw new HttpRequestException("offline"); }
        Assert.Null(await UpdateChecker.CheckAsync("0.2.1", State, Failing, now));
        Assert.Null(await UpdateChecker.CheckAsync("0.2.1", State, Failing, now.AddHours(23)));
        Assert.Equal(1, calls);
        await UpdateChecker.CheckAsync("0.2.1", State, Failing, now.AddHours(25));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_skipped_version_is_remembered_until_a_newer_one()
    {
        var now = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
        var info = await UpdateChecker.CheckAsync("0.2.1", State, _ => Task.FromResult<string?>(Release("v0.2.2")), now);
        UpdateChecker.SkipVersion(info!.LatestVersion, State);
        Assert.True(UpdateChecker.FromState("0.2.1", UpdateCheckState.Load(State))!.IsSkipped);
        var newer = await UpdateChecker.CheckAsync("0.2.1", State, _ => Task.FromResult<string?>(Release("v0.2.3")), now.AddDays(2));
        Assert.False(newer!.IsSkipped);
    }

    [Fact]
    public void The_environment_switch_disables_the_check()
    {
        Assert.True(UpdateChecker.IsDisabled(n => n == UpdateChecker.DisableEnvVar ? "1" : null));
        Assert.True(UpdateChecker.IsDisabled(n => n == UpdateChecker.DisableEnvVar ? "TRUE" : null));
        Assert.False(UpdateChecker.IsDisabled(_ => null));
    }

    [Fact]
    public async Task Release_notes_are_formatted_cached_and_trimmed_before_distribution()
    {
        var body = "Summary of **updates**.\n\n### Fixed\n- Fix [#16](https://github.com/bimwright/ipt-mcp/pull/16): `CLIENT_SANDBOXED`.\n\n### Distribution\nSetup ZIP details.\n\nSource commit: abc123.";
        var now = new DateTime(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc);
        var info = await UpdateChecker.CheckAsync("2.5.1", State, _ => Task.FromResult<string?>(Release("v2.5.2", body: body)), now);

        Assert.NotNull(info);
        Assert.Contains("Summary of updates.", info!.ReleaseNotes);
        Assert.Contains("• Fix #16: CLIENT_SANDBOXED.", info.ReleaseNotes);
        Assert.DoesNotContain("Distribution", info.ReleaseNotes);
        Assert.DoesNotContain("Source commit", info.ReleaseNotes);

        var cached = UpdateChecker.FromState("2.5.1", UpdateCheckState.Load(State));
        Assert.Equal(info.ReleaseNotes, cached!.ReleaseNotes);
    }

    [Fact]
    public async Task Interactive_check_interval_refreshes_sooner_than_daily_interval()
    {
        var calls = 0;
        var latest = "v2.5.1";
        Task<string?> Fetch(CancellationToken _) { calls++; return Task.FromResult<string?>(Release(latest)); }
        var now = new DateTime(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc);

        Assert.Null(await UpdateChecker.CheckAsync("2.5.1", State, Fetch, now));
        Assert.Equal(1, calls);

        latest = "v2.5.2";
        Assert.Null(await UpdateChecker.CheckAsync("2.5.1", State, Fetch, now.AddMinutes(10), UpdateChecker.InteractiveCheckInterval));
        Assert.Equal(1, calls);

        var info = await UpdateChecker.CheckAsync("2.5.1", State, Fetch, now.AddMinutes(31), UpdateChecker.InteractiveCheckInterval);
        Assert.Equal(2, calls);
        Assert.NotNull(info);
        Assert.Equal("v2.5.2", info!.LatestVersion);
    }
}
