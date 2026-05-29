using System.Runtime.InteropServices;

namespace Bimwright.Inventor.Plugin;

/// <summary>
/// Inventor 2024 (net48, TCP transport) add-in entrypoint. Per-version COM-visible shell with a
/// UNIQUE, FIXED <see cref="GuidAttribute"/> so only the 2024 build registers under this ClassId/
/// ClientId — the matching <c>Bimwright.Inventor.Inv24.addin</c> manifest carries the same GUID.
/// All behaviour lives in the shared <see cref="Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase"/>
/// (WS2-C); this type only supplies the version-specific COM identity.
/// </summary>
[ComVisible(true)]
[Guid("B562FC6E-F594-44E2-B1B8-561ABE81BF2C")]
[ProgId("Bimwright.Inventor.Plugin.Inv24")]
public sealed class InventorAddInServer : Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase
{
}
