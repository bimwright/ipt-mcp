using System.Runtime.InteropServices;

namespace Bimwright.Ipt.Plugin;

/// <summary>
/// Inventor 2024 (net48, TCP transport) add-in entrypoint. Per-version COM-visible shell with a
/// UNIQUE, FIXED <see cref="GuidAttribute"/> so only the 2024 build registers under this ClassId/
/// ClientId — the matching <c>Bimwright.Ipt.Inv24.addin</c> manifest carries the same GUID.
/// All behaviour lives in the shared <see cref="Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase"/>
/// (WS2-C); this type only supplies the version-specific COM identity.
/// </summary>
[ComVisible(true)]
[Guid("B562FC6E-F594-44E2-B1B8-561ABE81BF2C")]
[ProgId("Bimwright.Ipt.Plugin.Inv24")]
public sealed class InventorAddInServer : Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase
{
}
