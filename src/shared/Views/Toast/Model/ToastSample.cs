namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>Where a backdrop sample is taken. <see cref="None"/> means the frame is covered or the stack already has a palette.</summary>
public enum ToastSampleTarget
{
    None,
    Anchor,
    Beside,
}

/// <summary>
/// A sample is the colour behind the toast. A modal dialog or a minimized frame covers that spot,
/// so the stack waits and takes the sample when the frame is usable again.
/// </summary>
public static class ToastSample
{
    public static ToastSampleTarget Target(bool frameUsable, bool paletteCommitted, bool rethemeDue, bool onScreen)
    {
        if (!frameUsable) return ToastSampleTarget.None;
        // A painted card must never be its own backdrop: whenever one is on screen the sample is
        // taken beside the stack, not at the anchor the card now covers.
        if (!paletteCommitted || rethemeDue) return onScreen ? ToastSampleTarget.Beside : ToastSampleTarget.Anchor;
        return ToastSampleTarget.None;
    }
}
