#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Reflection;
using InvApi = global::Inventor;
using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>
/// Inventor STA only. Reads what the toast thread needs, so the toast thread never calls Inventor COM.
/// <see cref="Read"/> is cheap (runs after every command). <see cref="ReadHint"/> costs a few more COM calls
/// and runs once, then again after an application option change.
/// </summary>
internal static class InventorUiSnapshotReader
{
    public static InventorUiSnapshot Read(InvApi.Application app, BackdropHint hint)
    {
        var visible = false;
        long main = 0, view = 0;
        try { visible = app.Visible; } catch { }
        try { main = app.MainFrameHWND; } catch { }
        try { view = app.ActiveView?.HWND ?? 0; } catch { }
        return new InventorUiSnapshot(visible, main, view, hint);
    }

    public static BackdropHint ReadHint(InvApi.Application app)
    {
        bool? dark = null;
        Rgb? scheme = null;
        try
        {
            // Late-bound: ThemeManager may not exist in older interops (spec: guard older years).
            var tm = Get(app, "ThemeManager");
            var active = tm == null ? null : Get(tm, "ActiveTheme");
            if (active != null && Get(active, "Name") is string name)
                dark = name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { }
        try
        {
            var cs = app.ActiveColorScheme;
            switch (app.ColorSchemes.BackgroundType)
            {
                case InvApi.BackgroundTypeEnum.kOneColorBackgroundType:
                    scheme = ToRgb(cs.ScreenColor);
                    break;
                case InvApi.BackgroundTypeEnum.kGradientBackgroundType:
                    // toasts sit in the upper part of the view: weight the top colour (spike pass5)
                    scheme = ToastPaletteChooser.Blend(ToRgb(cs.TopScreenColor), ToRgb(cs.BottomScreenColor), 0.75);
                    break;
                // image background: the scheme colours are not what is drawn → no hint
            }
        }
        catch { }
        return new BackdropHint(dark, scheme);
    }

    private static object? Get(object target, string property)
        => target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);

    private static Rgb ToRgb(InvApi.Color c) => new((byte)c.Red, (byte)c.Green, (byte)c.Blue);
}
#endif
