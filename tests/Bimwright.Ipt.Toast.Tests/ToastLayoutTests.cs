using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastLayoutTests
{
    private static readonly PxRect Main = new(0, 0, 1920, 1080);

    [Fact]
    public void Anchor_over_the_view_at_100_percent()
        => Assert.Equal(new PxPoint(116, 216), ToastLayout.Anchor(new PxRect(100, 200, 1100, 900), Main, 96));

    [Fact]
    public void Anchor_over_the_view_at_200_percent()
        => Assert.Equal(new PxPoint(132, 232), ToastLayout.Anchor(new PxRect(100, 200, 1100, 900), Main, 192));

    [Fact]
    public void Home_page_anchors_below_the_ribbon()
    {
        Assert.Equal(new PxPoint(16, 150), ToastLayout.Anchor(null, Main, 96));
        Assert.Equal(new PxPoint(32, 300), ToastLayout.Anchor(null, Main, 192));
    }

    [Fact]
    public void Too_small_or_empty_view_falls_back_to_the_frame()
    {
        Assert.Equal(new PxPoint(16, 150), ToastLayout.Anchor(new PxRect(0, 0, 300, 900), Main, 96));   // narrower than card + edges
        Assert.Equal(new PxPoint(16, 150), ToastLayout.Anchor(new PxRect(0, 0, 1000, 100), Main, 96));  // shorter than 120 DIP
        Assert.Equal(new PxPoint(16, 150), ToastLayout.Anchor(new PxRect(5, 5, 5, 5), Main, 96));
    }

    [Fact]
    public void Dpi_zero_is_treated_as_96()
        => Assert.Equal(16, ToastLayout.Px(16, 0));

    [Fact]
    public void Stack_goes_down_with_gaps()
    {
        var p = ToastLayout.Stack(new PxPoint(16, 150), new[] { 100, 80, 120 }, 96);
        Assert.Equal(new[] { new PxPoint(16, 150), new PxPoint(16, 254), new PxPoint(16, 338) }, p);
    }

    [Fact]
    public void Stack_gap_scales_with_dpi_and_ignores_negative_heights()
    {
        var p = ToastLayout.Stack(new PxPoint(0, 0), new[] { -5, 10 }, 192);
        Assert.Equal(new[] { new PxPoint(0, 0), new PxPoint(0, 8) }, p);
    }

    [Fact]
    public void Sample_rect_is_card_sized()
        => Assert.Equal(new PxRect(16, 150, 332, 230), ToastLayout.SampleRect(new PxPoint(16, 150), 96));

    [Fact]
    public void Strip_is_just_right_of_the_stack()
        => Assert.Equal(new PxRect(364, 150, 420, 400), ToastLayout.StripRightOf(new PxRect(16, 150, 360, 400), 96));

    [Fact]
    public void Union_of_rects()
    {
        Assert.Equal(new PxRect(10, 20, 300, 500), ToastLayout.Union(new[] { new PxRect(10, 20, 300, 100), new PxRect(15, 110, 290, 500) }));
        Assert.Null(ToastLayout.Union(Array.Empty<PxRect>()));
    }

    [Theory]
    [InlineData(true, true, false, true, true)]    // normal
    [InlineData(false, true, false, true, false)]  // frame gone
    [InlineData(true, false, false, true, false)]  // hidden
    [InlineData(true, true, true, true, false)]    // minimized
    [InlineData(true, true, false, false, false)]  // modal dialog open (D1)
    public void Show_only_over_a_usable_frame(bool exists, bool visible, bool iconic, bool enabled, bool expected)
        => Assert.Equal(expected, ToastVisibility.ShouldShow(new HostWindowState(exists, visible, iconic, enabled)));

    [Theory]
    [InlineData(true, 1234L, true)]
    [InlineData(false, 1234L, false)]  // invisible Inventor / automation
    [InlineData(true, 0L, false)]      // no main frame yet
    public void Create_only_for_visible_Inventor(bool appVisible, long main, bool expected)
        => Assert.Equal(expected, ToastVisibility.ShouldCreate(appVisible, main));

    [Theory]
    [InlineData(true, true, true, false, true, true)]     // usable frame
    [InlineData(true, true, true, true, true, true)]      // minimized: held hidden, shown on restore
    [InlineData(true, true, true, false, false, true)]    // modal dialog: held hidden, shown when it closes
    [InlineData(true, true, false, false, true, false)]   // frame hidden since the snapshot: stale AppVisible
    [InlineData(true, false, false, false, false, false)] // frame destroyed
    [InlineData(false, true, true, false, true, false)]   // snapshot says invisible Inventor
    public void Notify_holds_a_card_for_a_minimized_or_modal_frame_but_not_a_hidden_one(
        bool appVisible, bool exists, bool visible, bool iconic, bool enabled, bool expected)
        => Assert.Equal(expected, ToastVisibility.ShouldCreate(appVisible, 1234L, new HostWindowState(exists, visible, iconic, enabled)));

    [Fact]
    public void Empty_snapshot_creates_nothing()
        => Assert.False(ToastVisibility.ShouldCreate(InventorUiSnapshot.Empty.AppVisible, InventorUiSnapshot.Empty.MainHwnd));
}
