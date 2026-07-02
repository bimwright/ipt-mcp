#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Assembly;

public static partial class InventorCommandRegistry
{
    static partial void AddAssemblyQuery(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new ListInterfacesHandler());
        add(new CheckInterferenceHandler());
        add(new MeasureMinDistanceHandler());
        add(new GetAssemblyBomHandler());
        add(new ListConstraintsHandler());
    }
}
#endif
