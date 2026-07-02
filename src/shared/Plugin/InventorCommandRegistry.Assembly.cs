#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Assembly;

public static partial class InventorCommandRegistry
{
    static partial void AddAssembly(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new PlaceOccurrenceHandler());
        add(new AddConstraintHandler());
        add(new CreateIMateHandler());
    }
}
#endif
