#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Assembly;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Scripting;

/// <summary>Named references: iMates, work features, origin geometry and faces — the same resolvers as the typed tools.</summary>
public static partial class Ipt
{
    /// <summary>
    /// A named reference resolved like inventor_add_constraint: iMate → work feature → origin ("XY Plane", "Z Axis",
    /// "Center Point"). With an occurrence selector the entity comes back as a proxy in the assembly's context, ready for
    /// constraints and measurements; with <paramref name="occurrence"/> null it is the assembly's own geometry.
    /// </summary>
    public static object Ref(object? assembly, object? occurrence, string name)
    {
        var def = AsmDef(assembly);
        var occ = occurrence switch
        {
            null => null,
            ComponentOccurrence o => o,
            _ => Occ(def, occurrence),
        };
        try { return AssemblyRefResolver.ResolveRef(def, occ, name); }
        catch (Exception ex) { throw new IptException(ex.Message); }
    }

    /// <summary>A work plane by name in a part/assembly (name/path/Document/definition, null = active), or a proxy when given an occurrence.</summary>
    public static object WorkPlane(object? scope, string name) =>
        WorkFeature(scope, name, "work plane", d => d is PartComponentDefinition p ? p.WorkPlanes : ((AssemblyComponentDefinition)d).WorkPlanes);

    /// <summary>A work axis by name (see <see cref="WorkPlane"/>).</summary>
    public static object WorkAxis(object? scope, string name) =>
        WorkFeature(scope, name, "work axis", d => d is PartComponentDefinition p ? p.WorkAxes : ((AssemblyComponentDefinition)d).WorkAxes);

    /// <summary>A work point by name (see <see cref="WorkPlane"/>).</summary>
    public static object WorkPoint(object? scope, string name) =>
        WorkFeature(scope, name, "work point", d => d is PartComponentDefinition p ? p.WorkPoints : ((AssemblyComponentDefinition)d).WorkPoints);

    /// <summary>
    /// One face of a part chosen like the typed tools' face selector, for example
    /// <c>new { kind = "planar", normal = "+Z", extreme = "max" }</c> or
    /// <c>new { kind = "cylindrical", radius_mm = 12.5, near_mm = new[] { 0, 0, 40 } }</c>.
    /// Throws with the candidate faces (centroid, area, normal/radius) when none or several match.
    /// </summary>
    public static Face Face(object? part, object spec)
    {
        var token = (spec is string s ? JObject.Parse(s) : Json(spec)) as JObject
                    ?? throw new IptException($"face spec must be an object, got {Describe(spec)}.");
        if (!FaceSelectorSpec.TryParse(token, out var parsed, out var error)) throw new IptException("face spec: " + error);
        var face = FaceSelector.SelectFace(Part(part).ComponentDefinition, parsed, out var candidates);
        if (face == null)
            throw new IptException($"{candidates.Count} faces match the spec (need exactly 1); add near_mm or narrow it. Candidates: " +
                                   ScriptNumbers.Clean(candidates)!.ToString(Newtonsoft.Json.Formatting.None));
        return face;
    }

    private static object WorkFeature(object? scope, string name, string kind, Func<object, System.Collections.IEnumerable> collection)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new IptException(kind + " name is required.");
        var occurrence = scope as ComponentOccurrence;
        var def = occurrence != null ? occurrence.Definition : (scope is PartComponentDefinition or AssemblyComponentDefinition ? scope : Def(scope));
        if (def is not (PartComponentDefinition or AssemblyComponentDefinition))
            throw new IptException($"{kind} lookup needs a part or assembly, got {Describe(scope)}.");
        var names = new List<string>();
        object? found = null;
        foreach (var f in collection(def))
        {
            var n = f switch { Inventor.WorkPlane w => w.Name, WorkAxis a => a.Name, Inventor.WorkPoint p => p.Name, _ => "" };
            names.Add(n);
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { found = f; break; }
        }
        if (found == null) throw new IptException($"No {kind} named '{name}'. Available: {List(names)}.");
        if (occurrence == null) return found;
        occurrence.CreateGeometryProxy(found, out var proxy);
        return proxy;
    }
}
#endif
