#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Inv = global::Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// Authoritative helper to find assembly occurrences and resolve named geometry references
/// (iMates, user work features, or origin geometry) into Inventor geometry objects/proxies.
/// </summary>
public static class AssemblyRefResolver
{
    private static readonly string[] OriginPlanes = { "XY Plane", "XZ Plane", "YZ Plane" };
    private static readonly string[] OriginAxes = { "X Axis", "Y Axis", "Z Axis" };
    private const string CenterPoint = "Center Point";

    /// <summary>
    /// Finds a top-level occurrence by name in the assembly. If name is null/empty,
    /// sets occ=null representing the assembly itself. Returns true if successful.
    /// On failure, returns false and lists all available occurrence names.
    /// </summary>
    public static bool TryFindOccurrence(
        Inv.AssemblyComponentDefinition def,
        string? name,
        out Inv.ComponentOccurrence? occ,
        out string? error)
    {
        occ = null;
        error = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        occ = FindOccurrenceRecursive(def.Occurrences, name);
        if (occ != null)
        {
            return true;
        }

        // Build recursive list of valid occurrence names to teach the client
        var validNames = new List<string>();
        CollectOccurrenceNames(def.Occurrences, validNames);

        error = $"Occurrence '{name}' not found. Available occurrences: {string.Join(", ", validNames)}";
        return false;
    }

