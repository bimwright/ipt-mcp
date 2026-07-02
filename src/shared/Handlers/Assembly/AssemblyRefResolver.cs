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

        foreach (Inv.ComponentOccurrence o in def.Occurrences)
        {
            if (string.Equals(o.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                occ = o;
                return true;
            }
        }

        // Build list of valid occurrence names to teach the client
        var validNames = new List<string>();
        foreach (Inv.ComponentOccurrence o in def.Occurrences)
        {
            validNames.Add(o.Name);
        }

        error = $"Occurrence '{name}' not found. Available occurrences: {string.Join(", ", validNames)}";
        return false;
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

    private static object ResolveRef(Inv.AssemblyComponentDefinition def, Inv.ComponentOccurrence? occ, string refName)
    {
        if (string.IsNullOrWhiteSpace(refName))
        {
            throw new ArgumentException("Reference name cannot be empty");
        }

        dynamic targetDef;
        if (occ != null)
        {
            targetDef = occ.Definition;
        }
        else
        {
            targetDef = def;
        }

        object resolvedEntity;

        // 1. Origin geometry
        if (OriginPlanes.Contains(refName, StringComparer.OrdinalIgnoreCase))
        {
            resolvedEntity = targetDef.WorkPlanes[refName];
        }
        else if (OriginAxes.Contains(refName, StringComparer.OrdinalIgnoreCase))
        {
            resolvedEntity = targetDef.WorkAxes[refName];
        }
        else if (string.Equals(refName, CenterPoint, StringComparison.OrdinalIgnoreCase))
        {
            resolvedEntity = targetDef.WorkPoints[refName];
        }
        else
        {
            // 2. iMates
            object? foundIMate = null;
            try
            {
                foreach (Inv.iMateDefinition im in targetDef.iMateDefinitions)
                {
                    if (string.Equals(im.Name, refName, StringComparison.OrdinalIgnoreCase))
                    {
                        foundIMate = ((dynamic)im).Entity;
                        break;
                    }
                }
            }
            catch { }

            if (foundIMate != null)
            {
                resolvedEntity = foundIMate;
            }
            else
            {
                // 3. Named User Work Features
                object? foundWork = null;
                try
                {
                    foreach (Inv.WorkPlane wp in targetDef.WorkPlanes)
                    {
                        if (string.Equals(wp.Name, refName, StringComparison.OrdinalIgnoreCase))
                        {
                            foundWork = wp;
                            break;
                        }
                    }
                    if (foundWork == null)
                    {
                        foreach (Inv.WorkAxis wa in targetDef.WorkAxes)
                        {
                            if (string.Equals(wa.Name, refName, StringComparison.OrdinalIgnoreCase))
                            {
                                foundWork = wa;
                                break;
                            }
                        }
                    }
                    if (foundWork == null)
                    {
                        foreach (Inv.WorkPoint wpt in targetDef.WorkPoints)
                        {
                            if (string.Equals(wpt.Name, refName, StringComparison.OrdinalIgnoreCase))
                            {
                                foundWork = wpt;
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (foundWork != null)
                {
                    resolvedEntity = foundWork;
                }
                else
                {
                    var available = AvailableNames(targetDef);
                    throw new ArgumentException($"Reference '{refName}' not found. Available references on target: {string.Join(", ", available)}");
                }
            }
        }

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
    public static List<string> AvailableNames(dynamic targetDef)
    {
        var names = new List<string>();
        names.AddRange(OriginPlanes);
        names.AddRange(OriginAxes);
        names.Add(CenterPoint);

        try
        {
            foreach (Inv.iMateDefinition im in targetDef.iMateDefinitions)
            {
                if (!string.IsNullOrEmpty(im.Name)) names.Add(im.Name);
            }
        }
        catch { }

        try
        {
            foreach (Inv.WorkPlane wp in targetDef.WorkPlanes)
            {
                if (!OriginPlanes.Contains(wp.Name, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wp.Name))
                    names.Add(wp.Name);
            }
            foreach (Inv.WorkAxis wa in targetDef.WorkAxes)
            {
                if (!OriginAxes.Contains(wa.Name, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wa.Name))
                    names.Add(wa.Name);
            }
            foreach (Inv.WorkPoint wpt in targetDef.WorkPoints)
            {
                if (!string.Equals(wpt.Name, CenterPoint, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(wpt.Name))
                    names.Add(wpt.Name);
            }
        }
        catch { }

        return names;
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
            _ => "unknown"
        };
    }
}
#endif
