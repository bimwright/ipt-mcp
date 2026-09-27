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
/// <c>check_interference</c> — read-only. Legacy form: <c>occurrences</c> (names; null = all
/// top-level) analysed against each other. v2 (E4): <c>set_a</c> [+ <c>set_b</c>] occurrence
/// selectors (leaf parts, globs, file filters — nested matches are analysed in context); with
/// set_b only A×B pairs are reported. <c>bbox_prefilter</c> (default true) drops members whose
/// range box touches nothing on the other side before Inventor's analysis. Pairs are sorted by
/// volume and capped at <c>max_pairs</c> (pairs_total / truncated report the rest).
/// </summary>
public sealed class CheckInterferenceHandler : HandlerBase, IInventorCommand
{
    public string Name => "check_interference";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetAssembly(context, parameters, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        var def = assemblyDoc.ComponentDefinition;
        var maxPairs = parameters.Value<int?>("max_pairs") ?? 200;
        if (maxPairs < 1 || maxPairs > 5000) return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, "max_pairs must be 1..5000");
        var prefilter = parameters["bbox_prefilter"]?.Type != JTokenType.Boolean || (bool)parameters["bbox_prefilter"]!;

        List<ComponentOccurrence> setA;
        List<ComponentOccurrence>? setB = null;
        var hasV2 = parameters["set_a"] is { Type: not JTokenType.Null } || parameters["set_b"] is { Type: not JTokenType.Null };
        if (hasV2)
        {
            if (parameters["occurrences"] is JArray { Count: > 0 })
                return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, "use either occurrences (legacy) or set_a/set_b, not both");
            if (!OccurrenceSelection.TryResolve(def, parameters["set_a"], "set_a", out var a, out var errA, defaultLimit: 2000))
                return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, errA!);
            setA = a.Select(i => i.Occurrence).ToList();
            if (parameters["set_b"] is { Type: not JTokenType.Null } sb)
            {
                if (!OccurrenceSelection.TryResolve(def, sb, "set_b", out var b, out var errB, defaultLimit: 2000))
                    return Fail(context, InventorErrorCodes.INVALID_ARGUMENT, errB!);
                setB = b.Select(i => i.Occurrence).ToList();
            }
        }
        else
        {
            setA = new List<ComponentOccurrence>();
            var occurrencesParam = parameters["occurrences"] as JArray;
            if (occurrencesParam == null || occurrencesParam.Count == 0)
            {
                foreach (ComponentOccurrence o in def.Occurrences) setA.Add(o);
            }
            else
            {
                foreach (var tok in occurrencesParam)
                {
                    string name = (string?)tok ?? "";
                    if (!AssemblyRefResolver.TryFindOccurrence(def, name, out var occ, out var err))
                        return Fail(context, "INVALID_ARGUMENT", err!);
                    if (occ != null) setA.Add(occ);
                }
            }
        }

        try
        {
            var candidatesA = setA.Count;
            var candidatesB = setB?.Count;
            if (prefilter && setB != null)
            {
                var boxesA = setA.Select(o => (o, box: SafeBox(o))).Where(x => x.box != null).ToList();
                var boxesB = setB.Select(o => (o, box: SafeBox(o))).Where(x => x.box != null).ToList();
                setA = boxesA.Where(x => boxesB.Any(y => OccurrenceSelection.Overlap(x.box!, y.box!, 0))).Select(x => x.o).ToList();
                setB = boxesB.Where(y => boxesA.Any(x => OccurrenceSelection.Overlap(x.box!, y.box!, 0))).Select(y => y.o).ToList();
            }

            if (setA.Count == 0 || (setB != null && setB.Count == 0) || (setB == null && setA.Count < 2))
                return Ok(context, Result(new Dictionary<(string, string), double>(), 0, 0, maxPairs, candidatesA, candidatesB, setA.Count, setB?.Count));

            var collA = app.TransientObjects.CreateObjectCollection();
            foreach (var o in setA) collA.Add(o);
            InterferenceResults results;
            if (setB != null)
            {
                var collB = app.TransientObjects.CreateObjectCollection();
                foreach (var o in setB) collB.Add(o);
                results = def.AnalyzeInterference(collA, collB);
            }
            else
            {
                results = def.AnalyzeInterference(collA);
            }

            int rawBodies = results.Count;
            var aggregated = new Dictionary<(string, string), double>();
            double totalVolCm3 = 0;
            for (int i = 1; i <= rawBodies; i++)
            {
                InterferenceResult res = results[i];
                string name1 = PathOf(res.OccurrenceOne);
                string name2 = PathOf(res.OccurrenceTwo);
                double volCm3 = res.Volume;
                totalVolCm3 += volCm3;

                // Sort names alphabetically to aggregate symmetrically
                var key = string.Compare(name1, name2, StringComparison.OrdinalIgnoreCase) <= 0 ? (name1, name2) : (name2, name1);
                aggregated[key] = aggregated.TryGetValue(key, out var v) ? v + volCm3 : volCm3;
            }

            return Ok(context, Result(aggregated, rawBodies, totalVolCm3, maxPairs, candidatesA, candidatesB, setA.Count, setB?.Count));
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Interference check failed: " + ex.Message);
        }
    }

    private static JObject Result(Dictionary<(string, string), double> aggregated, int rawBodies, double totalVolCm3, int maxPairs,
        int candidatesA, int? candidatesB, int analysedA, int? analysedB)
    {
        var sorted = aggregated.OrderByDescending(k => k.Value).ToList();
        var pairsArr = new JArray();
        foreach (var kvp in sorted.Take(maxPairs))
        {
            pairsArr.Add(new JObject
            {
                ["a"] = kvp.Key.Item1,
                ["b"] = kvp.Key.Item2,
                ["volume_mm3"] = UnitConvert.Cm3ToMm3(kvp.Value)
            });
        }
        var data = new JObject
        {
            ["count"] = aggregated.Count,
            ["bodies"] = rawBodies,
            ["total_volume_mm3"] = UnitConvert.Cm3ToMm3(totalVolCm3),
            ["pairs"] = pairsArr,
        };
        if (sorted.Count > maxPairs)
        {
            data["pairs_total"] = sorted.Count;
            data["truncated"] = true;
        }
        data["analysed"] = candidatesB is null
            ? new JObject { ["set_a"] = analysedA, ["set_a_candidates"] = candidatesA }
            : new JObject { ["set_a"] = analysedA, ["set_b"] = analysedB, ["set_a_candidates"] = candidatesA, ["set_b_candidates"] = candidatesB };
        return data;
    }

    // Nested occurrences come back as proxies; report their assembly path so pairs are unambiguous.
    internal static string PathOf(ComponentOccurrence? o)
    {
        if (o is null) return "Unknown";
        try
        {
            var parts = new List<string>();
            foreach (ComponentOccurrence p in o.OccurrencePath) parts.Add(p.Name);
            if (parts.Count > 0) return string.Join("/", parts);
        }
        catch { }
        try { return o.Name; } catch { return "Unknown"; }
    }

    private static Box? SafeBox(ComponentOccurrence o)
    {
        try { return o.RangeBox; } catch { return null; }
    }
}
#endif
