using System.Runtime.InteropServices;

namespace Bimwright.Ipt.Plugin;

/// <summary>
/// Inventor 2027 (net10.0-windows7.0, Named Pipe) add-in entry point.
/// Thin per-version shell: a unique <see cref="GuidAttribute"/> ClientId so only the
/// matching <c>Bimwright.Ipt.Inv27.addin</c> manifest loads it, and a stable
/// <see cref="ProgIdAttribute"/>. All behaviour lives in the shared abstract base
/// <see cref="Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase"/> (owned by WS2-C).
/// 2027 is the version where <c>UseInventorAssemblyContext=0</c> (isolated dependency
/// loading) is honoured; see <c>Bimwright.Ipt.Inv27.addin</c>.
/// </summary>
[ComVisible(true)]
[Guid("b1d27027-0000-4a27-9b27-bf1e2027c0de")]
[ProgId("Bimwright.Ipt.Plugin.Inv27")]
public sealed class InventorAddInServer : Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase
{
}
