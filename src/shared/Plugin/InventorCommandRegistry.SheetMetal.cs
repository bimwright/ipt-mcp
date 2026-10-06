#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// Sheet-metal registrar: registers the <c>sm_*</c> wire commands plus <c>unwrap_try</c> and
/// <c>new_sheet_metal_part</c>. Read/write split happens server-side via the
/// <c>sheetmetal</c>/<c>sheetmetal_query</c> toolsets — every handler registers unconditionally
/// here and reports its own <see cref="IInventorCommand.IsReadOnly"/> to the dispatcher.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddSheetMetal(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new SheetMetalInfoHandler());
        add(new NewSheetMetalPartHandler());
        add(new SheetMetalFaceHandler());
        add(new SheetMetalFlangeHandler());
        add(new SheetMetalCutHandler());
        add(new SheetMetalFlatPatternHandler());
        add(new SheetMetalThicknessHandler());
        add(new UnwrapTryHandler());
    }
}
#endif
