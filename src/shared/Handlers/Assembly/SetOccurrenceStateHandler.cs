#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// <c>set_occurrence_state</c> (E3) — apply visible / suppressed / grounded / pose / lock to every
/// occurrence a selector matches, in one undo step. <c>visible</c> works at any depth (in the top
/// assembly's context); suppressed, grounded, pose and lock need top-level occurrences. A pose
/// change on a workplane-locked occurrence re-creates its lock planes at the new pose.
/// <c>dry_run=true</c> lists the matches without changing anything.
/// </summary>
public sealed class SetOccurrenceStateHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_occurrence_state";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetAssembly(ctx, p, Name, out var app, out var asm, out var failure))
            return failure!;

        bool? visible = Bool(p, "visible"), suppressed = Bool(p, "suppressed"), grounded = Bool(p, "grounded");
        var hasPose = p["pose"] is { Type: not JTokenType.Null };
        if (!PoseSpec.TryParse(p["pose"], "pose", out var pose, out var poseError))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, poseError!);
        string? lockMode = null;
        if (p["lock"] is { Type: not JTokenType.Null } lt)
        {
            if (!OccurrencePlacement.TryParseLock(lt, out var lm, out var lockError))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, lockError!);
            lockMode = lm;
        }
        if (visible is null && suppressed is null && grounded is null && !hasPose && lockMode is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "nothing to change: pass visible, suppressed, grounded, pose and/or lock");
        if (grounded is not null && lockMode is not null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "use either grounded or lock, not both");
        var dryRun = p["dry_run"]?.Type == JTokenType.Boolean && (bool)p["dry_run"]!;

        // Suppressed occurrences must be selectable to un-suppress them.
        var selector = p["selector"];
        if (suppressed == false)
        {
            var so = selector switch
            {
                JObject o => (JObject)o.DeepClone(),
                JArray or JValue { Type: JTokenType.String } => new JObject { ["names"] = selector.DeepClone() },
                _ => new JObject(),
            };
            if (so["include_suppressed"] is null) so["include_suppressed"] = true;
            selector = so;
        }
        var ac = asm.ComponentDefinition;
        if (!OccurrenceSelection.TryResolve(ac, selector, "selector", out var items, out var error, defaultLimit: 500))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, error!);

        var topOnly = suppressed is not null || grounded is not null || hasPose || lockMode is not null;
        var nested = items.Where(i => i.Depth > 1).Select(i => i.Path).ToList();
        if (topOnly && nested.Count > 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "suppressed, grounded, pose and lock apply to top-level occurrences only; nested matches: " + string.Join(", ", nested.Take(10))
                + ". Add max_depth:1 or edit the sub-assembly.");

        if (dryRun)
            return Ok(ctx, new JObject { ["dry_run"] = true, ["would_change"] = items.Count, ["occurrences"] = new JArray(items.Select(i => i.Path)) });

        var results = new JArray();
        Transaction? tx = null;
        try
        {
            tx = app.TransactionManager.StartTransaction((_Document)(object)asm, "MCP: set_occurrence_state");
            foreach (var it in items)
            {
                var o = it.Occurrence;
                var row = new JObject { ["path"] = it.Path };
                if (suppressed == true && !o.Suppressed) o.Suppress();
                if (suppressed == false && o.Suppressed) o.Unsuppress();
                if (visible is { } v && !o.Suppressed) o.Visible = v;
                if (hasPose || lockMode is not null)
                {
                    var hadLock = OccurrencePlacement.HasLock(ac, it.Name);
                    var mode = lockMode ?? (hadLock ? "workplanes" : null);
                    if (hadLock) OccurrencePlacement.RemoveLock(ac, it.Name);
                    if (hasPose)
                    {
                        o.Grounded = false;
                        o.Transformation = OccurrencePlacement.ToMatrix(app, pose);
                    }
                    if (mode is not null)
                    {
                        var health = OccurrencePlacement.ApplyLock(ac, o, mode);
                        row["lock"] = mode;
                        if (health.Count > 0) row["constraint_health"] = health;
                    }
                }
                if (grounded is { } g) o.Grounded = g;
                row["suppressed"] = o.Suppressed;
                if (!o.Suppressed)
                {
                    row["visible"] = o.Visible;
                    if (it.Depth == 1) row["grounded"] = o.Grounded;
                }
                results.Add(row);
            }
            tx.End();
            tx = null;
            try { asm.Update2(false); } catch { }
            var data = new JObject { ["changed"] = results.Count };
            ResponseSpillWriter.AttachResults(Name, data, results);
            return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            try { tx?.Abort(); } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR, "set_occurrence_state failed (rolled back): " + ex.Message);
        }
    }

    private static bool? Bool(JObject p, string key) => p[key]?.Type == JTokenType.Boolean ? (bool)p[key]! : null;
}
#endif
