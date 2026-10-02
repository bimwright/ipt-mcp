#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// <c>place_occurrences</c> (E3) — place many components in one call and one undo step:
/// <c>items[{path, name?, pose?, lock?}]</c>. pose = <see cref="PoseSpec"/>; lock = none |
/// grounded | workplanes (<see cref="OccurrencePlacement"/>). Stops at the first failing item
/// and rolls the whole batch back unless <c>continue_on_error</c>.
/// </summary>
public sealed class PlaceOccurrencesHandler : HandlerBase, IInventorCommand
{
    public string Name => "place_occurrences";
    public bool IsReadOnly => false;

    public const int MaxItems = 200;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetAssembly(ctx, p, Name, out var app, out var asm, out var failure))
            return failure!;
        if (p["items"] is not JArray items || items.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "items must be a non-empty array of {path, name?, pose?, lock?}");
        if (items.Count > MaxItems)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"at most {MaxItems} items per call (got {items.Count})");
        var continueOnError = p["continue_on_error"]?.Type == JTokenType.Boolean && (bool)p["continue_on_error"]!;

        // Validate everything before touching the model.
        var plans = new (string path, string? name, PoseSpec pose, string lockMode)[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var field = $"items[{i}]";
            if (items[i] is not JObject it) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, field + " must be an object");
            var path = (string?)it["path"];
            if (string.IsNullOrWhiteSpace(path)) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, field + ".path is required");
            if (!System.IO.File.Exists(path)) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"{field}.path does not exist: {path}");
            if (!PoseSpec.TryParse(it["pose"], field + ".pose", out var pose, out var poseError))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, poseError!);
            if (!OccurrencePlacement.TryParseLock(it["lock"], out var lockMode, out var lockError))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, field + ": " + lockError);
            plans[i] = (path!, (string?)it["name"], pose, lockMode);
        }

        var ac = asm.ComponentDefinition;
        var results = new JArray();
        var anyFailed = false;
        Transaction? tx = null;
        try
        {
            tx = app.TransactionManager.StartTransaction((_Document)(object)asm, "MCP: place_occurrences");
            for (var i = 0; i < plans.Length; i++)
            {
                var (path, name, pose, lockMode) = plans[i];
                try
                {
                    var o = ac.Occurrences.Add(path, OccurrencePlacement.ToMatrix(app, pose));
                    if (!string.IsNullOrWhiteSpace(name)) o.Name = name;
                    var health = OccurrencePlacement.ApplyLock(ac, o, lockMode);
                    var row = new JObject
                    {
                        ["index"] = i,
                        ["ok"] = true,
                        ["name"] = o.Name,
                        ["lock"] = lockMode,
                        ["bbox_mm"] = OccurrenceSelection.BboxMm(o.RangeBox),
                    };
                    if (health.Count > 0) row["constraint_health"] = health;
                    results.Add(row);
                }
                catch (Exception ex)
                {
                    anyFailed = true;
                    results.Add(new JObject { ["index"] = i, ["ok"] = false, ["path"] = path, ["error"] = ex.Message });
                    if (!continueOnError) break;
                }
            }

            var rolledBack = anyFailed && !continueOnError;
            if (rolledBack) tx.Abort(); else tx.End();
            tx = null;
            if (!rolledBack)
            {
                try { asm.Update2(false); } catch { }
            }

            var data = new JObject
            {
                ["placed"] = rolledBack ? 0 : results.Count - Count(results, false),
                ["failed"] = Count(results, false),
                ["rolled_back"] = rolledBack,
            };
            ResponseSpillWriter.AttachResults(Name, data, results, ResponseSpillWriter.ForContext(ctx));
            return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            try { tx?.Abort(); } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR, "place_occurrences failed: " + ex.Message);
        }
    }

    private static int Count(JArray results, bool ok)
    {
        var n = 0;
        foreach (var r in results) if ((bool?)r["ok"] == ok) n++;
        return n;
    }
}
#endif
