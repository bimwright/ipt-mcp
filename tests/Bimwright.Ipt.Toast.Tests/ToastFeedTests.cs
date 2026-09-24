using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastFeedTests
{
    private static ToastModel Read(string command = "list_open_documents") => new(
        command, ToolNameFormatter.Format(command), "MCP · Query", "0 items", "", null,
        ToolActivityKind.Read, true, 2);

    [Fact]
    public void A_slow_consumer_and_many_distinct_errors_never_accumulate_more_than_three_cards()
    {
        var feed = new ToastFeed();
        for (var i = 0; i < 10000; i++)
            feed.Publish(Read() with { Success = false, Summary = "Error " + i });
        for (var i = 0; i < 100; i++) feed.Publish(Read());
        var cards = feed.TakeSnapshot(0)!;
        Assert.Equal(3, cards.Count);
        Assert.Equal(new[] { "Error 9999", "Error 9998", "Error 9997" }, cards.Select(c => c.Model.Summary));
        Assert.Null(feed.TakeSnapshot(500));
    }

    [Fact]
    public void Concurrent_producers_preserve_the_activity_count()
    {
        var feed = new ToastFeed();
        Parallel.For(0, 10000, _ => feed.Publish(Read()));
        Assert.Equal(10000, Assert.Single(feed.TakeSnapshot(0)!).Count);
    }

    [Fact]
    public void Writes_remain_identifiable_after_later_reads()
    {
        var feed = new ToastFeed();
        feed.Publish(Read("extrude") with { Kind = ToolActivityKind.Write });
        feed.Publish(Read());
        var card = Assert.Single(feed.TakeSnapshot(0)!);
        Assert.Equal(ToolActivityKind.Write, card.Model.Kind);
        Assert.Equal(6000, card.Model.LifetimeMs);
    }

    [Fact]
    public void Explicit_results_keep_separate_task_ids_and_update_repeated_reports_in_place()
    {
        var feed = new ToastFeed();
        var result = Read("report_task_result") with { TaskId = "agent-a", Summary = "Job A finished" };
        feed.Publish(result);
        var a = Assert.Single(feed.TakeSnapshot(0)!);
        feed.Publish(result with { Success = false, Summary = "Job A failed" });
        feed.Publish(result with { TaskId = "agent-b", Summary = "Job B finished" });
        for (var i = 0; i < 100; i++) feed.Publish(Read());
        var cards = feed.TakeSnapshot(500)!;
        Assert.Equal(3, cards.Count);
        Assert.Equal(a.Id, cards[0].Id);
        Assert.Equal("agent-a", cards[0].Model.TaskId);
        Assert.False(cards[0].Model.Success);
        Assert.Equal("agent-b", cards[1].Model.TaskId);
        Assert.Equal(100, cards[2].Count);
    }

    [Theory]
    [InlineData("failed", "Task failed")]
    [InlineData("cancelled", "Task cancelled")]
    public void Agent_reported_failure_is_not_disguised_as_a_successful_tool_call(string outcome, string title)
    {
        var model = ToastContentBuilder.Build(new ToastEvent("report_task_result", true,
            new JObject { ["task_id"] = "test-job", ["outcome"] = outcome, ["summary"] = "Did not finish" },
            null, null, 1, true));
        Assert.False(model.Success);
        Assert.Equal(title, model.Title);
        Assert.Equal("Did not finish", model.Summary);
    }

    [Fact]
    public void Only_an_explicit_agent_result_creates_a_task_summary_not_an_idle_gap()
    {
        var feed = new ToastFeed();
        feed.Publish(Read());
        Assert.Null(Assert.Single(feed.TakeSnapshot(0)!).Model.TaskId);
        Assert.Null(feed.TakeSnapshot(60000));
        var report = ToastContentBuilder.Build(new ToastEvent("report_task_result", true,
            JObject.Parse("""{"task_id":"agent-a-42","outcome":"completed","summary":"Checked 12 parts"}"""),
            null, null, 2, true));
        feed.Publish(report);
        var summary = feed.TakeSnapshot(60500)![0];
        Assert.Equal("agent-a-42", summary.Model.TaskId);
        Assert.Equal("Task completed", summary.Model.Title);
        Assert.Equal("Checked 12 parts", summary.Model.Summary);
        Assert.Contains("Agent reported", summary.Model.Detail);
        Assert.Equal(8000, summary.Model.LifetimeMs);
    }

    [Fact]
    public void Expiring_a_card_does_not_drop_an_update_that_arrived_during_its_fade()
    {
        var feed = new ToastFeed();
        feed.Publish(Read());
        var first = Assert.Single(feed.TakeSnapshot(0)!);
        feed.Publish(Read());
        feed.Dismiss(first.Id, first.Revision);
        var updated = Assert.Single(feed.TakeSnapshot(500)!);
        Assert.Equal(2, updated.Count);
        feed.Dismiss(updated.Id, updated.Revision);
        Assert.Empty(feed.TakeSnapshot(1000)!);
        feed.Publish(Read());
        var fresh = Assert.Single(feed.TakeSnapshot(1500)!);
        Assert.Equal(1, fresh.Count);
        Assert.NotEqual(first.Id, fresh.Id);
    }

    [Fact]
    public void Clearing_discards_pending_results_without_replaying_them_after_reenable()
    {
        var feed = new ToastFeed();
        feed.Publish(Read());
        var old = Assert.Single(feed.TakeSnapshot(0)!);
        feed.Publish(Read());
        feed.Clear();
        Assert.Empty(feed.TakeSnapshot(500)!);
        feed.Publish(Read());
        feed.Dismiss(old.Id, old.Revision);
        Assert.Equal(1, Assert.Single(feed.TakeSnapshot(1000)!).Count);
    }

    [Fact]
    public void Repeated_errors_update_one_card_with_an_occurrence_count()
    {
        var feed = new ToastFeed();
        var error = Read() with { Success = false, Summary = "No document", Detail = "NO_DOCUMENT" };
        for (var i = 0; i < 12; i++) feed.Publish(error);
        var card = Assert.Single(feed.TakeSnapshot(0)!);
        Assert.Equal(12, card.Count);
        Assert.Equal("No document", card.Model.Summary);
        Assert.Equal("NO_DOCUMENT · 12 occurrences", card.Model.Detail);
    }

    [Fact]
    public void Errors_and_important_results_survive_activity_floods_with_a_three_card_limit()
    {
        var feed = new ToastFeed();
        feed.Publish(Read() with { Success = false, Summary = "No document", Detail = "NO_DOCUMENT" });
        feed.Publish(Read("capture_view") with { ThumbnailPath = "capture.png" });
        for (var i = 0; i < 100; i++) feed.Publish(Read());
        var cards = feed.TakeSnapshot(0)!;
        Assert.Equal(3, cards.Count);
        Assert.False(cards[0].Model.Success);
        Assert.Equal("capture.png", cards[1].Model.ThumbnailPath);
        Assert.Equal(100, cards[2].Count);
        Assert.Equal(3, ToastLayout.MaxToasts);
    }

    [Fact]
    public void Updates_are_at_most_twice_a_second_and_keep_the_same_card_identity()
    {
        var feed = new ToastFeed();
        feed.Publish(Read());
        var first = Assert.Single(feed.TakeSnapshot(0)!);
        feed.Publish(Read("get_document_info"));
        Assert.Null(feed.TakeSnapshot(499));
        var second = Assert.Single(feed.TakeSnapshot(500)!);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.Count);
        Assert.Contains("Get Document Info", second.Model.Detail);
    }

    [Fact]
    public void Hundred_tool_results_become_one_activity_card_without_a_replay_queue()
    {
        var feed = new ToastFeed();
        for (var i = 0; i < 100; i++) feed.Publish(Read());

        var card = Assert.Single(feed.TakeSnapshot(0)!);
        Assert.Equal(100, card.Count);
        Assert.Equal("100 successful operations", card.Model.Summary);
        Assert.Null(feed.TakeSnapshot(500)); // no deferred per-tool playback
    }
}
