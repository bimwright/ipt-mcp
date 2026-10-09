#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.SheetFabrication;

/// <summary>
/// Sheet Fabrication registrar: registers the <c>sheet_fabrication_*</c> wire commands plus <c>unwrap_try</c> and
/// <c>new_sheet_fabrication_part</c>. Read/write split happens server-side via the
/// <c>sheet_fabrication</c>/<c>sheet_fabrication_query</c> toolsets — every handler registers unconditionally
/// here and reports its own <see cref="IInventorCommand.IsReadOnly"/> to the dispatcher.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddSheetFabrication(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new SheetFabricationInfoHandler());
        add(new NewSheetFabricationPartHandler());
        add(new SheetFabricationFaceHandler());
        add(new SheetFabricationFlangeHandler());
        add(new SheetFabricationCutHandler());
        add(new SheetFabricationFlatPatternHandler());
        add(new SheetFabricationThicknessHandler());
        add(new UnwrapTryHandler());
    }
}
#endif
