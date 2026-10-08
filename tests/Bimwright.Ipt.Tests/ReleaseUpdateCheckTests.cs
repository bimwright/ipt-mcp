using System.Net;
using System.Text;
using Bimwright.Ipt.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ReleaseUpdateCheckTests
{
    internal sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string Body = """{"tag_name":"v0.2.2","draft":false,"prerelease":false}""";
        internal Func<CancellationToken, Task>? BeforeResponse;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/repos/bimwright/ipt-mcp/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            if (BeforeResponse is not null) await BeforeResponse(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("0.2.1", "v0.2.2", true)]
    [InlineData("0.2.9", "v0.2.10", true)]
    [InlineData("0.2.2", "v0.2.2", false)]
    [InlineData("0.3.0", "v0.2.2", false)]
    [InlineData("0.2.2-beta.1", "v0.2.2", true)]
    [InlineData("0.2.2+build", "v0.2.2", false)]
    public async Task Stable_release_comparison_uses_numeric_semver_and_never_recommends_a_downgrade(
        string current, string tag, bool expected)
    {
        using var client = new HttpClient(new Handler { Body = new JObject
        {
            ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = false,
            ["html_url"] = "https://untrusted.invalid/installer", ["body"] = "untrusted release instructions",
        }.ToString() });
        var result = await new ReleaseUpdateCheck(client).CheckAsync(current, CancellationToken.None);
        Assert.Equal("checked", result.Value<string>("status"));
        Assert.Equal("semantic_version", result.Value<string>("comparison_basis"));
        Assert.Equal(expected, result.Value<bool>("update_available"));
        Assert.Equal("https://github.com/bimwright/ipt-mcp/releases/tag/" + tag, result.Value<string>("release_url"));
        Assert.DoesNotContain("untrusted", result.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Found)]
    public async Task API_errors_and_redirects_report_unknown_instead_of_up_to_date(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler { Status = status });
        var result = await new ReleaseUpdateCheck(client).CheckAsync("0.2.1", CancellationToken.None);
        Assert.Equal("unknown", result.Value<string>("status"));
        Assert.Null(result["update_available"]!.Value<bool?>());
        Assert.Null(result["latest_version"]!.Value<string>());
        Assert.Equal("http_" + (int)status, result.Value<string>("reason"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"v0.2.2\",\"draft\":true,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v0.2.2-beta\",\"draft\":false,\"prerelease\":true}")]
    [InlineData("{\"tag_name\":\"../unexpected\",\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v01.2.3\",\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v0.2.2\",\"tag_name\":\"v0.2.3\",\"draft\":false,\"prerelease\":false}")]
    public async Task Invalid_metadata_does_not_produce_a_verified_release(string body)
    {
        using var client = new HttpClient(new Handler { Body = body });
        var result = await new ReleaseUpdateCheck(client).CheckAsync("0.2.1", CancellationToken.None);
        Assert.Equal("unknown", result.Value<string>("status"));
        Assert.Null(result["update_available"]!.Value<bool?>());
        Assert.Equal("invalid_release_metadata", result.Value<string>("reason"));
    }

    [Fact]
    public async Task Metadata_size_is_bounded()
    {
        using var client = new HttpClient(new Handler { Body = new string('x', ReleaseUpdateCheck.MaxResponseBytes + 1) });
        var result = await new ReleaseUpdateCheck(client).CheckAsync("0.2.1", CancellationToken.None);
        Assert.Equal("unknown", result.Value<string>("status"));
        Assert.Equal("invalid_release_metadata", result.Value<string>("reason"));
    }

    [Fact]
    public async Task Network_failure_does_not_turn_into_a_no_update_result()
    {
        using var client = new HttpClient(new Handler { BeforeResponse = _ => throw new HttpRequestException("offline") });
        var result = await new ReleaseUpdateCheck(client).CheckAsync("0.2.1", CancellationToken.None);
        Assert.Equal("unknown", result.Value<string>("status"));
        Assert.Equal("network_unavailable", result.Value<string>("reason"));
        Assert.Null(result["update_available"]!.Value<bool?>());
    }

    [Fact]
    public async Task Timeout_is_unknown_but_caller_cancellation_is_propagated()
    {
        using var client = new HttpClient(new Handler { BeforeResponse = ct => Task.Delay(Timeout.Infinite, ct) })
            { Timeout = TimeSpan.FromMilliseconds(30) };
        var checker = new ReleaseUpdateCheck(client);
        var result = await checker.CheckAsync("0.2.1", CancellationToken.None);
        Assert.Equal("timeout", result.Value<string>("reason"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.CheckAsync("0.2.1", cancelled.Token));
    }
}
