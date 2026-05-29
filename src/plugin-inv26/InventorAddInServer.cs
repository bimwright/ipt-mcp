using System.Runtime.InteropServices;

namespace Bimwright.Inventor.Plugin;

/// <summary>
/// Inventor 2026 (net8.0-windows7.0, Named Pipe) add-in entry point.
/// Thin per-version shell: a unique <see cref="GuidAttribute"/> ClientId so only the
/// matching <c>Bimwright.Inventor.Inv26.addin</c> manifest loads it, and a stable
/// <see cref="ProgIdAttribute"/>. All behaviour lives in the shared abstract base
/// <see cref="Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase"/> (owned by WS2-C).
/// </summary>
[ComVisible(true)]
[Guid("b1d26026-0000-4a26-9b26-bf1e2026c0de")]
[ProgId("Bimwright.Inventor.Plugin.Inv26")]
public sealed class InventorAddInServer : Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase
{
}
