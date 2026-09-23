using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastPaletteTests
{
    private static readonly Rgb DarkCanvas = Rgb.FromHex("#2A313D");   // spike pass5, Inventor Dark canvas
    private static readonly Rgb LightBrowser = Rgb.FromHex("#F5F5F5");

    public static IEnumerable<object[]> Palettes()
    {
        yield return new object[] { ToastPalette.LightElevated };
        yield return new object[] { ToastPalette.DarkElevated };
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Text_meets_WCAG_AA_and_accents_meet_3_to_1(ToastPalette p)
    {
        Assert.True(ToastPaletteChooser.Contrast(p.Title, p.Background) >= 4.5);
        Assert.True(ToastPaletteChooser.Contrast(p.Body, p.Background) >= 4.5);
        Assert.True(ToastPaletteChooser.Contrast(p.AccentRead, p.Background) >= 3);
        Assert.True(ToastPaletteChooser.Contrast(p.AccentWrite, p.Background) >= 3);
        Assert.True(ToastPaletteChooser.Contrast(p.AccentError, p.Background) >= 3);
    }

    [Fact]
    public void Outline_contrasts_with_the_backdrop_it_is_chosen_for()
    {
        Assert.True(ToastPaletteChooser.Contrast(ToastPalette.LightElevated.Outline, DarkCanvas) >= 3);
        Assert.True(ToastPaletteChooser.Contrast(ToastPalette.DarkElevated.Outline, LightBrowser) >= 3);
    }

    [Fact]
    public void Exact_spec_colours()
    {
        Assert.Equal("#FFFFFF", ToastPalette.LightElevated.Background.Hex);
        Assert.Equal("#0F172A", ToastPalette.LightElevated.Title.Hex);
        Assert.Equal("#334155", ToastPalette.LightElevated.Body.Hex);
        Assert.Equal("#94A3B8", ToastPalette.LightElevated.Outline.Hex);
        Assert.Equal("#111827", ToastPalette.DarkElevated.Background.Hex);
        Assert.Equal("#F9FAFB", ToastPalette.DarkElevated.Title.Hex);
        Assert.Equal("#D1D5DB", ToastPalette.DarkElevated.Body.Hex);
        Assert.Equal("#475569", ToastPalette.DarkElevated.Outline.Hex);
    }

    [Fact]
    public void Dark_backdrop_sample_gives_light_toast()
    {
        var d = ToastPaletteChooser.Choose(ToastTheme.Auto, DarkCanvas, BackdropHint.None);
        Assert.Same(ToastPalette.LightElevated, d.Palette);
        Assert.StartsWith("screen sample #2A313D → light-elevated (", d.Reason);
        Assert.True(ToastPaletteChooser.Contrast(d.Palette.Background, DarkCanvas) >= 12);   // ≈13.1 measured in pass5
    }

    [Fact]
    public void Light_backdrop_sample_gives_dark_toast()
        => Assert.Same(ToastPalette.DarkElevated, ToastPaletteChooser.Choose(ToastTheme.Auto, LightBrowser, BackdropHint.None).Palette);

    [Fact]
    public void Black_sample_from_a_locked_session_falls_back_to_the_colour_scheme()   // Review Focus 5
    {
        var d = ToastPaletteChooser.Choose(ToastTheme.Auto, new Rgb(0, 0, 0), new BackdropHint(true, Rgb.FromHex("#F0F0F0")));
        Assert.Same(ToastPalette.DarkElevated, d.Palette);
        Assert.StartsWith("colour scheme #F0F0F0", d.Reason);
    }

    [Fact]
    public void No_sample_no_scheme_uses_inverse_of_theme()
    {
        var dark = ToastPaletteChooser.Choose(ToastTheme.Auto, null, new BackdropHint(true, null));
        Assert.Same(ToastPalette.LightElevated, dark.Palette);
        Assert.Equal("inverse of Dark theme", dark.Reason);
        var light = ToastPaletteChooser.Choose(ToastTheme.Auto, null, new BackdropHint(false, null));
        Assert.Same(ToastPalette.DarkElevated, light.Palette);
        Assert.Equal("inverse of Light theme", light.Reason);
    }

    [Fact]
    public void Nothing_known_defaults_to_light()
    {
        var d = ToastPaletteChooser.Choose(ToastTheme.Auto, null, BackdropHint.None);
        Assert.Same(ToastPalette.LightElevated, d.Palette);
        Assert.Equal("default", d.Reason);
    }

    [Fact]
    public void Forced_setting_wins_over_the_sample()
    {
        var d = ToastPaletteChooser.Choose(ToastTheme.Dark, DarkCanvas, BackdropHint.None);
        Assert.Same(ToastPalette.DarkElevated, d.Palette);
        Assert.Equal("forced dark (toastTheme)", d.Reason);
        Assert.Same(ToastPalette.LightElevated, ToastPaletteChooser.Choose(ToastTheme.Light, LightBrowser, BackdropHint.None).Palette);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(2, 2, 2, false)]
    [InlineData(3, 0, 0, true)]
    [InlineData(42, 49, 61, true)]
    public void Usable_sample(byte r, byte g, byte b, bool expected)
        => Assert.Equal(expected, ToastPaletteChooser.IsUsableSample(new Rgb(r, g, b)));

    [Fact]
    public void Blend_weights_the_top_colour()
        => Assert.Equal("#404040", ToastPaletteChooser.Blend(new Rgb(0, 0, 0), new Rgb(255, 255, 255), 0.75).Hex);

    [Fact]
    public void Contrast_extremes()
    {
        Assert.Equal(21.0, ToastPaletteChooser.Contrast(new Rgb(0, 0, 0), new Rgb(255, 255, 255)), 2);
        Assert.Equal(1.0, ToastPaletteChooser.Contrast(DarkCanvas, DarkCanvas), 6);
    }

    [Fact]
    public void FromHex_round_trips()
        => Assert.Equal("#2A313D", Rgb.FromHex("#2a313d").Hex);
}
