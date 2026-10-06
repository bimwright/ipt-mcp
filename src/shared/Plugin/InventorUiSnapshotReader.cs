#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using InvApi = global::Inventor;
using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>
/// Inventor STA only. Reads what the toast thread needs, so the toast thread never calls Inventor COM.
/// <see cref="Read"/> is cheap (runs after every command).
/// </summary>
internal static class InventorUiSnapshotReader
{
    public static InventorUiSnapshot Read(InvApi.Application app)
    {
        var visible = false;
        long main = 0, view = 0;
        try { visible = app.Visible; } catch { }
        try { main = app.MainFrameHWND; } catch { }
        try { view = app.ActiveView?.HWND ?? 0; } catch { }
        return new InventorUiSnapshot(visible, main, view);
    }
}
#endif
