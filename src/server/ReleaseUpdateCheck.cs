using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

internal sealed class ReleaseUpdateCheck
{
    internal const int MaxResponseBytes = 256 * 1024;
    private const string Endpoint = "https://api.github.com/repos/bimwright/ipt-mcp/releases/latest";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly HttpClient _client;

    internal ReleaseUpdateCheck() : this(SharedClient) { }
    internal ReleaseUpdateCheck(HttpClient client) => _client = client;

    internal static JObject NotChecked() => new()
    {
        ["status"] = "not_checked", ["comparison_basis"] = "semantic_version", ["latest_version"] = null,
        ["update_available"] = null, ["release_url"] = null,
    };

    internal async Task<JObject> CheckAsync(string currentVersion, CancellationToken ct)
    {
        var result = NotChecked();
        result["status"] = "unknown";
        result["checked_at_utc"] = DateTimeOffset.UtcNow.ToString("o");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.UserAgent.ParseAdd("ipt-mcp/" + Program.ServerVersion);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                result["reason"] = "http_" + (int)response.StatusCode;
                return result;
            }
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                throw new InvalidDataException("Release metadata exceeds the response limit.");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + count > MaxResponseBytes)
                    throw new InvalidDataException("Release metadata exceeds the response limit.");
                bytes.Write(buffer, 0, count);
            }
            using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes.ToArray())))
                { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            var release = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read() || release["draft"]?.Type != JTokenType.Boolean || release["prerelease"]?.Type != JTokenType.Boolean
                || release.Value<bool>("draft") || release.Value<bool>("prerelease")
                || release["tag_name"]?.Type != JTokenType.String)
                throw new InvalidDataException("The API did not return a published stable release.");
            var tag = release.Value<string>("tag_name")!;
            var latest = tag.StartsWith('v') ? tag.Substring(1) : tag;
            var currentCore = currentVersion.Split('+')[0].Split('-')[0];
            if (!TryVersion(latest, out var latestVersion) || !TryVersion(currentCore, out var installedVersion))
                throw new InvalidDataException("Release or installed version is not a supported semantic version.");
            result["status"] = "checked";
            result["latest_version"] = latest;
            result["update_available"] = latestVersion > installedVersion
                || latestVersion == installedVersion && currentVersion.Contains('-');
            // Construct our own link; never forward release notes or arbitrary API-provided URLs to the agent.
            result["release_url"] = "https://github.com/bimwright/ipt-mcp/releases/tag/" + tag;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result["reason"] = "timeout"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException)
        { result["reason"] = ex is HttpRequestException ? "network_unavailable" : "invalid_release_metadata"; }
        return result;
    }

    private static bool TryVersion(string value, out Version? version)
    {
        version = null;
        return Regex.IsMatch(value, "\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z")
            && Version.TryParse(value, out version);
    }
}
