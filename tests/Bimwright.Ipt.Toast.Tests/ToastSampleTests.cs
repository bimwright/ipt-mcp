using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastSampleTests
{
    [Fact]
    public void A_covered_frame_is_not_sampled()
    {
        Assert.Equal(ToastSampleTarget.None, ToastSample.Target(frameUsable: false, paletteCommitted: false, rethemeDue: false, onScreen: false));
        Assert.Equal(ToastSampleTarget.None, ToastSample.Target(frameUsable: false, paletteCommitted: true, rethemeDue: true, onScreen: true));
    }

    [Fact]
    public void The_first_toast_on_a_usable_frame_samples_the_anchor()
        => Assert.Equal(ToastSampleTarget.Anchor, ToastSample.Target(frameUsable: true, paletteCommitted: false, rethemeDue: false, onScreen: false));

    [Fact]
    public void A_toast_born_under_a_dialog_samples_the_anchor_once_the_frame_is_usable()
        => Assert.Equal(ToastSampleTarget.Anchor, ToastSample.Target(frameUsable: true, paletteCommitted: false, rethemeDue: true, onScreen: false));

    [Fact]
    public void A_replacement_card_on_screen_before_a_palette_is_set_samples_beside_the_stack()
        => Assert.Equal(ToastSampleTarget.Beside, ToastSample.Target(frameUsable: true, paletteCommitted: false, rethemeDue: false, onScreen: true));

    [Fact]
    public void Restore_keeps_a_palette_taken_from_the_canvas()
        => Assert.Equal(ToastSampleTarget.None, ToastSample.Target(frameUsable: true, paletteCommitted: true, rethemeDue: false, onScreen: false));

    [Fact]
    public void Retheme_while_a_card_is_showing_samples_beside_the_stack()
        => Assert.Equal(ToastSampleTarget.Beside, ToastSample.Target(frameUsable: true, paletteCommitted: true, rethemeDue: true, onScreen: true));

    [Fact]
    public void Retheme_while_cards_are_hidden_samples_the_anchor()
        => Assert.Equal(ToastSampleTarget.Anchor, ToastSample.Target(frameUsable: true, paletteCommitted: true, rethemeDue: true, onScreen: false));
}
