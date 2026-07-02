#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inv = global::Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// Resolves a named reference (iMate, user work feature, or origin geometry) on an occurrence
/// (or the top-level assembly if occurrence is null). Automatically handles creating geometry proxies
/// if the reference is resolved within an occurrence.
/// </summary>
public static class AssemblyRefResolver
{
    private static readonly string[] OriginPlanes = { "XY Plane", "XZ Plane", "YZ Plane" };
    private static readonly string[] OriginAxes = { "X Axis", "Y Axis", "Z Axis" };
    private const string CenterPoint = "Center Point";

    public static object Resolve(Inv.AssemblyComponentDefinition def, Inv.ComponentOccurrence? occ, string refName, out string resolvedKind)
    {
        resolvedKind = "unknown";
        if (string.IsNullOrWhiteSpace(refName))
        {
            throw new ArgumentException("Reference name cannot be empty");
        }

        // Get the active component definition context (occurrence's part/subassembly def, or top-level assembly def)
        // Cast to dynamic to bypass compile-time restrictions on WorkPlanes/WorkAxes/WorkPoints/iMateDefinitions
        // which are not exposed on the base ComponentDefinition interface in interop.
        dynamic targetDef = occ != null ? (dynamic)occ.Definition : (dynamic)def;
        object entity;

        // 1. Fallback: Origin geometry
        if (OriginPlanes.Contains(refName, StringComparer.OrdinalIgnoreCase))
        {
            resolvedKind = "origin_plane";
            entity = targetDef.WorkPlanes[refName];
        }
        else if (OriginAxes.Contains(refName, StringComparer.OrdinalIgnoreCase))
        {
            resolvedKind = "origin_axis";
            entity = targetDef.WorkAxes[refName];
        }
        else if (string.Equals(refName, CenterPoint, StringComparison.OrdinalIgnoreCase))
        {
            resolvedKind = "origin_point";
            entity = targetDef.WorkPoints[refName];
        }
        else
        {
            // 2. Fallback: iMate definitions
            object? foundIMate = null;
            try
            {
                foreach (Inv.iMateDefinition im in targetDef.iMateDefinitions)
                {
                    if (string.Equals(im.Name, refName, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedKind = "imate";
                        foundIMate = ((dynamic)im).Entity;
                        break;
                    }
                }
            }
            catch { }

            if (foundIMate != null)
            {
                entity = foundIMate;
            }
            else
            {
                // 3. Fallback: Named User Work Features
                object? foundWork = null;
                try
                {
                    foreach (Inv.WorkPlane wp in targetDef.WorkPlanes)
                    {
                        if (string.Equals(wp.Name, refName, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedKind = "work_plane";
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
                                resolvedKind = "work_axis";
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
                                resolvedKind = "work_point";
                                foundWork = wpt;
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (foundWork != null)
                {
                    entity = foundWork;
                }
                else
                {
                    // Unresolved: Collect all available names to self-teach the caller
                    var available = CollectAvailableNames(targetDef);
                    throw new ArgumentException($"Reference '{refName}' not found. Available reference names: {string.Join(", ", available)}");
                }
            }
        }

        // If occurrence is not null, create a geometry proxy so the parent assembly can use it for constraints
        if (occ != null)
        {
            occ.CreateGeometryProxy(entity, out var proxy);
            return proxy;
        }

        return entity;
    }

    public static List<string> CollectAvailableNames(dynamic targetDef)
    {
        var names = new List<string>();
        // Add origin names
        names.AddRange(OriginPlanes);
        names.AddRange(OriginAxes);
        names.Add(CenterPoint);

        // Add iMate names
        try
        {
            foreach (Inv.iMateDefinition im in targetDef.iMateDefinitions)
            {
                if (!string.IsNullOrEmpty(im.Name)) names.Add(im.Name);
            }
        }
        catch { }

        // Add user work feature names
        try
        {
            // Skip the first 3 default work planes/axes and center point in enumeration
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
}
#endif
