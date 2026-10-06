#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// <c>measure_min_distance</c> — read-only. Legacy single measurement: flat keys
/// <c>a_occurrence/a_ref</c>, <c>b_occurrence/b_ref</c>. Batch (E4): <c>pairs[{a, b, a_ref?, b_ref?}]</c>
/// or <c>set_a × set_b</c> selectors, optionally filtered by <c>threshold_mm</c> (report only pairs at
/// or below it — clearance checks). Set mode skips pairs whose range boxes are already farther apart
/// than the threshold. Results are sorted by distance, capped at <c>max_results</c>.
/// </summary>
public sealed class MeasureMinDistanceHandler : HandlerBase, IInventorCommand
{
    public string Name => "measure_min_distance";
    public bool IsReadOnly => true;

    public const int MaxComparisons = 5000;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetAssembly(context, parameters, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        var def = assemblyDoc.ComponentDefinition;
        var isBatch = parameters["pairs"] is { Type: not JTokenType.Null } || parameters["set_a"] is { Type: not JTokenType.Null };
        if (!isBatch) return Single(context, app, def, parameters);

        double? thresholdMm = parameters["threshold_mm"]?.Type is JTokenType.Integer or JTokenType.Float ? (double)parameters["threshold_mm"]! : null;
        var maxResults = parameters.Value<int?>("max_results") ?? 200;
        if (maxResults < 1 || maxResults > 5000) return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, "max_results must be 1..5000");

        var jobs = new List<(string a, object ea, string b, object eb)>();
        var skipped = 0;
        if (parameters["pairs"] is JArray pairs)
        {
            if (parameters["set_a"] is { Type: not JTokenType.Null })
                return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, "use either pairs or set_a/set_b, not both");
            if (pairs.Count == 0 || pairs.Count > MaxComparisons)
                return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, $"pairs must hold 1..{MaxComparisons} items");
            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i] is not JObject pr) return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, $"pairs[{i}] must be an object {{a, b, a_ref?, b_ref?}}");
                if (!TryEntity(def, (string?)pr["a"] ?? "", (string?)pr["a_ref"], out var ea, out var errA))
                    return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, $"pairs[{i}].a: {errA}");
                if (!TryEntity(def, (string?)pr["b"] ?? "", (string?)pr["b_ref"], out var eb, out var errB))
                    return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, $"pairs[{i}].b: {errB}");
                jobs.Add(((string?)pr["a"] + Suffix((string?)pr["a_ref"]), ea!, (string?)pr["b"] + Suffix((string?)pr["b_ref"]), eb!));
            }
        }
        else
        {
            if (!OccurrenceSelection.TryResolve(def, parameters["set_a"], "set_a", out var setA, out var errA, defaultLimit: 2000))
                return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, errA!);
            if (!OccurrenceSelection.TryResolve(def, parameters["set_b"], "set_b", out var setB, out var errB, defaultLimit: 2000))
                return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, errB!);
            var gapCm = thresholdMm is { } t ? UnitConvert.MmToCm(t) : (double?)null;
            var seen = new HashSet<(string, string)>();
            foreach (var x in setA)
            {
                foreach (var y in setB)
                {
                    if (x.Path == y.Path) continue;
                    var key = string.CompareOrdinal(x.Path, y.Path) < 0 ? (x.Path, y.Path) : (y.Path, x.Path);
                    if (!seen.Add(key)) continue;
                    if (gapCm is { } g)
                    {
                        Box? bx = null, by = null;
                        try { bx = x.Occurrence.RangeBox; by = y.Occurrence.RangeBox; } catch { }
                        if (bx != null && by != null && !OccurrenceSelection.Overlap(bx, by, g)) { skipped++; continue; }
                    }
                    jobs.Add((x.Path, x.Occurrence, y.Path, y.Occurrence));
                    if (jobs.Count > MaxComparisons)
                        return Fail(context, InventorErrorCodes.INVALID_ARGUMENT,
                            $"set_a × set_b needs more than {MaxComparisons} measurements; narrow the selectors or add threshold_mm");
                }
            }
        }

        var rows = new List<(string a, string b, double? mm, string? error)>();
        foreach (var (a, ea, b, eb) in jobs)
        {
            try
            {
                var mm = UnitConvert.CmToMm(app.MeasureTools.GetMinimumDistance(ea, eb));
                if (thresholdMm is { } t && mm > t) continue;
                rows.Add((a, b, mm, null));
            }
            catch (Exception ex)
            {
                rows.Add((a, b, null, ex.Message));
            }
        }

        var ordered = rows.OrderBy(r => r.mm ?? double.MaxValue).ToList();
        var results = new JArray();
        foreach (var r in ordered.Take(maxResults))
        {
            var o = new JObject { ["a"] = r.a, ["b"] = r.b, ["distance_mm"] = r.mm };
            if (r.error != null) o["error"] = r.error;
            results.Add(o);
        }
        var data = new JObject
        {
            ["measured"] = jobs.Count,
            ["reported"] = results.Count,
            ["min_distance_mm"] = ordered.FirstOrDefault(r => r.mm != null).mm,
            ["results"] = results,
        };
        if (thresholdMm is { } th) data["threshold_mm"] = th;
        if (skipped > 0) data["skipped_by_bbox"] = skipped;
        if (ordered.Count > maxResults) { data["truncated"] = true; data["results_total"] = ordered.Count; }
        return Ok(context, data);
    }

    private static string Suffix(string? r) => string.IsNullOrEmpty(r) ? "" : "#" + r;

    private static bool TryEntity(AssemblyComponentDefinition def, string occ, string? refName, out object? entity, out string? error)
    {
        entity = null;
        if (string.IsNullOrEmpty(refName))
        {
            if (!AssemblyRefResolver.TryFindOccurrence(def, occ, out var o, out error)) return false;
            if (o == null) { error = "occurrence is required when ref is omitted"; return false; }
            entity = o;
            return true;
        }
        if (!AssemblyRefResolver.TryResolveInAssembly(def, occ, refName!, out var e, out error)) return false;
        entity = e;
        return true;
    }

    private InventorCommandResult Single(InventorCommandContext context, Application app, AssemblyComponentDefinition def, JObject parameters)
    {
        if (!TryEntity(def, (string?)parameters["a_occurrence"] ?? "", (string?)parameters["a_ref"], out var entityA, out var errA))
            return Fail(context, "INVALID_ARGUMENT", "Failed to resolve a: " + errA);
        if (!TryEntity(def, (string?)parameters["b_occurrence"] ?? "", (string?)parameters["b_ref"], out var entityB, out var errB))
            return Fail(context, "INVALID_ARGUMENT", "Failed to resolve b: " + errB);

        try
        {
            double distCm = app.MeasureTools.GetMinimumDistance(entityA!, entityB!);
            return Ok(context, new JObject
            {
                ["distance_mm"] = UnitConvert.CmToMm(distCm)
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Measurement failed: " + ex.Message);
        }
    }
}
#endif
