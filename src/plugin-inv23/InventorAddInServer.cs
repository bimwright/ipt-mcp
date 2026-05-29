using System.Runtime.InteropServices;

namespace Bimwright.Inventor.Plugin;

/// <summary>
/// Inventor 2023 (net48, TCP transport) add-in entrypoint. Per-version COM-visible shell with a
/// UNIQUE, FIXED <see cref="GuidAttribute"/> so only the 2023 build registers under this ClassId/
/// ClientId — the matching <c>Bimwright.Inventor.Inv23.addin</c> manifest carries the same GUID.
/// All behaviour lives in the shared <see cref="Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase"/>
/// (WS2-C); this type only supplies the version-specific COM identity.
/// </summary>
[ComVisible(true)]
[Guid("E6E68FDF-601C-4F25-98C9-A814A3FC6F01")]
[ProgId("Bimwright.Inventor.Plugin.Inv23")]
public sealed class InventorAddInServer : Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase
{
}
