#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Drawing;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Plugin;

public static partial class InventorCommandRegistry
{
    static partial void AddDrawing(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        foreach (var name in DrawingInput.Commands) add(new DrawingCommandHandler(name));
    }
}
#endif
