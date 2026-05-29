using System.Runtime.InteropServices;

namespace Bimwright.Ipt.Plugin;

/// <summary>
/// Inventor 2025 (net8.0-windows7.0, Named Pipe) add-in entry point.
/// Thin per-version shell: a unique <see cref="GuidAttribute"/> ClientId so only the
/// matching <c>Bimwright.Ipt.Inv25.addin</c> manifest loads it, and a stable
/// <see cref="ProgIdAttribute"/>. All behaviour lives in the shared abstract base
/// <see cref="Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase"/> (owned by WS2-C).
/// </summary>
[ComVisible(true)]
[Guid("b1d25025-0000-4a25-9b25-bf1e2025c0de")]
[ProgId("Bimwright.Ipt.Plugin.Inv25")]
public sealed class InventorAddInServer : Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase
{
}
