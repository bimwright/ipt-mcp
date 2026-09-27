#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// Pose + lock helpers shared by <c>place_occurrences</c> and <c>set_occurrence_state</c> (E3).
/// <c>lock=workplanes</c> reproduces the recurring script helper ("Bind"): three grounded,
/// hidden fixed work planes at the occurrence's origin planes, each flush-constrained to the
/// occurrence's own origin plane — the pose is held by the solver and survives parent updates,
/// unlike plain grounding. Planes and constraints are named <c>IF_&lt;occurrence&gt;_1..3</c>.
/// </summary>
internal static class OccurrencePlacement
{
    public const string LockPrefix = "IF_";

    public static Matrix ToMatrix(Application app, PoseSpec pose)
    {
        var tg = app.TransientGeometry;
        var m = tg.CreateMatrix();
        m.SetCoordinateSystem(
            tg.CreatePoint(pose.OriginCm[0], pose.OriginCm[1], pose.OriginCm[2]),
            tg.CreateVector(pose.X[0], pose.X[1], pose.X[2]),
            tg.CreateVector(pose.Y[0], pose.Y[1], pose.Y[2]),
            tg.CreateVector(pose.Z[0], pose.Z[1], pose.Z[2]));
        return m;
    }

    public static bool TryParseLock(JToken? t, out string mode, out string? error)
    {
        mode = (string?)t ?? "none";
        error = null;
        if (mode is "none" or "grounded" or "workplanes") return true;
        error = "lock must be none, grounded or workplanes";
        return false;
    }

    /// <summary>Applies the lock mode; returns the constraint health list for workplanes.</summary>
    public static JArray ApplyLock(AssemblyComponentDefinition ac, ComponentOccurrence o, string mode)
    {
        var health = new JArray();
        switch (mode)
        {
            case "grounded":
                o.Grounded = true;
                break;
            case "workplanes":
                o.Grounded = false;
                var def = o.Definition;
                WorkPlanes planes = def is PartComponentDefinition pd ? pd.WorkPlanes : ((AssemblyComponentDefinition)def).WorkPlanes;
                var m = o.Transformation;
                for (var i = 1; i <= 3; i++)
                {
                    planes[i].GetPosition(out var origin, out var x, out var y);
                    origin.TransformBy(m);
                    x.TransformBy(m);
                    y.TransformBy(m);
                    var w = ac.WorkPlanes.AddFixed(origin, x, y, false);
                    w.Name = LockPrefix + o.Name + "_" + i;
                    w.Grounded = true;
                    w.Visible = false;
                    o.CreateGeometryProxy(planes[i], out var proxy);
                    var c = ac.Constraints.AddFlushConstraint(proxy, w, 0);
                    c.Name = w.Name;
                    string h;
                    try { h = EnumText.Friendly(c.HealthStatus.ToString(), "Health"); } catch { h = "Unknown"; }
                    health.Add(h);
                }
                break;
            default:
                o.Grounded = false;
                break;
        }
        return health;
    }

    /// <summary>Deletes the IF_&lt;name&gt;_1..3 lock planes (their flush constraints go with them). Returns how many.</summary>
    public static int RemoveLock(AssemblyComponentDefinition ac, string occurrenceName)
    {
        var doomed = new List<WorkPlane>();
        for (var i = 1; i <= 3; i++)
        {
            var name = LockPrefix + occurrenceName + "_" + i;
            foreach (WorkPlane w in ac.WorkPlanes)
            {
                string n;
                try { n = w.Name; } catch { continue; }
                if (n == name) { doomed.Add(w); break; }
            }
        }
        foreach (var w in doomed)
        {
            try { w.Delete(); } catch { }
        }
        return doomed.Count;
    }

    public static bool HasLock(AssemblyComponentDefinition ac, string occurrenceName)
    {
        var name = LockPrefix + occurrenceName + "_1";
        foreach (WorkPlane w in ac.WorkPlanes)
        {
            try { if (w.Name == name) return true; } catch { }
        }
        return false;
    }
}
#endif
