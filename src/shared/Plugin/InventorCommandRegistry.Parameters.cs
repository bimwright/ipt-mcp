#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Parameters;

/// <summary>
/// Phase-3 WS3-A registrar: model/user parameter commands.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddParameters(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new ListParametersHandler());
        add(new GetParameterHandler());
        add(new SetParameterHandler());
        add(new CreateParameterHandler());
    }
}
#endif
