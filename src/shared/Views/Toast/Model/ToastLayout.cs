using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Views.Toast;

public readonly record struct PxPoint(int X, int Y);

public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Placement in physical pixels. Sizes are DIPs scaled by the Inventor main frame's DPI (spike: the
/// process is system-DPI-aware).
/// </summary>
public static class ToastLayout
{
    // 300-DIP card plus 8-DIP shadow space on either side.
    public const double CardWidthDip = 316;
    public const double EdgeDip = 16;
    public const double HomeTopDip = 150;
    public const double GapDip = 4;
    public const double MinViewHeightDip = 120;
    public const int MaxToasts = 1;

    public static int Px(double dip, uint dpi)
        => (int)Math.Round(dip * (dpi == 0 ? 96 : dpi) / 96.0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Over the graphics view (top-left + 16 DIP) when it is big enough to hold a card. Otherwise, e.g.
    /// on the Home page, 16 DIP from the frame's left and 150 DIP down, below the ribbon and Quick Access Toolbar.
    /// </summary>
    public static PxPoint Anchor(PxRect? view, PxRect main, uint dpi)
    {
        var edge = Px(EdgeDip, dpi);
        if (view is { } v && !v.IsEmpty
            && v.Width >= Px(CardWidthDip, dpi) + 2 * edge
            && v.Height >= Px(MinViewHeightDip, dpi))
            return new PxPoint(v.Left + edge, v.Top + edge);
        return new PxPoint(main.Left + edge, main.Top + Px(HomeTopDip, dpi));
    }

    /// <summary>Caller supplies priority order, top to bottom.</summary>
    public static IReadOnlyList<PxPoint> Stack(PxPoint anchor, IReadOnlyList<int> heightsPx, uint dpi)
    {
        var gap = Px(GapDip, dpi);
        var y = anchor.Y;
        var list = new List<PxPoint>(heightsPx.Count);
        foreach (var h in heightsPx)
        {
            list.Add(new PxPoint(anchor.X, y));
            y += Math.Max(0, h) + gap;
        }
        return list;
    }

    public static PxRect? Union(IEnumerable<PxRect> rects)
    {
        PxRect? acc = null;
        foreach (var r in rects)
            acc = acc is { } a
                ? new PxRect(Math.Min(a.Left, r.Left), Math.Min(a.Top, r.Top), Math.Max(a.Right, r.Right), Math.Max(a.Bottom, r.Bottom))
                : r;
        return acc;
    }
}

/// <summary>Read-only Win32 facts about the Inventor main frame, sampled on the toast thread.</summary>
public sealed record HostWindowState(bool Exists, bool Visible, bool Iconic, bool Enabled);

public static class ToastVisibility
{
    /// <summary>
    /// Owner emulation (toasts are unowned). Show only over a usable Inventor frame. Hide while it is gone,
    /// hidden or minimized, or disabled by a modal dialog, so toasts never cover dialog buttons (D1).
    /// </summary>
    public static bool ShouldShow(HostWindowState s) => s.Exists && s.Visible && !s.Iconic && s.Enabled;

    /// <summary>No toast at all for invisible Inventor (automation, Inventor Server) or before the frame exists.</summary>
    public static bool ShouldCreate(bool appVisible, long mainHwnd) => appVisible && mainHwnd != 0;

    /// <summary>
    /// Notify-time gate. The snapshot's AppVisible may be stale, so the live frame must still exist and be
    /// visible. A minimized or modal-blocked frame still gets a card: it is held hidden and shown once the
    /// frame is usable again (<see cref="ShouldShow"/> decides when, not whether).
    /// </summary>
    public static bool ShouldCreate(bool appVisible, long mainHwnd, HostWindowState live)
        => ShouldCreate(appVisible, mainHwnd) && live.Exists && live.Visible;
}

/// <summary>What the toast thread knows about Inventor. Read on the Inventor STA after each command.</summary>
public sealed record InventorUiSnapshot(bool AppVisible, long MainHwnd, long ViewHwnd)
{
    public static readonly InventorUiSnapshot Empty = new(false, 0, 0);
}
