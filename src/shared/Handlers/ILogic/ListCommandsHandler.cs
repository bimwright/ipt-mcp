#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>list_commands</c> — read-only enumeration of Inventor's live
/// <c>CommandManager.ControlDefinitions</c> (internal names usable with <c>run_command</c>).
/// <c>filter</c> is a case-insensitive substring match on internal/display name,
/// <c>max_items</c> caps output (default 200) and <c>truncated</c> reports overflow.
/// </summary>
public sealed class ListCommandsHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_commands";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        int maxItems;
        try { maxItems = (int?)p["max_items"] ?? 200; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_items must be an integer"); }
        if (maxItems <= 0) maxItems = 200;
        var filter = (p["filter"]?.Type == JTokenType.String) ? ((string)p["filter"]!).Trim() : null;

        try
        {
            var arr = new JArray();
            var total = 0;
            var defs = app.CommandManager.ControlDefinitions;
            for (var i = 1; i <= defs.Count; i++)
            {
                ControlDefinition def;
                string internalName, displayName, typeName;
                try
                {
                    def = defs[i];
                    internalName = def.InternalName;
                    displayName = def.DisplayName;
                    typeName = def.Type.ToString();
                }
                catch { continue; }

                if (filter is not null
                    && internalName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                total++;
                if (arr.Count >= maxItems) continue;
                arr.Add(new JObject
                {
                    ["internal_name"] = internalName,
                    ["display_name"] = displayName,
                    ["type"] = typeName,
                });
            }

            return Ok(ctx, new JObject
            {
                ["commands"] = arr,
                ["total"] = total,
                ["truncated"] = total > arr.Count,
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
