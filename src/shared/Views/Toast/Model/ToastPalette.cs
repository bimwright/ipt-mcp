using System;
using System.Globalization;

namespace Bimwright.Ipt.Shared.Views.Toast;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        var h = hex.TrimStart('#');
        return new Rgb(
            byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public string Hex => "#" + R.ToString("X2", CultureInfo.InvariantCulture)
                             + G.ToString("X2", CultureInfo.InvariantCulture)
                             + B.ToString("X2", CultureInfo.InvariantCulture);
}

/// <summary>
/// Elevated toast palettes (spike pass4/pass5). The outline is the edge against the backdrop the
/// palette is chosen for: light toasts sit on dark backdrops, dark toasts on light ones.
/// </summary>
public sealed record ToastPalette(
    string Name, Rgb Background, Rgb Title, Rgb Body, Rgb Outline, Rgb AccentRead, Rgb AccentWrite, Rgb AccentError,
    Rgb BrandBim, Rgb BrandWright)
{
    // Brand wordmark colours come from the logo: navy "BIM" + green "wright". The logo navy is nearly
    // invisible on the dark card, so the dark palette carries brightened variants.
    public static readonly ToastPalette LightElevated = new(
        "light-elevated",
        Rgb.FromHex("#FFFFFF"), Rgb.FromHex("#0F172A"), Rgb.FromHex("#334155"), Rgb.FromHex("#94A3B8"),
        Rgb.FromHex("#007ACC"), Rgb.FromHex("#2F855A"), Rgb.FromHex("#E53E3E"),
        Rgb.FromHex("#0C3F76"), Rgb.FromHex("#589039"));

    public static readonly ToastPalette DarkElevated = new(
        "dark-elevated",
        Rgb.FromHex("#111827"), Rgb.FromHex("#F9FAFB"), Rgb.FromHex("#D1D5DB"), Rgb.FromHex("#475569"),
        Rgb.FromHex("#3B82F6"), Rgb.FromHex("#22C55E"), Rgb.FromHex("#F87171"),
        Rgb.FromHex("#5B9BD5"), Rgb.FromHex("#86C55C"));
}

/// <summary>
/// Fallback facts read on the Inventor STA: whether the UI theme is dark, and the canvas colour when the
/// colour scheme is one-colour or gradient (null for image backgrounds).
/// </summary>
public sealed record BackdropHint(bool? ThemeIsDark, Rgb? SchemeColor)
{
    public static readonly BackdropHint None = new(null, null);
}

public sealed record PaletteDecision(ToastPalette Palette, string Reason);

/// <summary>
/// Spike decision: the palette is the inverse of what is actually behind the toast (screen sample).
/// The fallbacks are the colour scheme, then the inverse of the UI theme, then light.
/// </summary>
public static class ToastPaletteChooser
{
    public static PaletteDecision Choose(ToastTheme setting, Rgb? sample, BackdropHint hint)
    {
        if (setting == ToastTheme.Light) return new PaletteDecision(ToastPalette.LightElevated, "forced light (toastTheme)");
        if (setting == ToastTheme.Dark) return new PaletteDecision(ToastPalette.DarkElevated, "forced dark (toastTheme)");
        if (sample is { } s && IsUsableSample(s)) return ForBackdrop(s, "screen sample");
        if (hint.SchemeColor is { } c) return ForBackdrop(c, "colour scheme");
        if (hint.ThemeIsDark is { } dark)
            return dark
                ? new PaletteDecision(ToastPalette.LightElevated, "inverse of Dark theme")
                : new PaletteDecision(ToastPalette.DarkElevated, "inverse of Light theme");
        return new PaletteDecision(ToastPalette.LightElevated, "default");
    }

    /// <summary>Pure black means the capture saw a locked or minimized session, not the canvas.</summary>
    public static bool IsUsableSample(Rgb c) => c.R > 2 || c.G > 2 || c.B > 2;

    public static double Luminance(Rgb c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static Rgb Blend(Rgb top, Rgb bottom, double topWeight)
    {
        byte Mix(byte t, byte b) => (byte)Math.Round(t * topWeight + b * (1 - topWeight), MidpointRounding.AwayFromZero);
        return new Rgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static PaletteDecision ForBackdrop(Rgb backdrop, string how)
    {
        var light = ToastPalette.LightElevated;
        var dark = ToastPalette.DarkElevated;
        var cl = Contrast(light.Background, backdrop);
        var cd = Contrast(dark.Background, backdrop);
        var pick = cl >= cd ? light : dark;
        var ratio = Math.Max(cl, cd).ToString("0.0", CultureInfo.InvariantCulture);
        return new PaletteDecision(pick, $"{how} {backdrop.Hex} → {pick.Name} ({ratio}:1)");
    }

    private static double Channel(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
