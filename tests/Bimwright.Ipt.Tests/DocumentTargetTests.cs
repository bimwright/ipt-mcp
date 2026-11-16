using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

// Family contract 5.2/5.3 (FC-DOC, FC-PAIR) with the owner decisions of 2026-11-16 (Q-A, Q-B, Q-C).
public sealed class DocumentTargetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ipt-doc-target-").FullName;
    private readonly string _pipe = "ipt-doc-target-" + Guid.NewGuid().ToString("N");

    public void Dispose() => Directory.Delete(_root, true);

    private static DocumentTargetEnvelope Pinned(string? id) => new() { ExpectedDocumentId = id };

    // ---- Policy (shared by gateway and add-in) ----

    [Fact]
    public void Reads_never_need_a_confirmed_target() =>
        Assert.Null(DocumentTargetPolicy.Check("list_parameters", isReadOnly: true, null, target: null, "doc-b", "B.ipt"));

    [Fact]
    public void A_write_from_a_gateway_without_the_protocol_needs_pairing() =>
        Assert.Equal(InventorErrorCodes.PAIRING_REQUIRED,
            DocumentTargetPolicy.Check("extrude", false, new JObject(), target: null, "doc-a", "A.ipt")?.Code);

    [Fact]
    public void A_write_without_a_confirmed_document_is_not_started() =>
        Assert.Equal(InventorErrorCodes.DOCUMENT_UNCONFIRMED,
            DocumentTargetPolicy.Check("extrude", false, new JObject(), Pinned(null), "doc-a", "A.ipt")?.Code);

    [Fact]
    public void A_write_runs_only_while_the_confirmed_document_is_active()
    {
        Assert.Null(DocumentTargetPolicy.Check("extrude", false, new JObject(), Pinned("doc-a"), "doc-a", "A.ipt"));
        var changed = DocumentTargetPolicy.Check("extrude", false, new JObject(), Pinned("doc-a"), "doc-b", "B.ipt");
        Assert.Equal(InventorErrorCodes.DOCUMENT_CHANGED, changed?.Code);
        Assert.Contains("not started", changed?.Message);
        Assert.Contains("'B.ipt'", changed?.Message);
        Assert.Equal(InventorErrorCodes.DOCUMENT_CHANGED,
            DocumentTargetPolicy.Check("extrude", false, new JObject(), Pinned("doc-a"), DocumentTargetPolicy.NoDocument, null)?.Code);
    }

    [Fact]
    public void An_explicit_document_parameter_skips_the_active_document_check_Q_A()
    {
        var p = new JObject { ["document"] = @"C:\work\B.ipt" };
        Assert.Null(DocumentTargetPolicy.Check("set_parameter", false, p, Pinned("doc-a"), "doc-c", "C.ipt"));
        // Still a write: an old gateway is refused.
        Assert.Equal(InventorErrorCodes.PAIRING_REQUIRED, DocumentTargetPolicy.Check("set_parameter", false, p, null, "doc-c", "C.ipt")?.Code);
        Assert.False(DocumentTargetPolicy.HasExplicitDocument(new JObject { ["document"] = "  " }));
    }

    [Theory]
    [InlineData("open_document")]
    [InlineData("open_documents")]
    [InlineData("new_part")]
    [InlineData("new_assembly")]
    [InlineData("new_drawing")]
    [InlineData("new_sheet_fabrication_part")]
    [InlineData("create_part")]
    public void Open_and_create_commands_skip_the_active_document_check(string command)
    {
        Assert.Null(DocumentTargetPolicy.Check(command, false, new JObject(), Pinned("doc-a"), "doc-b", "B.ipt"));
        Assert.True(DocumentTargetPolicy.NeedsConfirmedTarget(command, false, new JObject()));
    }

    [Fact]
    public void Dry_compiles_and_bake_installs_are_not_document_writes()
    {
        Assert.False(DocumentTargetPolicy.NeedsConfirmedTarget("send_code", false, new JObject { ["compile_only"] = true }));
        Assert.True(DocumentTargetPolicy.NeedsConfirmedTarget("send_code", false, new JObject { ["compile_only"] = false }));
        Assert.False(DocumentTargetPolicy.NeedsConfirmedTarget("apply_bake", false, new JObject()));
    }

    [Fact]
    public void Repin_follows_only_a_document_the_command_itself_activated_Q_B()
    {
        Assert.Equal("doc-new", DocumentTargetPolicy.Repin("new_part", Pinned("doc-a"), "doc-a", "doc-new"));
        // The user had switched to another document before the call: never adopt it.
        Assert.Null(DocumentTargetPolicy.Repin("new_part", Pinned("doc-a"), "doc-b", "doc-new"));
        // Nothing new became active (e.g. open_document with activate=false).
        Assert.Null(DocumentTargetPolicy.Repin("open_document", Pinned("doc-a"), "doc-a", "doc-a"));
        // Ordinary writes never move the pin.
        Assert.Null(DocumentTargetPolicy.Repin("extrude", Pinned("doc-a"), "doc-a", "doc-new"));
        Assert.Null(DocumentTargetPolicy.Repin("new_part", Pinned(null), "doc-a", "doc-new"));
        // Save-As changes the active document's path, and so its id: the pin follows it.
        Assert.Equal("doc-saved", DocumentTargetPolicy.Repin("save_document", Pinned("doc-a"), "doc-a", "doc-saved"));
        Assert.Null(DocumentTargetPolicy.Repin("save_document", Pinned("doc-a"), "doc-b", "doc-saved"));
    }

    [Fact]
    public void Copies_with_the_same_internal_name_get_different_ids()
    {
        // A checkpoint restore or Explorer copy keeps InternalName; live 16/11 both copies were open at once.
        var table = new DocumentIdTable();
        var working = table.IdFor(DocumentIdTable.Key("{C9CF0122}", @"C:\w\INLET-TUBE.ipt"));
        var restored = table.IdFor(DocumentIdTable.Key("{C9CF0122}", @"C:\w\INLET-TUBE-restored-1.ipt"));
        Assert.NotEqual(working, restored);
        Assert.Equal(working, table.IdFor(DocumentIdTable.Key("{C9CF0122}", @"c:\W\inlet-tube.IPT")));
        Assert.NotEqual(working, table.IdFor(DocumentIdTable.Key("{C9CF0122}", null)));   // unsaved vs saved
    }

    [Fact]
    public void Document_ids_last_until_the_document_closes()
    {
        var table = new DocumentIdTable();
        var first = table.IdFor("{A}");
        Assert.Equal(first, table.IdFor("{A}"));
        Assert.NotEqual(first, table.IdFor("{B}"));
        table.Forget("{A}");
        Assert.NotEqual(first, table.IdFor("{A}"));
    }

    // ---- Gateway ----

    private void WriteDescriptor(bool supportsDocumentTarget = true, int pid = 0) =>
        File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + (pid == 0 ? Environment.ProcessId : pid), ProcessId = pid == 0 ? Environment.ProcessId : pid,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = _pipe, AuthToken = "test-token",
            DocumentTitle = "A.ipt", LastHeartbeatUtc = DateTimeOffset.UtcNow,
            Capabilities = supportsDocumentTarget ? DocumentTargetFixture.Capabilities : null,
        }));

    private PluginClient Client() => new(new InventorMcpConfig { DescriptorDirectory = _root, TimeoutMs = 5000 });

    /// <summary>Answers one request like the add-in; returns the request envelope.</summary>
    private async Task<JObject> AnswerOnce(Func<JObject, JObject> respond, CancellationToken ct)
    {
        using var server = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync(ct);
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var request = JObject.Parse((await reader.ReadLineAsync(ct))!);
        await writer.WriteLineAsync(respond(request).ToString(Formatting.None));
        return request;
    }

    private static JObject Ok(JObject request, JObject? data = null, JObject? meta = null) =>
        new() { ["id"] = request["id"], ["ok"] = true, ["data"] = data ?? new JObject { ["ok"] = true }, ["meta"] = meta ?? new JObject() };

    [Fact]
    public async Task A_write_is_not_sent_before_the_target_is_confirmed_Q_C()
    {
        WriteDescriptor();
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => Client().SendAsync("extrude", new JObject(), default));
        Assert.Equal(InventorErrorCodes.TARGET_UNCONFIRMED, error.Code);
        Assert.Contains($"inventor_switch_target(target=\"pid:{Environment.ProcessId}\", verify=true)", error.Message);
    }

    [Fact]
    public async Task A_write_is_not_sent_to_an_add_in_without_the_protocol()
    {
        WriteDescriptor(supportsDocumentTarget: false);
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => Client().SendAsync("extrude", new JObject(), default));
        Assert.Equal(InventorErrorCodes.PAIRING_REQUIRED, error.Code);
    }

    [Fact]
    public async Task Reads_and_dry_compiles_still_reach_an_unconfirmed_or_old_add_in()
    {
        WriteDescriptor(supportsDocumentTarget: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = Client();
        foreach (var (command, p) in new[] { ("list_parameters", new JObject()), ("send_code", new JObject { ["code"] = "", ["compile_only"] = true }) })
        {
            var answer = AnswerOnce(r => Ok(r), timeout.Token);
            await client.SendAsync(command, p, timeout.Token);
            Assert.Equal(command, (string?)(await answer)["command"]);
        }
    }

    [Fact]
    public async Task Verify_pins_the_active_document_and_writes_carry_it()
    {
        WriteDescriptor();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = Client();
        var tools = new MetaTools(client);

        var verifyAnswer = AnswerOnce(r => Ok(r, new JObject { ["document_id"] = "doc-a", ["document_title"] = "A.ipt" }), timeout.Token);
        var switched = JObject.Parse(await tools.SwitchTarget("pid:" + Environment.ProcessId, verify: true, timeout.Token));
        Assert.Equal("document_target", (string?)(await verifyAnswer)["command"]);
        Assert.True((bool)switched["document_confirmed"]!);
        Assert.Equal("doc-a", (string?)switched["confirmed_document_id"]);
        Assert.Equal("A.ipt", (string?)switched["confirmed_document_title"]);

        var writeAnswer = AnswerOnce(r => Ok(r), timeout.Token);
        await client.SendAsync("extrude", new JObject(), timeout.Token);
        var request = await writeAnswer;
        Assert.Equal(DocumentTargetPolicy.Protocol, (string?)request["document_target"]?["protocol"]);
        Assert.Equal("doc-a", (string?)request["document_target"]?["expected_document_id"]);

        // Selecting the same session again keeps the confirmed document.
        var again = JObject.Parse(await tools.SwitchTarget("pid:" + Environment.ProcessId));
        Assert.True((bool)again["document_confirmed"]!);
        var current = JObject.Parse(tools.GetCurrentTarget());
        Assert.Equal("doc-a", (string?)current["confirmed_document_id"]);
    }

    [Fact]
    public async Task Verify_against_an_old_add_in_reports_pairing()
    {
        WriteDescriptor(supportsDocumentTarget: false);
        var tools = new MetaTools(Client());
        var response = JObject.Parse(await tools.SwitchTarget("pid:" + Environment.ProcessId, verify: true));
        Assert.Equal(InventorErrorCodes.PAIRING_REQUIRED, (string?)response["error"]?["code"]);
        var current = JObject.Parse(tools.GetCurrentTarget());
        Assert.False((bool)current["document_confirmed"]!);
        Assert.Contains("PAIRING_REQUIRED", (string?)current["next_step"]);
    }

    [Fact]
    public async Task An_open_or_create_command_moves_the_pin_when_the_add_in_says_so_Q_B()
    {
        WriteDescriptor();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = Client().Confirmed();
        var target = client.ResolveTarget();

        var answer = AnswerOnce(r => Ok(r, meta: new JObject { ["document_id"] = "doc-new", ["document_title"] = "Part2", ["repin_document_id"] = "doc-new" }), timeout.Token);
        await client.SendAsync("new_part", new JObject(), timeout.Token);
        await answer;
        Assert.Equal("doc-new", client.PinnedDocument(target)?.Id);
        Assert.Equal("Part2", client.PinnedDocument(target)?.Title);

        // A plain write reporting another active document does not move the pin.
        answer = AnswerOnce(r => Ok(r, meta: new JObject { ["document_id"] = "doc-other" }), timeout.Token);
        await client.SendAsync("extrude", new JObject(), timeout.Token);
        await answer;
        Assert.Equal("doc-new", client.PinnedDocument(target)?.Id);
    }

    [Fact]
    public async Task An_add_in_refusal_reaches_the_caller_and_keeps_the_pin()
    {
        WriteDescriptor();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = Client().Confirmed();
        var answer = AnswerOnce(r => new JObject
        {
            ["id"] = r["id"], ["ok"] = false,
            ["error"] = new JObject { ["code"] = InventorErrorCodes.DOCUMENT_CHANGED, ["message"] = "extrude was not started" },
            ["meta"] = new JObject { ["document_id"] = "doc-b" },
        }, timeout.Token);
        var error = await Assert.ThrowsAsync<InventorGatewayException>(() => client.SendAsync("extrude", new JObject(), timeout.Token));
        await answer;
        Assert.Equal(InventorErrorCodes.DOCUMENT_CHANGED, error.Code);
        Assert.Equal(DocumentTargetFixture.DocumentId, client.PinnedDocument(client.ResolveTarget())?.Id);
    }
}
