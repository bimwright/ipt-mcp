#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// Phase-3 WS3-C Export registrar: <c>capture_view</c>, <c>export_step</c>, <c>export_stl</c>,
/// <c>export_dxf</c>. Implemented only under an Inventor compile symbol (the handlers touch the API);
/// without a symbol the <c>partial void AddExport</c> stays a no-op.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddExport(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new CaptureViewHandler());
        add(new ExportStepHandler());
        add(new ExportStlHandler());
        add(new ExportDxfHandler());
    }
}
#endif
