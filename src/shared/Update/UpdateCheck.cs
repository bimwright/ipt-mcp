using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Update;

/// <summary>Last update check, kept in %LOCALAPPDATA%\Bimwright\ipt-mcp\update-check.json (rvt-mcp parity).</summary>
public sealed class UpdateCheckState
{
    [JsonProperty("lastCheckedUtc")] public DateTime? LastCheckedUtc { get; set; }
    [JsonProperty("latestTag")] public string? LatestTag { get; set; }
    [JsonProperty("latestUrl")] public string? LatestUrl { get; set; }
    [JsonProperty("latestNotes")] public string? LatestNotes { get; set; }
    [JsonProperty("skippedVersion")] public string? SkippedVersion { get; set; }

    public static UpdateCheckState Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<UpdateCheckState>(File.ReadAllText(path, Encoding.UTF8)) ?? new UpdateCheckState()
                : new UpdateCheckState();
        }
        catch { return new UpdateCheckState(); }
    }

    public void Save(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch { }
    }
}

/// <summary>A newer stable release than the running add-in.</summary>
public sealed class UpdateInfo
{
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string? ReleaseNotes { get; set; }
    public bool IsSkipped { get; set; }
}

/// <summary>
/// Background check against GitHub's latest release, at most one request per 24 h (counted even when it fails);
/// every failure is silent. Only stable vX.Y.Z tags count, so a prerelease or draft never prompts.
/// </summary>
public static class UpdateChecker
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/bimwright/ipt-mcp/releases/latest";
    public const string ReleasesLatestUrl = "https://github.com/bimwright/ipt-mcp/releases/latest";
    public const string DisableEnvVar = "BIMWRIGHT_DISABLE_UPDATE_CHECK";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan InteractiveCheckInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public static bool IsDisabled(Func<string, string?> env)
    {
        var v = env(DisableEnvVar);
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stable X.Y.Z (optional leading v); prereleases and build suffixes other than +meta are rejected.</summary>
    public static Version? Stable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text!.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s.Substring(0, plus);
        if (s.IndexOf('-') >= 0) return null;
        var parts = s.Split('.');
        if (parts.Length != 3) return null;
        foreach (var p in parts)
        {
            if (p.Length == 0) return null;
            foreach (var c in p) if (c < '0' || c > '9') return null;
        }
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>Clean version of an assembly's InformationalVersion ("0.2.1+sha" → "0.2.1").</summary>
    public static string VersionOf(System.Reflection.Assembly assembly)
    {
        try
        {
            var info = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
                assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            return (info?.InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0").Split('+')[0];
        }
        catch { return "0.0.0"; }
    }

    /// <summary>(tag, release page, formattedNotes) of a published stable release; null otherwise.</summary>
    public static Tuple<string, string, string?>? ParseLatestRelease(string json)
    {
        try
        {
            var o = JObject.Parse(json);
            if (o.Value<bool?>("prerelease") == true || o.Value<bool?>("draft") == true) return null;
            var tag = o.Value<string>("tag_name");
            if (Stable(tag) == null) return null;
            var url = o.Value<string>("html_url");
            if (string.IsNullOrWhiteSpace(url) || !url!.StartsWith("https://github.com/bimwright/ipt-mcp/", StringComparison.Ordinal))
                url = ReleasesLatestUrl;
            var notes = FormatReleaseNotes(o.Value<string>("body"));
            return Tuple.Create(tag!, url!, notes);
        }
        catch { return null; }
    }

    public static string? FormatReleaseNotes(string? markdown, int maxChars = 1500)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var lines = markdown!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        var prevBlank = true;
        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("### Distribution", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("### Validation", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Source commit:", StringComparison.OrdinalIgnoreCase))
                break;

            if (trimmed.Length == 0)
            {
                if (!prevBlank && sb.Length > 0)
                {
                    sb.AppendLine();
                    prevBlank = true;
                }
                continue;
            }

            var line = StripMarkdownInline(trimmed);
            if (line.StartsWith("### ") || line.StartsWith("## ") || line.StartsWith("# "))
                line = line.TrimStart('#', ' ');
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                line = "• " + line.Substring(2).Trim();

            if (sb.Length > 0 && sb.Length + line.Length + 2 > maxChars)
            {
                var remaining = maxChars - sb.Length - 1;
                if (remaining > 20)
                    sb.Append(line.Substring(0, remaining).TrimEnd()).Append('…');
                else
                    sb.Append('…');
                break;
            }

            sb.AppendLine(line);
            prevBlank = false;
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string StripMarkdownInline(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 1)
                    {
                        sb.Append(text, i + 1, closeBracket - i - 1);
                        i = closeParen + 1;
                        continue;
                    }
                }
            }
            sb.Append(text[i++]);
        }
        return sb.ToString().Replace("**", string.Empty).Replace("`", string.Empty);
    }

    /// <summary>The notice from the saved state (no network); null when up to date, a dev or unknown build, or unknown.</summary>
    public static UpdateInfo? FromState(string currentVersion, UpdateCheckState? state)
    {
        var current = Stable(currentVersion);
        var latest = Stable(state?.LatestTag);
        if (current == null || latest == null || latest <= current) return null;
        return new UpdateInfo
        {
            CurrentVersion = "v" + current.ToString(3),
            LatestVersion = "v" + latest.ToString(3),
            ReleaseUrl = string.IsNullOrWhiteSpace(state!.LatestUrl) ? ReleasesLatestUrl : state.LatestUrl!,
            ReleaseNotes = string.IsNullOrWhiteSpace(state.LatestNotes) ? null : state.LatestNotes,
            IsSkipped = Stable(state.SkippedVersion) is { } skipped && skipped == latest,
        };
    }

    /// <summary>Refreshes the saved state when it is due and returns the notice. Never throws.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string currentVersion, string statePath,
        Func<CancellationToken, Task<string?>>? fetch = null, DateTime? nowUtc = null, TimeSpan? checkInterval = null)
    {
        try
        {
            var now = nowUtc ?? DateTime.UtcNow;
            var interval = checkInterval ?? CheckInterval;
            var state = UpdateCheckState.Load(statePath);
            var due = state.LastCheckedUtc == null
                || now - state.LastCheckedUtc.Value >= interval
                || state.LastCheckedUtc.Value > now.AddMinutes(5);   // clock moved backwards
            if (due)
            {
                state.LastCheckedUtc = now;
                state.Save(statePath);
                string? json;
                using (var cts = new CancellationTokenSource(RequestTimeout))
                {
                    try { json = await (fetch ?? FetchLatestAsync)(cts.Token).ConfigureAwait(false); }
                    catch { json = null; }
                }
                if (json != null && ParseLatestRelease(json) is { } parsed)
                {
                    state.LatestTag = parsed.Item1;
                    state.LatestUrl = parsed.Item2;
                    state.LatestNotes = parsed.Item3;
                    state.Save(statePath);
                }
            }
            return FromState(currentVersion, state);
        }
        catch { return null; }
    }

    public static void SkipVersion(string latestVersion, string statePath)
    {
        var state = UpdateCheckState.Load(statePath);
        state.SkippedVersion = latestVersion;
        state.Save(statePath);
    }

    private static async Task<string?> FetchLatestAsync(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = RequestTimeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd("ipt-mcp-update-check");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, token).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
    }
}

/// <summary>The prompt is English on purpose: it is pasted into an AI agent, which answers in the user's language.</summary>
public static class UpdateNoticeText
{
    public static string BuildAgentPrompt(UpdateInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Please help me update ipt-mcp (the Inventor MCP server and add-in by bimwright).");
        sb.AppendLine("Installed version: " + info.CurrentVersion);
        sb.AppendLine("Latest version: " + info.LatestVersion);
        sb.AppendLine("Release page: " + info.ReleaseUrl);
        sb.AppendLine();
        sb.AppendLine("Steps:");
        sb.AppendLine("1. Open the release page and read the release notes and install instructions.");
        sb.AppendLine("2. Download the IptMcp.Setup-" + info.LatestVersion + "-win-x64.zip asset and verify it against its .sha256 file.");
        sb.AppendLine("3. Tell me to save my work and close every running Inventor before installing, then guide me through the installer step by step.");
        sb.AppendLine("4. After installing, help me restart Inventor and my MCP client, and confirm inventor_health reports " + info.LatestVersion + " for the server and the add-in.");
        sb.Append("Ask me before running any command on my computer.");
        return sb.ToString();
    }
}
