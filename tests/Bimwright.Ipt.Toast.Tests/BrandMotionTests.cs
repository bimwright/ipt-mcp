using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

/// <summary>
/// Pins the toast-brand-v1 contract (docs/toast-brand-roadmap.md §3): the wordmark wipe must
/// match rvt-mcp's numbers exactly. These tokens feed the WPF window; drift here is a bug.
/// </summary>
public sealed class BrandMotionTests
{
    [Fact]
    public void Alpha_profile_is_dim_rest_full_crest_settled_end()
    {
        Assert.Equal(0.3, BrandMotion.RestOpacity);
        Assert.Equal(1.0, BrandMotion.CrestOpacity);
        Assert.Equal(0.8, BrandMotion.SettleOpacity);
    }

    [Fact]
    public void Entrance_delay_waits_for_the_eye_hover_replay_does_not()
    {
        Assert.Equal(1300, BrandMotion.EntranceDelayMs);
        Assert.Equal(150, BrandMotion.HoverDelayMs);
        Assert.Equal(800, BrandMotion.SweepMs);
    }

    [Fact]
    public void Sweep_covers_the_full_wordmark_left_to_right()
    {
        Assert.Equal(-0.75, BrandMotion.SweepFrom);
        Assert.Equal(0.75, BrandMotion.SweepTo);
    }

    [Fact]
    public void Brand_mask_settles_behind_the_crest_and_rests_ahead()
    {
        Assert.Equal(
            new[] { (0.00, 0.8), (0.32, 0.8), (0.44, 1.0), (0.58, 0.3), (1.00, 0.3) },
            BrandMotion.BrandStops);
    }

    [Fact]
    public void Shine_band_peak_rides_the_brand_crest()
    {
        var crest = BrandMotion.BrandStops.MaxBy(s => s.Alpha).Offset;
        var peak = BrandMotion.ShineStops.MaxBy(s => s.Alpha).Offset;

        Assert.Equal(0.44, crest);
        Assert.Equal(crest, peak);
        Assert.Equal(0.0, BrandMotion.ShineStops.First().Alpha);
        Assert.Equal(0.0, BrandMotion.ShineStops.Last().Alpha);
    }

    [Fact]
    public void Glint_tint_blends_brand_colour_over_white()
        => Assert.Equal(0.55, BrandMotion.ShineBlendWeight);

    [Fact]
    public void Wordmark_size_is_pinned_for_both_layers()
        => Assert.Equal(10, BrandMotion.FontSizeDip);
}
