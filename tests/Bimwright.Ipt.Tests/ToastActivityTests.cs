using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Toast.Tests;

/// <summary>
/// The card state machine as ipt-mcp drives it: rvt-mcp's <see cref="ActivityAggregator"/> plus the ipt-only
/// <c>Record(ToastModel)</c> / <c>ShowResult</c> additions. Time is injected, so no test waits.
/// </summary>
public sealed class ToastActivityTests
{
    private static ToastModel Result(bool success = true, string? image = null, string command = "extrude") =>
        new(command, "Extrude", "Feature", "Result", "", image, ToolActivityKind.Write, success, 12);

    private static ToastModel TaskReport(string outcome) => ToastContentBuilder.Build(new ToastEvent(
        "report_task_result", true,
        JObject.FromObject(new { task_id = "job-1", outcome, summary = "Checked result" }), null, null, 0, true));

    private sealed class Clock
    {
        public TimeSpan Now;
        public void Advance(int seconds) => Now += TimeSpan.FromSeconds(seconds);
        public ActivityAggregator Activity() => new(now: () => Now);
    }

    private static ActivitySnapshot Card(ActivityAggregator activity)
    {
        var render = activity.TakeRender();
        Assert.NotNull(render.Card);
        return render.Card!;
    }

    [Fact]
    public void Hundred_mixed_results_have_one_card_and_one_pending_render()
    {
        var activity = new Clock().Activity();
        Assert.True(activity.Record(Result(), true));
        for (var i = 1; i < 100; i++)
            Assert.False(activity.Record(Result(success: i % 10 != 0, image: i % 3 == 0 ? "capture.png" : null), true));
        var render = activity.TakeRender();
        Assert.Equal(ActivityCardPhase.Visible, render.Phase);
        Assert.Equal(91, render.Card!.Succeeded);
        Assert.Equal(9, render.Card.Failed);
        Assert.Equal(30, render.Card.Images);
        Assert.True(render.Card.HasFailure);
        Assert.True(render.Card.LatestSuccess);
        Assert.Equal("Extrude", render.Card.Title);
        Assert.True(activity.Record(Result(), true)); // draining the render permits exactly one new signal
        Assert.Equal(render.Card.CardId, activity.TakeRender().Card!.CardId);
    }

    [Fact]
    public void Failed_capture_never_increments_image_count()
    {
        var activity = new Clock().Activity();
        activity.Record(Result(false, "capture.png"), true);
        Assert.Equal(0, Card(activity).Images);
    }

    [Fact]
    public void Inline_capture_counts_as_success_and_capture_without_thumbnail()
    {
        var activity = new Clock().Activity();
        var model = ToastContentBuilder.Build(new ToastEvent("capture_view", true,
            JObject.FromObject(new { base64 = "image", width = 800, height = 600 }), null, null, 0, true));
        Assert.Null(model.ThumbnailPath);
        activity.Record(model, true);
        var card = Card(activity);
        Assert.Equal(1, card.Succeeded);
        Assert.Equal(1, card.Images);
        Assert.Null(card.ImagePath);
        Assert.Equal(0, card.Failed);
    }

    [Fact]
    public void Failed_inline_capture_counts_only_as_failed()
    {
        var activity = new Clock().Activity();
        activity.Record(Result(false, command: "capture_view"), true);
        var card = Card(activity);
        Assert.Equal(0, card.Images);
        Assert.Equal(1, card.Failed);
    }

    [Fact]
    public void Connection_status_does_not_count_and_cannot_cover_activity()
    {
        var activity = new Clock().Activity();
        activity.ShowStatus("Agent connected", "Ready", 6);
        Assert.True(Card(activity).IsStatus);
        activity.Record(Result(), true);
        var open = Card(activity);
        Assert.False(open.IsStatus);
        Assert.Equal(1, open.Succeeded);
        Assert.False(activity.ShowConnectionStatus("Agent connected", "Ready", 6));
        Assert.Equal(open.CardId, Card(activity).CardId);
    }

