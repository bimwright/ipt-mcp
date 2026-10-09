#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// iLogic registrar: the <c>ilogic_*</c> wire commands plus the generic <c>list_commands</c>,
/// <c>run_command</c> and <c>undo</c>. Upsert and snippet are send-code-grade — they register
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
        if (o.EnableSendCode)
        {
            add(new ILogicUpsertRuleHandler());
            add(new ILogicRunSnippetHandler());
        }
        add(new ListCommandsHandler());
        add(new RunCommandHandler());
        add(new UndoHandler());
    }
}
#endif
