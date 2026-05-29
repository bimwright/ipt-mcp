#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Inventor.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Inventor.Shared.Infrastructure;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Handlers.Code;

/// <summary>
/// Phase-3 WS3-C Platform registrar: the framework escape hatch + ToolBaker runtime. <c>send_code</c> is
/// registered only when the add-in opted in (<c>o.EnableSendCode</c>); without it the command is simply
/// absent and the dispatcher returns <c>SEND_CODE_DISABLED</c>. <c>run_baked_tool</c> and <c>apply_bake</c>
/// are always registered (they re-check the dispatch authorizer at run time). Implemented only under an
/// Inventor compile symbol (the handlers touch the API); without a symbol the <c>partial void
/// AddPlatform</c> stays a no-op.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddPlatform(Dictionary<string, IInventorCommand> d, PluginOptions o, Action<IInventorCommand> add)
    {
        if (o.EnableSendCode)
            add(new SendCodeHandler());

        add(new RunBakedToolHandler());
        add(new ApplyBakeHandler());
    }
}
#endif