    [Fact]
    public void Connection_status_may_replace_an_earlier_status_and_a_finished_report()
    {
        var activity = new Clock().Activity();
        Assert.True(activity.ShowConnectionStatus("Agent connected", "one", 6));
        var first = Card(activity);
        Assert.True(activity.ShowConnectionStatus("Agent connected", "two", 6));
        Assert.NotEqual(first.CardId, Card(activity).CardId);

        activity.Record(TaskReport("completed"), true);
        var report = Card(activity);
        activity.Dismiss(report.CardId);
        activity.TakeRender();
        activity.CardClosed(report.CardId);
        Assert.True(activity.ShowConnectionStatus("Agent connected", "three", 6));
        Assert.Equal("three", Card(activity).Body);
    }

    [Fact]
    public void Idle_expires_twenty_seconds_after_latest_result_not_latest_render()
    {
        var clock = new Clock();
        var activity = clock.Activity();
        activity.Record(Result(), true);
        clock.Advance(19);
        Assert.Equal(ActivityCardPhase.Visible, activity.TakeRender().Phase);
        activity.Record(Result(), true);
        clock.Advance(19);
        Assert.False(activity.Tick(true));
        Assert.Equal(ActivityCardPhase.Visible, activity.TakeRender().Phase);
        clock.Advance(1);
        Assert.True(activity.Tick(true));
        Assert.Equal(ActivityCardPhase.Closing, activity.TakeRender().Phase);
    }

    [Fact]
    public void Real_hover_pauses_and_leave_rearms_a_full_idle_interval()
    {
        var clock = new Clock();
        var activity = clock.Activity();
        activity.Record(Result(), true);
        var id = Card(activity).CardId;
        clock.Advance(19);
        activity.PointerEntered(id);
        clock.Advance(50);
        activity.Record(Result(), true);
        Assert.Equal(ActivityCardPhase.Visible, activity.TakeRender().Phase);
        activity.PointerLeft(id);
        clock.Advance(19);
        Assert.False(activity.Tick(true));
        clock.Advance(1);
        Assert.True(activity.Tick(true));
        Assert.Equal(ActivityCardPhase.Closing, activity.TakeRender().Phase);
    }

    [Fact]
    public void Late_hover_cannot_revive_an_expired_card_and_requests_render()
    {
        var clock = new Clock();
        var activity = clock.Activity();
        activity.Record(Result(), true);
        var id = Card(activity).CardId;
        clock.Advance(20);
        activity.PointerEntered(id);
        Assert.Equal(ActivityCardPhase.Closing, activity.TakeRender().Phase);
    }

    [Fact]
    public void Pending_without_a_window_is_restored_by_flush_with_full_counts_and_fresh_deadline()
    {
        var clock = new Clock();
        var activity = clock.Activity();
        for (var i = 0; i < 100; i++) activity.Record(Result(i != 50), false);
        Assert.True(activity.HasUnrenderedResults);
        Assert.Equal(ActivityCardPhase.Hidden, activity.TakeRender().Phase);
        clock.Advance(120);
        Assert.True(activity.FlushIfUsable(true));
        var card = Card(activity);
        Assert.Equal(99, card.Succeeded);
        Assert.Equal(1, card.Failed);
        clock.Advance(19);
        Assert.False(activity.Tick(true));
        clock.Advance(1);
        Assert.True(activity.Tick(true));
    }

    [Fact]
    public void Parking_resets_hover_and_ignores_window_close_callback()
    {
        var clock = new Clock();
        var activity = clock.Activity();
        activity.Record(Result(), true);
        var id = Card(activity).CardId;
        activity.PointerEntered(id);
        activity.Tick(false);
        Assert.Equal(ActivityCardPhase.Hidden, activity.TakeRender().Phase);
        activity.CardClosed(id);
        clock.Advance(120);
        activity.FlushIfUsable(true);
        Assert.Equal(id, Card(activity).CardId);
        clock.Advance(20);
        activity.Tick(true);
        Assert.Equal(ActivityCardPhase.Closing, activity.TakeRender().Phase);
    }

    [Fact]
    public void Result_arriving_during_fade_starts_a_new_card_and_old_callbacks_are_ignored()
    {
        var activity = new Clock().Activity();
        activity.Record(Result(false), true);
        var oldId = Card(activity).CardId;
        Assert.True(activity.Dismiss(oldId));
        activity.TakeRender();
        activity.Record(Result(), true);
        var card = Card(activity);
        Assert.NotEqual(oldId, card.CardId);
        Assert.False(card.HasFailure);
        Assert.Equal(1, card.Succeeded);
        activity.CardClosed(oldId);
        Assert.False(activity.Dismiss(oldId));
        activity.PointerEntered(oldId);
        activity.PointerLeft(oldId);
        Assert.Equal(card.CardId, Card(activity).CardId);
    }

