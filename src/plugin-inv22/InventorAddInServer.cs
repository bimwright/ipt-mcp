using System.Runtime.InteropServices;

namespace Bimwright.Ipt.Plugin;

/// <summary>
/// Inventor 2022 (net48, TCP transport) add-in entrypoint. Per-version COM-visible shell with a
/// UNIQUE, FIXED <see cref="GuidAttribute"/> so only the 2022 build registers under this ClassId/
/// ClientId — the matching <c>Bimwright.Ipt.Inv22.addin</c> manifest carries the same GUID.
/// All behaviour lives in the shared <see cref="Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase"/>
/// (WS2-C); this type only supplies the version-specific COM identity.
/// </summary>
[ComVisible(true)]
[Guid("2F4F08C6-E88B-4B75-92A8-B9C52244C169")]
[ProgId("Bimwright.Ipt.Plugin.Inv22")]
public sealed class InventorAddInServer : Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase
{
}
