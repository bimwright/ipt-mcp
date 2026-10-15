#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Handlers.IPart;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Plugin;

public static partial class InventorCommandRegistry
{
    static partial void AddIPart(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        foreach (var name in new[] { "get_ipart_info", "create_ipart_factory", "update_ipart_table", "set_ipart_row",
            "generate_ipart_members", "sync_ipart_excel", "place_ipart_member", "change_ipart_member" }) add(new StandardIPartHandler(name));
    }
}
#endif