    [Fact]
    public void Toggle_reset_clears_pending_counts_and_allows_one_off_confirmation()
    {
        var activity = new Clock().Activity();
        activity.Record(Result(false), false);
        activity.Reset();
        activity.ShowStatus("Toast notifications disabled", "Hidden", 3);
        var card = Card(activity);
        Assert.True(card.IsStatus);
        Assert.Equal(0, card.Failed);
        Assert.False(card.HasFailure);
        activity.CardClosed(card.CardId);
        Assert.False(activity.HasUnrenderedResults);
        Assert.Equal(ActivityCardPhase.Hidden, activity.TakeRender().Phase);
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    [InlineData("cancelled", true)]
    public void Explicit_task_report_replaces_activity_without_inflating_counts(string outcome, bool success)
    {
        var activity = new Clock().Activity();
        activity.Record(Result(false), true);
        activity.Record(TaskReport(outcome), false);
        Assert.Equal(ActivityCardPhase.Hidden, activity.TakeRender().Phase);   // parked until the frame is usable
        Assert.True(activity.FlushIfUsable(true));
        var card = Card(activity);
        Assert.True(card.IsStatus);
        Assert.Equal(success, card.LatestSuccess);
        Assert.Equal(!success, card.HasFailure);
        Assert.Equal("Agent reported · Checked result · job-1", card.Body);
        Assert.Equal(0, card.Succeeded);
        Assert.False(activity.ShowConnectionStatus("Agent connected", "Ready", 6));   // never covers a report
        activity.Record(Result(), true);
        Assert.Equal(1, Card(activity).Succeeded);
    }

    [Fact]
    public void Task_report_replaces_an_open_activity_card_even_when_the_frame_is_usable()
    {
        var activity = new Clock().Activity();
        activity.Record(Result(), true);
        var activityId = Card(activity).CardId;
        activity.Record(TaskReport("completed"), true);
        var report = Card(activity);
        Assert.NotEqual(activityId, report.CardId);
        Assert.True(report.IsStatus);
        Assert.Equal("Task completed", report.Title);
    }

    [Fact]
    public void Task_report_keeps_its_own_eight_second_lifetime_after_a_park()
    {
        var clock = new Clock();
        var activity = clock.Activity();
        activity.Record(TaskReport("completed"), false);
        activity.TakeRender();
        clock.Advance(100);
        Assert.True(activity.FlushIfUsable(true));
        activity.TakeRender();
        clock.Advance(7);
        Assert.Equal(ActivityCardPhase.Visible, activity.TakeRender().Phase);
        clock.Advance(1);
        Assert.Equal(ActivityCardPhase.Closing, activity.TakeRender().Phase);
    }

    [Fact]
    public void Soft_script_failure_counts_as_failed_even_when_envelope_succeeded()
    {
        var activity = new Clock().Activity();
        activity.Record(ToastContentBuilder.Build(new ToastEvent("send_code", true,
            JObject.Parse("{\"ok\":false,\"error\":\"Script failed\"}"), null, null, 0, false)), true);
        Assert.Equal(1, Card(activity).Failed);
    }

    [Fact]
    public void Parallel_producers_do_not_lose_counts()
    {
        var activity = new Clock().Activity();
        Parallel.For(0, 1000, i => activity.Record(Result(i % 2 == 0), false));
        activity.TakeRender();
        activity.FlushIfUsable(true);
        var card = Card(activity);
        Assert.Equal(500, card.Succeeded);
        Assert.Equal(500, card.Failed);
    }

    [Fact]
    public void Status_uses_its_own_lifetime()
    {
        var clock = new Clock();
        var activity = clock.Activity();
        activity.ShowStatus("Agent connected", "Ready", 6);
        activity.TakeRender();
        clock.Advance(5);
        Assert.Equal(ActivityCardPhase.Visible, activity.TakeRender().Phase);
        clock.Advance(1);
        Assert.Equal(ActivityCardPhase.Closing, activity.TakeRender().Phase);
    }
}
