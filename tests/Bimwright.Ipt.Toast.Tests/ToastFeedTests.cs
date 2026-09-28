using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastFeedTests
{
    private static ToastModel Result(bool success = true, string? image = null) =>
        new("extrude", "Extrude", "Feature", "Result", "", image, ToolActivityKind.Write, success, 12);
    private sealed class Clock
    {
        public TimeSpan Now;
        public void Advance(int seconds) => Now += TimeSpan.FromSeconds(seconds);
        public ToastFeed Feed() => new(now: () => Now);
    }

    [Fact]
    public void Hundred_mixed_results_have_one_card_and_one_pending_render()
    {
        var feed = new Clock().Feed();
        Assert.True(feed.Record(Result(), true));
        for (var i = 1; i < 100; i++)
            Assert.False(feed.Record(Result(success: i % 10 != 0, image: i % 3 == 0 ? "capture.png" : null), true));
        var render = feed.TakeRender();
        Assert.Equal(ToastPhase.Visible, render.Phase);
        Assert.Equal(91, render.Card!.Succeeded);
        Assert.Equal(9, render.Card.Failed);
        Assert.Equal(30, render.Card.Captures);
        Assert.True(render.Card.HasFailure);
        Assert.True(render.Card.LatestSuccess);
        Assert.Equal("Extrude", render.Card.Title);
        Assert.True(feed.Record(Result(), true)); // draining the render permits exactly one new signal
        Assert.Equal(render.Card.Id, feed.TakeRender().Card!.Id);
    }

    [Fact]
    public void Failed_capture_never_increments_image_count()
    {
        var feed = new Clock().Feed();
        feed.Record(Result(false, "capture.png"), true);
        Assert.Equal(0, feed.TakeRender().Card!.Captures);
    }

    [Fact]
    public void Inline_capture_counts_as_success_and_capture_without_thumbnail()
    {
        var feed = new Clock().Feed();
        var model = ToastContentBuilder.Build(new ToastEvent("capture_view", true,
            JObject.FromObject(new { base64 = "image", width = 800, height = 600 }), null, null, 0, true));
        Assert.Null(model.ThumbnailPath);
        feed.Record(model, true);
        var card = feed.TakeRender().Card!;
        Assert.Equal(1, card.Succeeded);
        Assert.Equal(1, card.Captures);
        Assert.Equal(0, card.Failed);
    }

    [Fact]
    public void Connection_status_does_not_count_and_cannot_cover_activity()
    {
        var feed = new Clock().Feed();
        feed.ShowStatus("Agent connected", "Ready", 6, true);
        Assert.Equal(ToastCardKind.Status, feed.TakeRender().Card!.Kind);
        feed.Record(Result(), true);
        var activity = feed.TakeRender().Card!;
        Assert.Equal(1, activity.Succeeded);
        Assert.False(feed.ShowStatus("Agent connected", "Ready", 6, true));
        Assert.Equal(activity, feed.TakeRender().Card);
    }

    [Fact]
    public void Idle_expires_twenty_seconds_after_latest_result_not_latest_render()
    {
        var clock = new Clock();
        var feed = clock.Feed();
        feed.Record(Result(), true);
        clock.Advance(19);
        Assert.Equal(ToastPhase.Visible, feed.TakeRender().Phase);
        feed.Record(Result(), true);
        clock.Advance(19);
        Assert.False(feed.Tick(true));
        Assert.Equal(ToastPhase.Visible, feed.TakeRender().Phase);
        clock.Advance(1);
        Assert.True(feed.Tick(true));
        Assert.Equal(ToastPhase.Closing, feed.TakeRender().Phase);
    }

    [Fact]
    public void Real_hover_pauses_and_leave_rearms_a_full_idle_interval()
    {
        var clock = new Clock();
        var feed = clock.Feed();
        feed.Record(Result(), true);
        var id = feed.TakeRender().Card!.Id;
        clock.Advance(19);
        feed.PointerEntered(id);
        clock.Advance(50);
        feed.Record(Result(), true);
        Assert.Equal(ToastPhase.Visible, feed.TakeRender().Phase);
        feed.PointerLeft(id);
        clock.Advance(19);
        Assert.False(feed.Tick(true));
        clock.Advance(1);
        Assert.True(feed.Tick(true));
        Assert.Equal(ToastPhase.Closing, feed.TakeRender().Phase);
    }

    [Fact]
    public void Late_hover_cannot_revive_an_expired_card_and_requests_render()
    {
        var clock = new Clock();
        var feed = clock.Feed();
        feed.Record(Result(), true);
        var id = feed.TakeRender().Card!.Id;
        clock.Advance(20);
        Assert.True(feed.PointerEntered(id));
        Assert.Equal(ToastPhase.Closing, feed.TakeRender().Phase);
    }

    [Fact]
    public void Pending_without_a_window_is_restored_by_tick_with_full_counts_and_fresh_deadline()
    {
        var clock = new Clock();
        var feed = clock.Feed();
        for (var i = 0; i < 100; i++) feed.Record(Result(i != 50), false);
        Assert.True(feed.HasWork);
        Assert.Equal(ToastPhase.Hidden, feed.TakeRender().Phase);
        clock.Advance(120);
        Assert.True(feed.Tick(true));
        var render = feed.TakeRender();
        Assert.Equal(99, render.Card!.Succeeded);
        Assert.Equal(1, render.Card.Failed);
        clock.Advance(19);
        Assert.False(feed.Tick(true));
        clock.Advance(1);
        Assert.True(feed.Tick(true));
    }

    [Fact]
    public void Parking_resets_hover_and_ignores_window_close_callback()
    {
        var clock = new Clock();
        var feed = clock.Feed();
        feed.Record(Result(), true);
        var id = feed.TakeRender().Card!.Id;
        feed.PointerEntered(id);
        feed.Tick(false);
        Assert.Equal(ToastPhase.Hidden, feed.TakeRender().Phase);
        feed.CardClosed(id);
        clock.Advance(120);
        feed.Tick(true);
        Assert.Equal(id, feed.TakeRender().Card!.Id);
        clock.Advance(20);
        feed.Tick(true);
        Assert.Equal(ToastPhase.Closing, feed.TakeRender().Phase);
    }

    [Fact]
    public void Result_arriving_during_fade_starts_a_new_card_and_old_callbacks_are_ignored()
    {
        var feed = new Clock().Feed();
        feed.Record(Result(false), true);
        var oldId = feed.TakeRender().Card!.Id;
        feed.Dismiss(oldId);
        feed.TakeRender();
        feed.Record(Result(), true);
        var card = feed.TakeRender().Card!;
        Assert.NotEqual(oldId, card.Id);
        Assert.False(card.HasFailure);
        Assert.Equal(1, card.Succeeded);
        feed.CardClosed(oldId);
        Assert.False(feed.Dismiss(oldId));
        feed.PointerEntered(oldId);
        feed.PointerLeft(oldId);
        Assert.Equal(card, feed.TakeRender().Card);
    }

    [Fact]
    public void Toggle_reset_clears_pending_counts_and_allows_one_off_confirmation()
    {
        var feed = new Clock().Feed();
        feed.Record(Result(false), false);
        feed.Reset();
        feed.ShowStatus("Toast notifications disabled", "Hidden", 3, true);
        var render = feed.TakeRender();
        Assert.Equal(ToastCardKind.Status, render.Card!.Kind);
        Assert.Equal(0, render.Card.Failed);
        Assert.False(render.Card.HasFailure);
        feed.CardClosed(render.Card.Id);
        Assert.False(feed.HasWork);
        Assert.Equal(ToastPhase.Hidden, feed.TakeRender().Phase);
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    [InlineData("cancelled", true)]
    public void Explicit_task_report_replaces_activity_without_inflating_counts(string outcome, bool success)
    {
        var feed = new Clock().Feed();
        feed.Record(Result(false), true);
        var model = ToastContentBuilder.Build(new ToastEvent("report_task_result", true,
            JObject.FromObject(new { task_id = "job-1", outcome, summary = "Checked result" }), null, null, 0, true));
        feed.Record(model, false);
        Assert.Equal(ToastPhase.Hidden, feed.TakeRender().Phase);
        feed.Tick(true);
        var card = feed.TakeRender().Card!;
        Assert.Equal(ToastCardKind.TaskResult, card.Kind);
        Assert.Equal(outcome, card.Outcome);
        Assert.Equal(success, card.LatestSuccess);
        Assert.Equal("Agent reported · Checked result · job-1", card.Body);
        Assert.Equal(0, card.Succeeded);
        Assert.False(feed.ShowStatus("Agent connected", "Ready", 6, true));
        feed.Record(Result(), true);
        Assert.Equal(1, feed.TakeRender().Card!.Succeeded);
    }

    [Fact]
    public void Soft_script_failure_counts_as_failed_even_when_envelope_succeeded()
    {
        var feed = new Clock().Feed();
        feed.Record(ToastContentBuilder.Build(new ToastEvent("send_code", true,
            JObject.Parse("{\"ok\":false,\"error\":\"Script failed\"}"), null, null, 0, false)), true);
        Assert.Equal(1, feed.TakeRender().Card!.Failed);
    }

    [Fact]
    public void Parallel_producers_do_not_lose_counts()
    {
        var feed = new Clock().Feed();
        Parallel.For(0, 1000, i => feed.Record(Result(i % 2 == 0), false));
        feed.TakeRender();
        feed.Tick(true);
        var card = feed.TakeRender().Card!;
        Assert.Equal(500, card.Succeeded);
        Assert.Equal(500, card.Failed);
    }

    [Fact]
    public void Status_is_parked_under_modal_too_and_uses_its_own_lifetime()
    {
        var clock = new Clock();
        var feed = clock.Feed();
        feed.ShowStatus("Agent connected", "Ready", 6, false);
        feed.TakeRender();
        clock.Advance(100);
        feed.Tick(true);
        Assert.Equal(ToastPhase.Visible, feed.TakeRender().Phase);
        clock.Advance(6);
        Assert.Equal(ToastPhase.Closing, feed.TakeRender().Phase);
    }
}
