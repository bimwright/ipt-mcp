using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>rvt-mcp #19 parity: one gateway stays on one Inventor session and never guesses between several.</summary>
public sealed class TargetBindingTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inv-bind-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<int, TargetBinding.ProcessProbe> _processes = new();
    // The registry checks liveness with real pids, so descriptors use long-lived system processes.
    private readonly int[] _pids = Process.GetProcesses().Select(p => p.Id).Where(id => id > 4).OrderBy(id => id).Take(3).ToArray();

    public TargetBindingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void Write(int year, int pid, int ageSeconds = 0)
    {
        File.WriteAllText(Path.Combine(_dir, $"inventor-{year}-{pid}.json"), new JObject
        {
            ["target_id"] = $"inventor-{year}-{pid}",
            ["inventor_year"] = year,
            ["process_id"] = pid,
            ["host_app"] = "Inventor",
            ["transport"] = "pipe",
            ["pipe_name"] = "BimwrightInventor-" + pid,
            ["auth_token"] = "tok-" + pid,
            ["document_title"] = $"model-{pid}.ipt",
            ["last_heartbeat_utc"] = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds),
        }.ToString());
        _processes.TryAdd(pid, new TargetBinding.ProcessProbe(true, Started));
    }

    private void Remove(int year, int pid) => File.Delete(Path.Combine(_dir, $"inventor-{year}-{pid}.json"));

    private PluginClient Client(string? target = null) =>
        new(new InventorMcpConfig { DescriptorDirectory = _dir, TargetId = target },
            pid => _processes.TryGetValue(pid, out var p) ? p : new TargetBinding.ProcessProbe(false, null));

    private static string Code(Action call) => Assert.Throws<InventorGatewayException>(call).Code;

    [Fact]
    public void Single_live_target_binds_automatically()
    {
        Write(2027, _pids[0]);
        var client = Client();

        Assert.Equal(_pids[0], client.ResolveTarget().ProcessId);
        Assert.Equal("auto", client.SelectedBy);
    }

    [Fact]
    public async Task Several_live_targets_without_selection_fail_closed_with_candidates()
    {
        Write(2027, _pids[0]);
        Write(2027, _pids[1]);
        var client = Client();

        var error = Assert.Throws<InventorGatewayException>(() => client.ResolveTarget());
        Assert.Equal(InventorErrorCodes.AMBIGUOUS_TARGET, error.Code);
        Assert.Equal(2, ((JArray)error.ResponseData!["candidates"]!).Count);
        Assert.DoesNotContain("tok-", error.ResponseData.ToString());

        var write = await Assert.ThrowsAsync<InventorGatewayException>(
            () => client.SendAsync("extrude", new JObject(), CancellationToken.None));
        Assert.Equal(InventorErrorCodes.AMBIGUOUS_TARGET, write.Code);
        Assert.Null(client.SelectedTargetId);
    }

    [Fact]
    public void Selection_survives_a_newer_session_appearing()
    {
        Write(2027, _pids[0]);
        var client = Client();
        client.ResolveTarget();

        Write(2027, _pids[1]); // newest heartbeat used to win the old FirstOrDefault fallback

        Assert.Equal(_pids[0], client.ResolveTarget().ProcessId);
    }

    [Fact]
    public void Exited_target_is_lost_and_never_replaced_even_by_a_single_candidate()
    {
        Write(2027, _pids[0]);
        Write(2027, _pids[1]);
        var client = Client();
        client.SwitchTarget("pid:" + _pids[0]);

        _processes[_pids[0]] = new TargetBinding.ProcessProbe(false, null);
        Remove(2027, _pids[0]);

        Assert.Equal(InventorErrorCodes.TARGET_LOST, Code(() => client.ResolveTarget()));
        Assert.Equal(InventorErrorCodes.TARGET_LOST, Code(() => client.ResolveTarget()));

        Assert.Equal(_pids[1], client.SwitchTarget("pid:" + _pids[1]).ProcessId);
        Assert.Equal(_pids[1], client.ResolveTarget().ProcessId);
    }

    [Fact]
    public void Reused_pid_with_a_new_start_time_is_lost()
    {
        Write(2027, _pids[0]);
        var client = Client();
        client.ResolveTarget();

        _processes[_pids[0]] = new TargetBinding.ProcessProbe(true, Started.AddMinutes(5));

        Assert.Equal(InventorErrorCodes.TARGET_LOST, Code(() => client.ResolveTarget()));
    }

    [Fact]
    public void Running_target_without_descriptor_is_unavailable_but_stays_selected()
    {
        Write(2027, _pids[0]);
        Write(2027, _pids[1]);
        var client = Client();
        client.SwitchTarget("id:inventor-2027-" + _pids[0]);

        Remove(2027, _pids[0]); // add-in restarting inside the same Inventor process

        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, Code(() => client.ResolveTarget()));
        Write(2027, _pids[0]);
        Assert.Equal(_pids[0], client.ResolveTarget().ProcessId);
    }

    [Fact]
    public void Ambiguous_or_unknown_switch_keeps_the_current_selection()
    {
        Write(2026, _pids[0]);
        Write(2027, _pids[1]);
        Write(2027, _pids[2]);
        var client = Client();
        client.SwitchTarget("2026");

        Assert.Equal(InventorErrorCodes.AMBIGUOUS_TARGET, Code(() => client.SwitchTarget("2027")));
        Assert.Equal(InventorErrorCodes.NO_TARGET, Code(() => client.SwitchTarget("pid:1")));
        Assert.Equal(InventorErrorCodes.INVALID_TARGET_SELECTOR, Code(() => client.SwitchTarget(" ")));
        Assert.Equal(_pids[0], client.ResolveTarget().ProcessId);

        Assert.Equal(_pids[2], client.SwitchTarget("BimwrightInventor-" + _pids[2], rebind: true).ProcessId);
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("2022", true)]
    [InlineData("2027", true)]
    [InlineData("pid:123", true)]
    [InlineData("id:inventor-2027-123", true)]
    [InlineData("inventor-2027-123", true)]          // older clients: bare target id
    [InlineData("BimwrightInventor-123", true)]      // older clients: pipe name
    [InlineData("", false)]
    [InlineData("2021", false)]
    [InlineData("2028", false)]
    [InlineData("123", false)]
    [InlineData("pid:", false)]
    [InlineData("pid:abc", false)]
    [InlineData("pid:-5", false)]
    [InlineData("id:", false)]
    [InlineData("R2027", false)]
    [InlineData("revit-2027", false)]
    public void Selector_syntax_FC_TGT_06(string selector, bool valid)
        => Assert.Equal(valid, TargetBinding.IsValidSelector(selector));

    [Fact]
    public void Invalid_selector_keeps_the_selection_FC_TGT_06()
    {
        Write(2027, _pids[0]);
        var client = Client();
        client.SwitchTarget("pid:" + _pids[0]);
        Assert.Equal(InventorErrorCodes.INVALID_TARGET_SELECTOR, Code(() => client.SwitchTarget("R2027")));
        Assert.Equal(_pids[0], client.ResolveTarget().ProcessId);
    }

    [Fact]
    public void Leaving_a_selected_live_session_needs_rebind_FC_TGT_03()
    {
        Write(2026, _pids[0]);
        Write(2027, _pids[1]);
        var client = Client();
        client.SwitchTarget("pid:" + _pids[0]);

        Assert.Equal(InventorErrorCodes.TARGET_REBIND_REQUIRED, Code(() => client.SwitchTarget("pid:" + _pids[1])));
        Assert.Equal(InventorErrorCodes.TARGET_REBIND_REQUIRED, Code(() => client.SwitchTarget("2027")));
        Assert.Equal(_pids[0], client.ResolveTarget().ProcessId);   // refused switches keep the selection

        // Selecting the same session again is not leaving it.
        Assert.Equal(_pids[0], client.SwitchTarget("2026").ProcessId);

        Assert.Equal(_pids[1], client.SwitchTarget("pid:" + _pids[1], rebind: true).ProcessId);
        Assert.Equal(_pids[1], client.ResolveTarget().ProcessId);
    }

    [Fact]
    public void Auto_or_year_never_moves_an_automatic_binding_but_pid_does_FC_TGT_03()
    {
        Write(2026, _pids[0]);
        var client = Client();
        client.ResolveTarget();                       // first call bound automatically (not confirmed)
        Write(2027, _pids[1]);

        Assert.Equal(InventorErrorCodes.TARGET_REBIND_REQUIRED, Code(() => client.SwitchTarget("2027")));
        Assert.Equal(InventorErrorCodes.AMBIGUOUS_TARGET, Code(() => client.SwitchTarget("auto")));
        Assert.Equal(_pids[1], client.SwitchTarget("pid:" + _pids[1]).ProcessId);
    }

    [Fact]
    public void A_lost_selection_is_replaced_by_pid_without_rebind_but_never_by_auto()
    {
        Write(2027, _pids[0]);
        Write(2027, _pids[1]);
        var client = Client();
        client.SwitchTarget("pid:" + _pids[0]);
        _processes[_pids[0]] = new TargetBinding.ProcessProbe(false, null);
        Remove(2027, _pids[0]);

        Assert.Equal(InventorErrorCodes.TARGET_REBIND_REQUIRED, Code(() => client.SwitchTarget("auto")));
        Assert.Equal(InventorErrorCodes.TARGET_LOST, Code(() => client.ResolveTarget()));
        Assert.Equal(InventorErrorCodes.TARGET_REBIND_REQUIRED, Code(() => client.SwitchTarget("2027")));
        Assert.Equal(_pids[1], client.SwitchTarget("pid:" + _pids[1]).ProcessId);
    }

    [Fact]
    public void Get_current_target_never_binds_FC_TGT_05()
    {
        Write(2027, _pids[0]);
        var client = Client();
        var tools = new MetaTools(client);

        var report = JObject.Parse(tools.GetCurrentTarget());
        Assert.False((bool)report["bound"]!);
        Assert.Single((JArray)report["candidates"]!);
        Assert.Null(client.SelectedTargetId);
        tools.ListAvailableTargets();
        Assert.Null(client.SelectedTargetId);

        client.ResolveTarget();
        var bound = JObject.Parse(tools.GetCurrentTarget());
        Assert.True((bool)bound["bound"]!);
        Assert.Equal(_pids[0], (int)bound["process_id"]!);
    }

    [Fact]
    public void Config_target_year_selects_the_matching_session_and_refuses_ambiguity()
    {
        Write(2025, _pids[0]);
        Write(2027, _pids[1]); // newer heartbeat; the old code ignored a year and fell back to it
        var pinned = Client("2025");
        Assert.Equal(_pids[0], pinned.ResolveTarget().ProcessId);
        Assert.Equal("config", pinned.SelectedBy);

        Write(2025, _pids[2]);
        Assert.Equal(InventorErrorCodes.AMBIGUOUS_TARGET, Code(() => Client("2025").ResolveTarget()));
        Assert.Equal(InventorErrorCodes.NO_TARGET, Code(() => Client("2024").ResolveTarget()));
    }

    [Fact]
    public void Meta_tools_report_selection_and_ambiguity_without_tokens()
    {
        Write(2027, _pids[0]);
        Write(2027, _pids[1]);
        var client = Client();
        var tools = new MetaTools(client);

        var ambiguous = JObject.Parse(tools.GetCurrentTarget());
        Assert.False((bool)ambiguous["bound"]!);
        Assert.Equal(2, ((JArray)ambiguous["candidates"]!).Count);

        var switched = JObject.Parse(tools.SwitchTarget("pid:" + _pids[1]).GetAwaiter().GetResult());
        Assert.True((bool)switched["ok"]!);
        Assert.Equal(_pids[1], (int)switched["target"]!["process_id"]!);

        var current = JObject.Parse(tools.GetCurrentTarget());
        Assert.Equal("switch_target", (string?)current["selected_by"]);

        var list = JArray.Parse(tools.ListAvailableTargets());
        Assert.Single(list, t => (bool)t["selected"]!);
        Assert.Equal(_pids[1], (int)list.Single(t => (bool)t["selected"]!)["process_id"]!);
        Assert.DoesNotContain("tok-", list.ToString() + current + switched + ambiguous);
    }
}
