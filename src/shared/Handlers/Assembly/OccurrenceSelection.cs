#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// Walks an assembly's occurrence tree and resolves an <see cref="OccurrenceSelectorSpec"/> (S3.2).
/// Nested occurrences come back as the assembly-context proxies Inventor hands out from
/// <c>SubOccurrences</c>, so geometry/visibility calls on them act in the top assembly's context.
/// </summary>
internal static class OccurrenceSelection
{
    public sealed class Item
    {
        public ComponentOccurrence Occurrence { get; init; } = null!;
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";
        public int Depth { get; init; }
        public bool IsLeaf { get; init; }
        public bool Suppressed { get; init; }
        public string? File { get; init; }
    }

    /// <summary>Every occurrence, depth-first, in browser order. Suppressed occurrences are not descended.</summary>
    public static List<Item> Walk(AssemblyComponentDefinition def, int maxDepth = 64)
    {
        var list = new List<Item>();
        Walk(def.Occurrences, "", 1, maxDepth, list);
        return list;
    }

    private static void Walk(System.Collections.IEnumerable occurrences, string prefix, int depth, int maxDepth, List<Item> list)
    {
        foreach (ComponentOccurrence o in occurrences)
        {
            string name;
            try { name = o.Name; } catch { continue; }
            var suppressed = false;
            try { suppressed = o.Suppressed; } catch { }
            ComponentOccurrencesEnumerator? subs = null;
            if (!suppressed)
            {
                try { subs = o.SubOccurrences; } catch { }
            }
            var hasSubs = false;
            try { hasSubs = subs != null && subs.Count > 0; } catch { }
            string? file = null;
            try { file = o.ReferencedDocumentDescriptor?.FullDocumentName; } catch { }
            var path = prefix.Length == 0 ? name : prefix + "/" + name;
            var isAssembly = false;
            try { isAssembly = o.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject; } catch { }
            list.Add(new Item
            {
                Occurrence = o, Name = name, Path = path, Depth = depth,
                IsLeaf = !isAssembly && !hasSubs, Suppressed = suppressed, File = file,
            });
            if (hasSubs && depth < maxDepth) Walk(subs!, path, depth + 1, maxDepth, list);
        }
    }

    /// <summary>
    /// Resolves the selector. Zero matches or more than <see cref="OccurrenceSelectorSpec.Limit"/>
    /// matches are errors (never a silent cut); a zero-match error suggests close names.
    /// </summary>
    public static bool TryResolve(AssemblyComponentDefinition def, OccurrenceSelectorSpec spec, string field,
        out List<Item> matched, out string? error, bool allowEmpty = false)
    {
        var all = Walk(def, spec.MaxDepth ?? 64);
        matched = all.Where(i => spec.Matches(i.Name, i.Path, i.File, i.Depth, i.IsLeaf, i.Suppressed)).ToList();
        error = null;
        if (matched.Count == 0 && !allowEmpty)
        {
            var near = spec.Suggest(all.Select(i => i.Path));
            error = $"{field} matched no occurrence ({spec.Describe()}; {all.Count} occurrences scanned)."
                    + (near.Count > 0 ? " Closest: " + string.Join(", ", near) : "");
            return false;
        }
        if (matched.Count > spec.Limit)
        {
            error = $"{field} matched {matched.Count} occurrences, above limit {spec.Limit} ({spec.Describe()}). Narrow the selector or raise limit.";
            return false;
        }
        return true;
    }

    /// <summary>Parse + resolve in one step; failures come back as the error text.</summary>
    public static bool TryResolve(AssemblyComponentDefinition def, JToken? selector, string field,
        out List<Item> matched, out string? error, int? defaultLimit = null, bool allowEmpty = false)
    {
        matched = new List<Item>();
        if (!OccurrenceSelectorSpec.TryParse(selector, field, out var spec, out error, defaultLimit)) return false;
        return TryResolve(def, spec, field, out matched, out error, allowEmpty);
    }

    public static JArray BboxMm(Box b) => new JArray
    {
        new JArray(UnitConvert.CmToMm(b.MinPoint.X), UnitConvert.CmToMm(b.MinPoint.Y), UnitConvert.CmToMm(b.MinPoint.Z)),
        new JArray(UnitConvert.CmToMm(b.MaxPoint.X), UnitConvert.CmToMm(b.MaxPoint.Y), UnitConvert.CmToMm(b.MaxPoint.Z)),
    };

    /// <summary>{origin_mm, x_axis, y_axis, z_axis} from an occurrence transformation (columns = axes).</summary>
    public static JObject Transform(Matrix m) => new JObject
    {
        ["origin_mm"] = new JArray(UnitConvert.CmToMm(m.Cell[1, 4]), UnitConvert.CmToMm(m.Cell[2, 4]), UnitConvert.CmToMm(m.Cell[3, 4])),
        ["x_axis"] = new JArray(R(m.Cell[1, 1]), R(m.Cell[2, 1]), R(m.Cell[3, 1])),
        ["y_axis"] = new JArray(R(m.Cell[1, 2]), R(m.Cell[2, 2]), R(m.Cell[3, 2])),
        ["z_axis"] = new JArray(R(m.Cell[1, 3]), R(m.Cell[2, 3]), R(m.Cell[3, 3])),
    };

    private static double R(double v) => Math.Round(v, 9);

    /// <summary>True when two boxes overlap after growing each by <paramref name="gapCm"/>.</summary>
    public static bool Overlap(Box a, Box b, double gapCm)
        => a.MinPoint.X - gapCm <= b.MaxPoint.X && b.MinPoint.X - gapCm <= a.MaxPoint.X
        && a.MinPoint.Y - gapCm <= b.MaxPoint.Y && b.MinPoint.Y - gapCm <= a.MaxPoint.Y
        && a.MinPoint.Z - gapCm <= b.MaxPoint.Z && b.MinPoint.Z - gapCm <= a.MaxPoint.Z;
}
#endif