    private static Inv.ComponentOccurrence? FindOccurrenceRecursive(System.Collections.IEnumerable occurrences, string name)
    {
        foreach (Inv.ComponentOccurrence o in occurrences)
        {
            if (string.Equals(o.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return o;
            try
            {
                if (o.SubOccurrences != null && o.SubOccurrences.Count > 0)
                {
                    var found = FindOccurrenceRecursive(o.SubOccurrences, name);
                    if (found != null) return found;
                }
            }
            catch { }
        }
        return null;
    }

    private static void CollectOccurrenceNames(System.Collections.IEnumerable occurrences, List<string> names)
    {
        foreach (Inv.ComponentOccurrence o in occurrences)
        {
            names.Add(o.Name);
            try
            {
                if (o.SubOccurrences != null && o.SubOccurrences.Count > 0)
                {
                    CollectOccurrenceNames(o.SubOccurrences, names);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Resolves an occurrence and reference name in the assembly. Returns true if successful.
    /// If occurrenceName is null/empty, resolves against the top-level assembly's origin/work geometry.
    /// If resolved within an occurrence, automatically creates and returns a geometry proxy.
    /// </summary>
    public static bool TryResolveInAssembly(
        Inv.AssemblyComponentDefinition def,
        string? occurrenceName,
        string refName,
        out object? entity,
        out string? error)
    {
        entity = null;
        error = null;

        if (!TryFindOccurrence(def, occurrenceName, out var occ, out var occErr))
        {
            error = occErr;
            return false;
        }

        try
        {
            entity = ResolveRef(def, occ, refName);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Resolves a reference name against a part document. Returns true if successful.
    /// </summary>
    public static bool TryResolveInPart(
        Inv.PartComponentDefinition def,
        string refName,
        out object? entity,
        out string? error)
    {
        entity = null;
        error = null;

        try
        {
            entity = ResolveRef(def, occ: null, refName);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // def is passed as object because Inventor interop does NOT model inheritance between
    // ComponentDefinition and Part/AssemblyComponentDefinition (assignment fails), yet COM QueryInterface
    // via `is` pattern-matching works — so we accept object and type-test.
    private static object ResolveRef(object def, Inv.ComponentOccurrence? occ, string refName)
    {
        if (string.IsNullOrWhiteSpace(refName))
        {
            throw new ArgumentException("Reference name cannot be empty");
        }

        Inv.WorkPlanes? workPlanes = null;
        Inv.WorkAxes? workAxes = null;
        Inv.WorkPoints? workPoints = null;
        Inv.iMateDefinitions? iMates = null;

        if (occ != null)
        {
            var occDef = occ.Definition;
            if (occDef is Inv.PartComponentDefinition partDef)
            {
                workPlanes = partDef.WorkPlanes;
                workAxes = partDef.WorkAxes;
                workPoints = partDef.WorkPoints;
                iMates = partDef.iMateDefinitions;
            }
            else if (occDef is Inv.AssemblyComponentDefinition asmDef)
            {
                workPlanes = asmDef.WorkPlanes;
                workAxes = asmDef.WorkAxes;
                workPoints = asmDef.WorkPoints;
                iMates = asmDef.iMateDefinitions;
            }
        }
        else if (def is Inv.PartComponentDefinition partDefTop)
        {
            workPlanes = partDefTop.WorkPlanes;
            workAxes = partDefTop.WorkAxes;
            workPoints = partDefTop.WorkPoints;
            iMates = partDefTop.iMateDefinitions;
        }
        else if (def is Inv.AssemblyComponentDefinition asmDefTop)
        {
            workPlanes = asmDefTop.WorkPlanes;
            workAxes = asmDefTop.WorkAxes;
            workPoints = asmDefTop.WorkPoints;
            iMates = asmDefTop.iMateDefinitions;
        }

        // Resolution order per design: iMate name -> named work feature -> origin geometry.
        // (Origin planes/axes/points are themselves members of the WorkPlanes/WorkAxes/WorkPoints
        // collections, so a user iMate that happens to share an origin name still wins.)
        object? resolved = null;

        // 1. iMates
        if (iMates != null)
        {
            foreach (Inv.iMateDefinition im in iMates)
            {
                try
                {
                    if (string.Equals(im.Name, refName, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = im.ReferencedEntity;
                        break;
                    }
                }
                catch { }
            }
        }

        // 2. Named work features (this also matches origin geometry by its enumerated name)
        try
        {
            if (resolved == null && workPlanes != null)
            {
                foreach (Inv.WorkPlane wp in workPlanes)
                {
                    if (string.Equals(wp.Name, refName, StringComparison.OrdinalIgnoreCase)) { resolved = wp; break; }
                }
            }
            if (resolved == null && workAxes != null)
            {
                foreach (Inv.WorkAxis wa in workAxes)
                {
                    if (string.Equals(wa.Name, refName, StringComparison.OrdinalIgnoreCase)) { resolved = wa; break; }
                }
            }
            if (resolved == null && workPoints != null)
            {
                foreach (Inv.WorkPoint wpt in workPoints)
                {
                    if (string.Equals(wpt.Name, refName, StringComparison.OrdinalIgnoreCase)) { resolved = wpt; break; }
                }
            }
        }
        catch { }

        // 3. Origin geometry by canonical name via indexer (fallback for localized enumerated names)
        if (resolved == null && OriginPlanes.Contains(refName, StringComparer.OrdinalIgnoreCase) && workPlanes != null)
        {
            try { resolved = workPlanes[refName]; } catch { }
        }
        if (resolved == null && OriginAxes.Contains(refName, StringComparer.OrdinalIgnoreCase) && workAxes != null)
        {
            try { resolved = workAxes[refName]; } catch { }
        }
        if (resolved == null && string.Equals(refName, CenterPoint, StringComparison.OrdinalIgnoreCase) && workPoints != null)
        {
            try { resolved = workPoints[refName]; } catch { }
        }

        if (resolved == null)
        {
            var available = AvailableNames(workPlanes, workAxes, workPoints, iMates);
            throw new ArgumentException($"Reference '{refName}' not found. Available references on target: {string.Join(", ", available)}");
        }

        object resolvedEntity = resolved;

        // If occurrence is provided, create geometry proxy for assembly constraint Solver
        if (occ != null)
        {
            occ.CreateGeometryProxy(resolvedEntity, out var proxy);
            return proxy;
        }

        return resolvedEntity;
    }

    /// <summary>
    /// Collects all valid reference names (origin, iMates, user work features) on the definition.
    /// </summary>
    public static List<string> AvailableNames(
        Inv.WorkPlanes? workPlanes,
        Inv.WorkAxes? workAxes,
        Inv.WorkPoints? workPoints,
        Inv.iMateDefinitions? iMates)
    {
        var names = new List<string>();
        names.AddRange(OriginPlanes);
        names.AddRange(OriginAxes);
        names.Add(CenterPoint);

        if (iMates != null)
        {
            foreach (Inv.iMateDefinition im in iMates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(im.Name)) names.Add(im.Name);
                }
                catch { }
            }
        }

        try
        {
            if (workPlanes != null)
            {
                foreach (Inv.WorkPlane wp in workPlanes)
                {
                    if (!OriginPlanes.Contains(wp.Name, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wp.Name))
                        names.Add(wp.Name);
                }
            }
            if (workAxes != null)
            {
                foreach (Inv.WorkAxis wa in workAxes)
                {
                    if (!OriginAxes.Contains(wa.Name, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wa.Name))
                        names.Add(wa.Name);
                }
            }
            if (workPoints != null)
            {
                foreach (Inv.WorkPoint wpt in workPoints)
                {
                    if (!string.Equals(wpt.Name, CenterPoint, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wpt.Name))
                        names.Add(wpt.Name);
                }
            }
        }
        catch { }

        return names;
    }

    /// <summary>
    /// Structured self-teaching payload per spec §9: <c>{imates, work_features, origin}</c> for a part
    /// or assembly component definition (or an occurrence's definition). Names are grouped so an agent
    /// can retry a failed ref against the correct category. scopeDef is object because Inventor interop
    /// has no ComponentDefinition inheritance (see ResolveRef); we COM type-test it.
    /// </summary>
    public static JObject AvailableNamesStructured(object? scopeDef)
    {
        Inv.WorkPlanes? workPlanes = null;
        Inv.WorkAxes? workAxes = null;
        Inv.WorkPoints? workPoints = null;
        Inv.iMateDefinitions? iMates = null;

        if (scopeDef is Inv.PartComponentDefinition pd)
        {
            workPlanes = pd.WorkPlanes; workAxes = pd.WorkAxes; workPoints = pd.WorkPoints; iMates = pd.iMateDefinitions;
        }
        else if (scopeDef is Inv.AssemblyComponentDefinition ad)
        {
            workPlanes = ad.WorkPlanes; workAxes = ad.WorkAxes; workPoints = ad.WorkPoints; iMates = ad.iMateDefinitions;
        }

        var imates = new JArray();
        if (iMates != null)
        {
            foreach (Inv.iMateDefinition im in iMates)
            {
                try { if (!string.IsNullOrEmpty(im.Name)) imates.Add(im.Name); } catch { }
            }
        }

        var workFeatures = new JArray();
        var origin = new JArray();
        try
        {
            if (workPlanes != null)
                foreach (Inv.WorkPlane wp in workPlanes)
                {
                    if (string.IsNullOrEmpty(wp.Name)) continue;
                    (OriginPlanes.Contains(wp.Name, StringComparer.OrdinalIgnoreCase) ? origin : workFeatures).Add(wp.Name);
                }
            if (workAxes != null)
                foreach (Inv.WorkAxis wa in workAxes)
                {
                    if (string.IsNullOrEmpty(wa.Name)) continue;
                    (OriginAxes.Contains(wa.Name, StringComparer.OrdinalIgnoreCase) ? origin : workFeatures).Add(wa.Name);
                }
            if (workPoints != null)
                foreach (Inv.WorkPoint wpt in workPoints)
                {
                    if (string.IsNullOrEmpty(wpt.Name)) continue;
                    (string.Equals(wpt.Name, CenterPoint, StringComparison.OrdinalIgnoreCase) ? origin : workFeatures).Add(wpt.Name);
                }
        }
        catch { }

        return new JObject { ["imates"] = imates, ["work_features"] = workFeatures, ["origin"] = origin };
    }

    /// <summary>
    /// Converts a constraint HealthStatusEnum to its stable wire string format.
    /// </summary>
    public static string HealthToString(Inv.HealthStatusEnum h)
    {
        return h switch
        {
            Inv.HealthStatusEnum.kUpToDateHealth => "up_to_date",
            Inv.HealthStatusEnum.kOutOfDateHealth => "out_of_date",
            Inv.HealthStatusEnum.kDriverLostHealth => "driver_lost",
            Inv.HealthStatusEnum.kInErrorHealth => "in_error",
            Inv.HealthStatusEnum.kSuppressedHealth => "suppressed",
            Inv.HealthStatusEnum.kInconsistentHealth => "inconsistent",
            _ => "unknown_" + ((int)h)
        };
    }
}
#endif
