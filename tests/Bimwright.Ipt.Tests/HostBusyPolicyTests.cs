using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>Family contract FC-BUSY-01..05: host busy is HOST_BUSY, says "not started", and never runs later.</summary>
public class HostBusyPolicyTests
{
    [Theory]
    [InlineData("new_drawing")]
    [InlineData("list_open_documents")]
    [InlineData("send_code")]
    [InlineData("document_target")]
    public void Modal_dialog_refuses_every_command_but_health(string command)
    {
        Assert.True(HostBusyPolicy.RefusesForModal(command, modalOpen: true));
        Assert.False(HostBusyPolicy.RefusesForModal(command, modalOpen: false));
    }

    [Fact]
    public void Health_still_answers_while_a_modal_dialog_is_open()
        => Assert.False(HostBusyPolicy.RefusesForModal("health", modalOpen: true));

    [Fact]
    public void Busy_messages_say_the_command_was_not_started()
    {
        foreach (var message in new[]
                 {
                     HostBusyPolicy.ModalMessage("new_drawing", "Sheet:1 - TITLE A2 Prompted Texts"),
                     HostBusyPolicy.QueueFullMessage("extrude"),
                     HostBusyPolicy.ExpiredMessage("send_code", 30000),
                 })
        {
            Assert.Contains("was not started and will not run later", message);
            Assert.Contains("inventor_health", message);
        }
        Assert.Contains("(\"Sheet:1 - TITLE A2 Prompted Texts\")", HostBusyPolicy.ModalMessage("new_drawing", "Sheet:1 - TITLE A2 Prompted Texts"));
        Assert.DoesNotContain("(\"", HostBusyPolicy.ModalMessage("new_drawing", ""));
    }

    [Fact]
    public void Started_timeout_reports_an_unknown_outcome_not_a_refusal()
    {
        Assert.Contains("outcome is unknown", HostBusyPolicy.StartedTimeoutNote);
        Assert.DoesNotContain("not started", HostBusyPolicy.StartedTimeoutNote);
    }

    [Theory]
    [InlineData(false, false, false, null)]
    [InlineData(true, false, true, "sta_busy")]
    [InlineData(false, true, true, "modal_dialog")]
    [InlineData(true, true, true, "modal_dialog")]
    public void Health_reports_host_busy_and_reason(bool staBusy, bool modalOpen, bool busy, string? reason)
    {
        var data = new JObject { ["modal_dialog"] = new JObject { ["open"] = modalOpen } }.WithHostBusy(staBusy);
        Assert.Equal(busy, (bool)data["host_busy"]!);
        Assert.Equal(reason, (string?)data["busy_reason"]);
    }

    [Fact]
    public void Health_without_a_probe_reads_as_not_modal()
    {
        var data = new JObject().WithHostBusy(staBusy: false);
        Assert.False((bool)data["host_busy"]!);
    }

    /// <summary>FC-BUSY-03: work reported as expired before it started never runs when the STA frees up.</summary>
    [Fact]
    public void Expired_queued_work_never_runs_later()
    {
        var posted = new System.Collections.Generic.List<System.Action>();
        var queue = new StaWorkQueue(posted.Add, new StaQueueStats());
        var ran = false;
        using var cancel = new CancellationTokenSource();
        var task = queue.TryInvokeAsync(() => { ran = true; return 1; }, cancel.Token)!;

        cancel.Cancel();                 // HandleLine's deadline passed while the STA was busy
        Assert.True(task.IsCanceled);    // → HOST_BUSY "not started"
        foreach (var run in posted) run();   // the STA frees up and drains its messages
        Assert.False(ran);
    }
}
