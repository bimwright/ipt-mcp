#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Inventor.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Inventor.Shared.Infrastructure;
using Bimwright.Inventor.Shared.Handlers.Properties;

/// <summary>
/// Phase-3 WS3-A registrar: iProperty and mass-property commands.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddProperties(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new GetIPropertyHandler());
        add(new SetIPropertyHandler());
        add(new GetMassPropertiesHandler());
    }
}
#endif
