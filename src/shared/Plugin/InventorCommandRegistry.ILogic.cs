#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// iLogic registrar: the <c>ilogic_*</c> wire commands plus the generic <c>list_commands</c>,
/// <c>run_command</c> and <c>undo</c>. <c>ilogic_run_snippet</c> is send-code-grade — it registers
/// only when <c>o.EnableSendCode</c>, exactly like <c>send_code</c>, and is additionally fenced in
/// the dispatcher. All other handlers register unconditionally and report their own
/// <see cref="IInventorCommand.IsReadOnly"/>.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddILogic(Dictionary<string, IInventorCommand> d, PluginOptions o, Action<IInventorCommand> add)
    {
        add(new ILogicListRulesHandler());
        add(new ILogicGetRuleHandler());
        add(new ILogicReadLogHandler());
        add(new ILogicRunRuleHandler());
        add(new ILogicRunExternalRuleHandler());
        add(new ILogicUpsertRuleHandler());
        if (o.EnableSendCode)
            add(new ILogicRunSnippetHandler());
        add(new ListCommandsHandler());
        add(new RunCommandHandler());
        add(new UndoHandler());
    }
}
#endif
