#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Inventor.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Inventor.Shared.Infrastructure;
using Bimwright.Inventor.Shared.Handlers.Feature;

/// <summary>
/// Phase-3 WS3-B feature/work-feature registrar: registers the six feature wire commands. Only
/// compiled under an Inventor symbol (the handlers touch the API); otherwise <c>AddFeature</c> stays
/// an unimplemented no-op so the server/tests build without Inventor.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddFeature(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new ExtrudeHandler());
        add(new RevolveHandler());
        add(new FilletHandler());
        add(new ChamferHandler());
        add(new CreateWorkPlaneHandler());
        add(new CreateWorkAxisHandler());
    }
}
#endif
