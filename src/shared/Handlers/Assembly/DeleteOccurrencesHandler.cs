#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// <c>delete_occurrences</c> (E3) — delete the top-level occurrences a selector matches, in one
/// undo step. The selector must carry at least one criterion (no accidental "delete everything");
/// nested matches are refused (edit the sub-assembly instead). <c>dry_run=true</c> only reports
/// what would go. The occurrences' <c>IF_&lt;name&gt;_*</c> lock planes are removed with them.
/// </summary>
public sealed class DeleteOccurrencesHandler : HandlerBase, IInventorCommand
{
    public string Name => "delete_occurrences";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var dryRun = p["dry_run"]?.Type == JTokenType.Boolean && (bool)p["dry_run"]!;
        if (!ActiveDocumentSupport.TryGetAssembly(ctx, p, Name, out var app, out var asm, out var failure))
            return failure!;
        if (!OccurrenceSelectorSpec.TryParse(p["selector"], "selector", out var spec, out var parseError, defaultLimit: 500))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, parseError!);
        if (!spec.HasCriteria)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "selector needs at least one of names/regex/file/path_contains (refusing to delete every occurrence)");

        var ac = asm.ComponentDefinition;
        if (!OccurrenceSelection.TryResolve(ac, spec, "selector", out var items, out var error))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, error!);
        var nested = items.Where(i => i.Depth > 1).Select(i => i.Path).ToList();
        if (nested.Count > 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "only top-level occurrences can be deleted from this assembly; nested matches: " + string.Join(", ", nested.Take(10))
                + ". Add max_depth:1 to the selector or edit the sub-assembly.");

        var names = new JArray(items.Select(i => i.Path));
        if (dryRun)
            return Ok(ctx, new JObject { ["dry_run"] = true, ["would_delete"] = names.Count, ["occurrences"] = names });

        Transaction? tx = null;
        try
        {
            tx = app.TransactionManager.StartTransaction((_Document)(object)asm, "MCP: delete_occurrences");
            var planes = 0;
            foreach (var it in items)
            {
                planes += OccurrencePlacement.RemoveLock(ac, it.Name);
                it.Occurrence.Delete();
            }
            tx.End();
            tx = null;
            return Ok(ctx, new JObject { ["deleted"] = names.Count, ["occurrences"] = names, ["lock_planes_removed"] = planes });
        }
        catch (Exception ex)
        {
            try { tx?.Abort(); } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR, "delete_occurrences failed (rolled back): " + ex.Message);
        }
    }
}
#endif
