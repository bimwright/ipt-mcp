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
        internal string Revision = new('a', 40);
        internal string? RefBody;
        internal string? TagBody;
        internal HttpStatusCode RefStatus = HttpStatusCode.OK;
        internal Func<CancellationToken, Task>? BeforeResponse;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            if (BeforeResponse is not null) await BeforeResponse(ct);
            var path = request.RequestUri.AbsolutePath;
            string body;
            var status = RefStatus;
            if (path == "/repos/bimwright/ipt-mcp/releases/latest") { body = Body; status = Status; }
            else if (path.StartsWith("/repos/bimwright/ipt-mcp/git/ref/tags/", StringComparison.Ordinal))
            {
                var tag = path.Split('/').Last();
                body = RefBody ?? new JObject { ["ref"] = "refs/tags/" + tag,
                    ["object"] = new JObject { ["type"] = "commit", ["sha"] = Revision } }.ToString();
            }
            else
            {
                Assert.Equal("/repos/bimwright/ipt-mcp/git/tags/" + new string('b', 40), path);
                body = TagBody!;
            }
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("0.2.1", "v0.2.2", true, "NEW_RELEASE_AVAILABLE")]
    [InlineData("0.2.9", "v0.2.10", true, "NEW_RELEASE_AVAILABLE")]
    [InlineData("0.2.2", "v0.2.2", false, "RELEASE_IDENTITY_UNVERIFIED")]
    [InlineData("0.3.0", "v0.2.2", false, "LOCAL_VERSION_AHEAD")]
    [InlineData("0.2.2-beta.1", "v0.2.2", true, "NEW_RELEASE_AVAILABLE")]
    [InlineData("0.2.2+build", "v0.2.2", false, "RELEASE_IDENTITY_UNVERIFIED")]
    public async Task Stable_release_comparison_uses_numeric_semver_and_never_recommends_a_downgrade(
        string current, string tag, bool expected, string? notificationCode)
    {
        using var client = new HttpClient(new Handler { Body = new JObject
        {
            ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = false,
            ["html_url"] = "https://untrusted.invalid/installer", ["body"] = "untrusted release instructions",
        }.ToString() });
        var result = await Check(client, current);
        Assert.Equal("checked", result.Value<string>("status"));
        Assert.Equal("semantic_version_and_release_source_revision", result.Value<string>("comparison_basis"));
        Assert.Equal(expected, result["components"]!["server"]!.Value<bool>("update_available"));
        Assert.Equal(expected, result["components"]!["plugin"]!.Value<bool>("update_available"));
        if (!expected) Assert.Null(result.Value<bool?>("update_available"));
        Assert.Equal("https://github.com/bimwright/ipt-mcp/releases/tag/" + tag, result.Value<string>("release_url"));
        Assert.DoesNotContain("untrusted", result.ToString());
        Assert.Equal(notificationCode, (result["notification"] as JObject)?.Value<string>("code"));
        if (expected)
        {
            Assert.Equal("NEW_RELEASE_AVAILABLE", (string?)result["notification"]?["code"]);
            Assert.Equal(new[] { "server", "plugin" }, result["notification"]!["components"]!.Values<string>());
        }
        else if (notificationCode is null) Assert.Equal(JTokenType.Null, result["notification"]!.Type);
        else Assert.False(result["notification"]!.Value<bool>("bundle_update_proposal_allowed"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Found)]
    public async Task API_errors_and_redirects_report_unknown_instead_of_up_to_date(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler { Status = status });
        var result = await Check(client);
        Assert.Equal("unknown", result.Value<string>("status"));
        Assert.Null(result["update_available"]!.Value<bool?>());
        Assert.Null(result["latest_version"]!.Value<string>());
        Assert.Equal("http_" + (int)status, result.Value<string>("reason"));
        Assert.Equal("UPDATE_CHECK_UNAVAILABLE", (string?)result["notification"]?["code"]);
        Assert.False(result["notification"]!.Value<bool>("bundle_update_proposal_allowed"));
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
        var result = await Check(client);
        Assert.Equal("unknown", result.Value<string>("status"));
        Assert.Null(result["update_available"]!.Value<bool?>());
        Assert.Equal("invalid_release_metadata", result.Value<string>("reason"));
    }

    [Fact]
    public async Task Metadata_size_is_bounded()
    {
        using var client = new HttpClient(new Handler { Body = new string('x', ReleaseUpdateCheck.MaxResponseBytes + 1) });
        var result = await Check(client);
        Assert.Equal("unknown", result.Value<string>("status"));
        Assert.Equal("invalid_release_metadata", result.Value<string>("reason"));
    }

    [Fact]
    public async Task Network_failure_does_not_turn_into_a_no_update_result()
    {
        using var client = new HttpClient(new Handler { BeforeResponse = _ => throw new HttpRequestException("offline") });
        var result = await Check(client);
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
        var result = await checker.CheckAsync(Identity("0.2.1"), null, CancellationToken.None);
        Assert.Equal("timeout", result.Value<string>("reason"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.CheckAsync(Identity("0.2.1"), null, cancelled.Token));
    }

    internal static JObject Identity(string version) => new() { ["assembly_informational_version"] = version };
    private static Task<JObject> Check(HttpClient client, string current = "0.2.1")
        => new ReleaseUpdateCheck(client).CheckAsync(Identity(current), Identity(current), CancellationToken.None);

    [Theory]
    [InlineData("0.2.1", "0.2.2+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "server")]
    [InlineData("0.2.2+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "0.2.1", "plugin")]
    public async Task Notification_identifies_the_component_with_a_newer_release_and_requires_authorized_update(
        string server, string plugin, string affected)
    {
        using var client = new HttpClient(new Handler());
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity(server), Identity(plugin), CancellationToken.None);
        var notification = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notification.Value<string>("code"));
        Assert.Equal(new[] { affected }, notification["components"]!.Values<string>());
        Assert.Equal("notify_user_and_propose_update", notification.Value<string>("agent_action"));
        Assert.True(notification.Value<bool>("requires_user_authorization"));
        Assert.True(notification.Value<bool>("bundle_update_proposal_allowed"));
        Assert.Empty((JArray)notification["protected_components"]!);
        Assert.Equal("ipt-mcp 0.2.2 is available for: " + affected + ".", notification.Value<string>("message"));
    }

    [Fact]
    public async Task Known_newer_release_can_notify_without_a_tag_revision_or_plugin_identity()
    {
        using var client = new HttpClient(new Handler { RefStatus = HttpStatusCode.Forbidden });
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity("0.2.1"), null, CancellationToken.None);
        Assert.Equal("http_403", result.Value<string>("build_reason"));
        Assert.Equal("unknown", (string?)result["components"]?["server"]?["build_status"]);
        Assert.Equal(new[] { "server" }, result["notification"]!["components"]!.Values<string>());
        Assert.Equal("unknown", (string?)result["components"]?["plugin"]?["status"]);
        Assert.False(result["notification"]!.Value<bool>("bundle_update_proposal_allowed"));
        Assert.Empty((JArray)result["notification"]!["protected_components"]!);
        Assert.Equal(new[] { "plugin" }, result["notification"]!["unobserved_components"]!.Values<string>());
    }

    [Fact]
    public async Task Matching_release_build_does_not_raise_a_new_version_notification()
    {
        using var client = new HttpClient(new Handler());
        var result = await Check(client, "0.2.2+" + new string('a', 40));
        Assert.False(result.Value<bool>("update_available"));
        Assert.Equal(JTokenType.Null, result["notification"]!.Type);
    }

    [Theory]
    [InlineData("0.3.0", "0.2.1", "server", "plugin")]
    [InlineData("0.2.1", "0.3.0", "plugin", "server")]
    public async Task Ahead_component_blocks_the_bundle_update_proposal_and_is_named_as_protected(
        string server, string plugin, string protectedComponent, string olderComponent)
    {
        using var client = new HttpClient(new Handler());
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity(server), Identity(plugin), CancellationToken.None);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notice.Value<string>("code"));
        Assert.Equal(new[] { olderComponent }, notice["components"]!.Values<string>());
        Assert.Equal(false, notice.Value<bool?>("bundle_update_proposal_allowed"));
        Assert.Equal(new[] { protectedComponent }, notice["protected_components"]!.Values<string>());
        Assert.Equal("notify_user_and_review_update_state", notice.Value<string>("agent_action"));
        Assert.Contains(protectedComponent, notice.Value<string>("message"));
        Assert.True(result.Value<bool>("update_available")); // Version discovery is distinct from a safe bundle proposal.
    }

    [Theory]
    [InlineData("0.2.2+bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "RELEASE_REVIEW_REQUIRED")]
    [InlineData("0.2.2+development", "RELEASE_IDENTITY_UNVERIFIED")]
    public async Task Same_version_unverified_build_notifies_for_review_without_claiming_a_new_version(string server, string code)
    {
        using var client = new HttpClient(new Handler());
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity(server), Identity("0.2.2+" + new string('a', 40)), CancellationToken.None);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal(code, notice.Value<string>("code"));
        Assert.Equal(new[] { "server" }, notice["components"]!.Values<string>());
        Assert.Equal(new[] { "server" }, notice["protected_components"]!.Values<string>());
        Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
        Assert.Equal("notify_user_and_review_update_state", notice.Value<string>("agent_action"));
        Assert.False(result["components"]!["server"]!.Value<bool>("update_available"));
        Assert.Null(result.Value<bool?>("update_available"));
    }

    [Fact]
    public async Task Older_component_with_an_unverified_peer_reports_the_version_but_requires_review()
    {
        using var client = new HttpClient(new Handler());
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity("0.2.2+development"), Identity("0.2.1"), CancellationToken.None);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notice.Value<string>("code"));
        Assert.Equal(new[] { "plugin" }, notice["components"]!.Values<string>());
        Assert.Equal(new[] { "server" }, notice["protected_components"]!.Values<string>());
        Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
        Assert.Equal("notify_user_and_review_update_state", notice.Value<string>("agent_action"));
    }

    [Fact]
    public async Task Failed_release_lookup_notifies_as_unavailable_and_never_proposes_an_installer()
    {
        using var client = new HttpClient(new Handler { Status = HttpStatusCode.Forbidden });
        var result = await Check(client);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("UPDATE_CHECK_UNAVAILABLE", notice.Value<string>("code"));
        Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
        Assert.Empty((JArray)notice["protected_components"]!);
        Assert.False(notice.Value<bool>("requires_user_authorization"));
    }

    [Theory]
    [InlineData("http", "http_403")]
    [InlineData("network", "network_unavailable")]
    [InlineData("timeout", "timeout")]
    [InlineData("metadata", "invalid_release_metadata")]
    public async Task Lookup_failure_message_reports_the_external_reason_without_a_runtime_fault_or_install_authorization(
        string failure, string reason)
    {
        var handler = new Handler();
        if (failure == "http") handler.Status = HttpStatusCode.Forbidden;
        if (failure == "network") handler.BeforeResponse = _ => throw new HttpRequestException("offline");
        if (failure == "timeout") handler.BeforeResponse = ct => Task.Delay(Timeout.Infinite, ct);
        if (failure == "metadata") handler.Body = "not json";
        // Only the timeout case needs a short client timeout; the others fail immediately and a 40 ms
        // limit made them race the timeout on slow CI runners (seen as "timeout" instead of "network_unavailable").
        using var client = new HttpClient(handler)
        { Timeout = failure == "timeout" ? TimeSpan.FromMilliseconds(40) : TimeSpan.FromSeconds(30) };
        var result = await Check(client);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("UPDATE_CHECK_UNAVAILABLE", notice.Value<string>("code"));
        Assert.Equal(reason, notice.Value<string>("lookup_reason"));
        Assert.Contains(reason, notice.Value<string>("message"));
        Assert.DoesNotContain("mismatched", notice.Value<string>("message")!, StringComparison.OrdinalIgnoreCase);
        Assert.False(notice.Value<bool>("requires_user_authorization"));
        Assert.Empty((JArray)notice["protected_components"]!);
    }

    [Fact]
    public async Task Local_ahead_without_an_old_peer_returns_an_informational_notice_and_protection_reason()
    {
        using var client = new HttpClient(new Handler());
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity("0.3.0"), Identity("0.2.2+" + new string('a', 40)), CancellationToken.None);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("LOCAL_VERSION_AHEAD", notice.Value<string>("code"));
        Assert.Equal("ahead_of_release", (string?)notice["protected_component_reasons"]?["server"]);
        Assert.Contains("0.3.0", notice.Value<string>("message"));
        Assert.Contains("0.2.2", notice.Value<string>("message"));
        Assert.False(notice.Value<bool>("requires_user_authorization"));
        Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
    }

    [Fact]
    public async Task Unobserved_plugin_is_distinguished_from_an_observed_plugin_with_missing_revision()
    {
        using var client = new HttpClient(new Handler());
        var checker = new ReleaseUpdateCheck(client);
        var server = Identity("0.2.2+" + new string('a', 40));
        var unobserved = await checker.CheckAsync(server, null, CancellationToken.None);
        Assert.False(unobserved["components"]!["plugin"]!.Value<bool>("identity_observed"));
        Assert.Equal(new[] { "plugin" }, unobserved["notification"]!["unobserved_components"]!.Values<string>());
        Assert.Empty((JArray)unobserved["notification"]!["protected_components"]!);
        Assert.Contains("not observed", unobserved["notification"]!.Value<string>("message"));
        var observed = await checker.CheckAsync(server, Identity("0.2.2+development"), CancellationToken.None);
        Assert.True(observed["components"]!["plugin"]!.Value<bool>("identity_observed"));
        Assert.Empty((JArray)observed["notification"]!["unobserved_components"]!);
        Assert.Equal(new[] { "plugin" }, observed["notification"]!["protected_components"]!.Values<string>());
        Assert.Equal("runtime_revision_unavailable", (string?)observed["notification"]?["protected_component_reasons"]?["plugin"]);
    }

    [Fact]
    public async Task Ahead_server_with_an_unobserved_plugin_keeps_both_facts_in_review_guidance()
    {
        using var client = new HttpClient(new Handler());
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity("0.3.0"), null, CancellationToken.None);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("RELEASE_IDENTITY_UNVERIFIED", notice.Value<string>("code"));
        Assert.Equal(new[] { "plugin" }, notice["components"]!.Values<string>());
        Assert.Equal(new[] { "server" }, notice["protected_components"]!.Values<string>());
        Assert.Equal("ahead_of_release", (string?)notice["protected_component_reasons"]?["server"]);
        Assert.Equal(new[] { "plugin" }, notice["unobserved_components"]!.Values<string>());
        Assert.Contains("server version 0.3.0", notice.Value<string>("message"));
        Assert.Contains("plugin runtime identity was not observed", notice.Value<string>("message"));
        Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
        var guidance = HealthRuntimeInfo.UpdateGuidance(result);
        Assert.Equal("review", guidance.Value<string>("mode"));
        Assert.False(guidance.Value<bool>("requires_user_authorization"));
        Assert.Contains("primary affected set", guidance["steps"]!.ToString());
        Assert.DoesNotContain("install.ps1", guidance["steps"]!.ToString());
    }

    [Fact]
    public async Task Same_version_tag_lookup_failure_blocks_installation_without_protecting_or_faulting_local_builds()
    {
        using var client = new HttpClient(new Handler { RefStatus = HttpStatusCode.Forbidden });
        var result = await Check(client, "0.2.2+" + new string('a', 40));
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("checked", result.Value<string>("status"));
        Assert.Equal("RELEASE_IDENTITY_UNVERIFIED", notice.Value<string>("code"));
        Assert.Equal("http_403", notice.Value<string>("lookup_reason"));
        foreach (var name in new[] { "server", "plugin" })
        {
            Assert.Equal("match", (string?)result["components"]?[name]?["version_status"]);
            Assert.Equal("release_revision_unavailable", (string?)result["components"]?[name]?["comparison_reason"]);
        }
        Assert.Empty((JArray)notice["protected_components"]!);
        Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
        var guidance = HealthRuntimeInfo.UpdateGuidance(result);
        Assert.Equal("review", guidance.Value<string>("mode"));
        Assert.False(guidance.Value<bool>("requires_user_authorization"));
        Assert.Contains("http_403", guidance["steps"]!.ToString());
        Assert.Contains("external lookup_reason", guidance["steps"]!.ToString());
        Assert.Contains("otherwise healthy Inventor work can continue", guidance["steps"]!.ToString());
        Assert.DoesNotContain("install.ps1", guidance["steps"]!.ToString());
    }

    [Fact]
    public async Task Failed_tag_resolution_preserves_known_version_facts_but_returns_no_install_proposal()
    {
        using var client = new HttpClient(new Handler { RefStatus = HttpStatusCode.Forbidden });
        var result = await Check(client);
        var notice = Assert.IsType<JObject>(result["notification"]);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notice.Value<string>("code"));
        Assert.Equal("http_403", notice.Value<string>("lookup_reason"));
        Assert.Contains("http_403", notice.Value<string>("message"));
        Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
        Assert.False(notice.Value<bool>("requires_user_authorization"));
        Assert.Empty((JArray)notice["protected_components"]!);
    }

    [Theory]
    [InlineData("0.2.1+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "update_available", "older")]
    [InlineData("0.2.2+bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "release_mismatch", "match")]
    [InlineData("0.2.2+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "release_match", "match")]
    [InlineData("0.2.2+development", "unknown", "match")]
    public async Task Current_server_does_not_hide_older_or_different_plugin(string plugin, string status, string versionStatus)
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity("0.2.2+" + handler.Revision), Identity(plugin), CancellationToken.None);
        Assert.Equal(2, handler.Calls); // One release baseline and one shared tag resolution, not a query per component.
        Assert.Equal("release_match", (string?)result["components"]?["server"]?["status"]);
        Assert.Equal(status, (string?)result["components"]?["plugin"]?["status"]);
        Assert.Equal(versionStatus, (string?)result["components"]?["plugin"]?["version_status"]);
        Assert.Equal("not_performed", result.Value<string>("artifact_verification"));
        if (status == "update_available") Assert.True(result.Value<bool>("update_available"));
        else if (status == "release_match") Assert.False(result.Value<bool>("update_available"));
        else Assert.Null(result.Value<bool?>("update_available"));
    }

    [Fact]
    public async Task Missing_plugin_and_unresolvable_tag_keep_partial_evidence_unknown()
    {
        var handler = new Handler { RefStatus = HttpStatusCode.Forbidden };
        using var client = new HttpClient(handler);
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity("0.2.2+" + handler.Revision), null, CancellationToken.None);
        Assert.Equal("checked", result.Value<string>("status"));
        Assert.Equal("http_403", result.Value<string>("build_reason"));
        Assert.Equal("match", (string?)result["components"]?["server"]?["version_status"]);
        Assert.Equal("unknown", (string?)result["components"]?["server"]?["build_status"]);
        Assert.Equal("unknown", (string?)result["components"]?["plugin"]?["status"]);
        Assert.Null(result.Value<bool?>("update_available"));
    }

    [Fact]
    public async Task Missing_plugin_cannot_inherit_the_servers_release_match()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var result = await new ReleaseUpdateCheck(client).CheckAsync(Identity("0.2.2+" + handler.Revision), null, CancellationToken.None);
        Assert.Equal("release_match", (string?)result["components"]?["server"]?["status"]);
        Assert.Equal("unknown", (string?)result["components"]?["plugin"]?["status"]);
        Assert.Null(result.Value<bool?>("update_available"));
    }

    [Fact]
    public async Task Timeout_resolving_the_build_retains_version_evidence_without_claiming_a_build_match()
    {
        var handler = new Handler();
        handler.BeforeResponse = ct => handler.Calls > 1 ? Task.Delay(Timeout.Infinite, ct) : Task.CompletedTask;
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(40) };
        var result = await Check(client, "0.2.2+" + handler.Revision);
        Assert.Equal("checked", result.Value<string>("status"));
        Assert.Equal("timeout", result.Value<string>("build_reason"));
        Assert.Equal("match", (string?)result["components"]?["server"]?["version_status"]);
        Assert.Equal("unknown", (string?)result["components"]?["server"]?["build_status"]);
    }

    [Fact]
    public async Task Annotated_tag_is_peeled_without_following_API_provided_URLs()
    {
        var handler = new Handler {
            RefBody = """{"ref":"refs/tags/v0.2.2","object":{"type":"tag","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","url":"https://untrusted.invalid"}}""",
            TagBody = """{"tag":"v0.2.2","object":{"type":"commit","sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}""",
        };
        using var client = new HttpClient(handler);
        var result = await Check(client, "0.2.2+" + handler.Revision);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(handler.Revision, result.Value<string>("release_revision"));
        Assert.False(result.Value<bool>("update_available"));
        Assert.DoesNotContain("untrusted", result.ToString());
    }

    [Theory]
    [InlineData("{\"ref\":\"refs/tags/other\",\"object\":{\"type\":\"commit\",\"sha\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}")]
    [InlineData("{\"ref\":\"refs/tags/v0.2.2\",\"object\":{\"type\":\"commit\",\"sha\":\"../unsafe\"}}")]
    [InlineData("{\"ref\":\"refs/tags/v0.2.2\",\"object\":{\"type\":\"tree\",\"sha\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}")]
    public async Task Invalid_tag_identity_never_claims_a_release_build_match(string metadata)
    {
        using var client = new HttpClient(new Handler { RefBody = metadata });
        var result = await Check(client, "0.2.2+" + new string('a', 40));
        Assert.Equal("invalid_release_metadata", result.Value<string>("build_reason"));
        Assert.Equal("unknown", (string?)result["components"]?["server"]?["build_status"]);
        Assert.Null(result.Value<bool?>("update_available"));
    }

    public static IEnumerable<object[]> WrongTagMetadataTypes()
    {
        foreach (var value in new JToken[] { new JObject(), new JArray(), new JValue(42), new JValue(true), JValue.CreateNull() })
        {
            foreach (var field in new[] { "ref", "type", "tag", "annotated_type" })
            {
                var reference = new JObject { ["ref"] = "refs/tags/v0.2.2",
                    ["object"] = new JObject { ["type"] = "commit", ["sha"] = new string('a', 40) } };
                var annotated = new JObject { ["tag"] = "v0.2.2",
                    ["object"] = new JObject { ["type"] = "commit", ["sha"] = new string('a', 40) } };
                if (field == "ref") reference["ref"] = value.DeepClone();
                else if (field == "type") reference["object"]!["type"] = value.DeepClone();
                else
                {
                    reference["object"]!["type"] = "tag";
                    reference["object"]!["sha"] = new string('b', 40);
                    if (field == "tag") annotated["tag"] = value.DeepClone();
                    else annotated["object"]!["type"] = value.DeepClone();
                }
                yield return new object[] { field, value.Type.ToString(), reference.ToString(), annotated.ToString() };
            }
        }
    }

    [Theory]
    [MemberData(nameof(WrongTagMetadataTypes))]
    public async Task Wrong_tag_metadata_types_return_unknown_build_without_throwing(
        string field, string tokenType, string reference, string annotated)
    {
        using var client = new HttpClient(new Handler { RefBody = reference, TagBody = annotated });
        var result = await Check(client, "0.2.2+" + new string('a', 40));
        Assert.Equal("checked", result.Value<string>("status"));
        Assert.True(result.Value<string>("build_reason") == "invalid_release_metadata", $"{field} ({tokenType}): {result}");
        Assert.Null(result.Value<string>("release_revision"));
        foreach (var component in new[] { "server", "plugin" })
        {
            Assert.Equal("match", (string?)result["components"]?[component]?["version_status"]);
            Assert.Equal("unknown", (string?)result["components"]?[component]?["build_status"]);
            Assert.Equal("unknown", (string?)result["components"]?[component]?["status"]);
        }
        Assert.Null(result.Value<bool?>("update_available"));
    }
}
