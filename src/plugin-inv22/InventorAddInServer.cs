using System.Runtime.InteropServices;

namespace Bimwright.Inventor.Plugin;

/// <summary>
/// Inventor 2022 (net48, TCP transport) add-in entrypoint. Per-version COM-visible shell with a
/// UNIQUE, FIXED <see cref="GuidAttribute"/> so only the 2022 build registers under this ClassId/
/// ClientId — the matching <c>Bimwright.Inventor.Inv22.addin</c> manifest carries the same GUID.
/// All behaviour lives in the shared <see cref="Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase"/>
/// (WS2-C); this type only supplies the version-specific COM identity.
/// </summary>
[ComVisible(true)]
[Guid("2F4F08C6-E88B-4B75-92A8-B9C52244C169")]
[ProgId("Bimwright.Inventor.Plugin.Inv22")]
public sealed class InventorAddInServer : Bimwright.Inventor.Shared.Plugin.InventorAddInServerBase
{
}
