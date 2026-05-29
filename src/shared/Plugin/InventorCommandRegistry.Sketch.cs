#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Inventor.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Inventor.Shared.Infrastructure;
using Bimwright.Inventor.Shared.Handlers.Sketch;

/// <summary>
/// Phase-3 WS3-B sketch registrar: registers the nine sketch wire commands. Only compiled under an
/// Inventor symbol (the handlers touch the API); otherwise <c>AddSketch</c> stays an unimplemented
/// no-op so the server/tests build without Inventor.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddSketch(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new CreateSketchHandler());
        add(new ProjectGeometryHandler());
        add(new DrawLineHandler());
        add(new DrawCircleHandler());
        add(new DrawRectangleHandler());
        add(new DrawArcHandler());
        add(new AddSketchDimensionHandler());
        add(new AddSketchConstraintHandler());
        add(new CloseSketchHandler());
    }
}
#endif
