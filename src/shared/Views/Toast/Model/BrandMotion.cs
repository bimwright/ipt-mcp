namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Host-free tokens for the wordmark wipe — the one place the animation's numbers live.
/// Contract: docs/toast-brand-roadmap.md §3 (toast-brand-v1). The WPF window consumes these;
/// the model tests pin them so the effect cannot drift silently.
/// </summary>
public static class BrandMotion
{
    /// <summary>Wordmark alpha while the wipe is still ahead of the text.</summary>
    public const double RestOpacity = 0.3;
    /// <summary>Wordmark alpha once the wipe has passed.</summary>
    public const double SettleOpacity = 0.8;
    /// <summary>Alpha at the moving crest — the visible wave front.</summary>
    public const double CrestOpacity = 1.0;

    /// <summary>First pass waits for the reader's eye to land on a fresh toast.</summary>
    public const int EntranceDelayMs = 1300;
    /// <summary>Hover replay runs almost immediately — the eye is already there.</summary>
    public const int HoverDelayMs = 150;
    /// <summary>Sweep duration, shared by the brand mask and the glint band.</summary>
    public const int SweepMs = 800;

    /// <summary>Both opacity masks translate −0.75 → +0.75 relative units, left to right.</summary>
    public const double SweepFrom = -0.75;
    public const double SweepTo = 0.75;

    /// <summary>Brand mask stops (offset, alpha): settled behind the crest, rest ahead of it.</summary>
    public static readonly (double Offset, double Alpha)[] BrandStops =
    {
        (0.00, SettleOpacity), (0.32, SettleOpacity), (0.44, CrestOpacity), (0.58, RestOpacity), (1.00, RestOpacity),
    };

    /// <summary>Glint band stops — its peak rides the brand crest at 0.44.</summary>
    public static readonly (double Offset, double Alpha)[] ShineStops =
    {
        (0.00, 0.0), (0.36, 0.0), (0.44, 1.0), (0.52, 0.0), (1.00, 0.0),
    };

    /// <summary>Glint tint = this much brand colour blended over white.</summary>
    public const double ShineBlendWeight = 0.55;

    /// <summary>Wordmark size in dip. Both layers must use it or the glint drifts off the letters.</summary>
    public const double FontSizeDip = 10;
}
