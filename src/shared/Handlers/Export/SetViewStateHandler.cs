#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Assembly;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>set_view_state</c> (E5) — display state of the active document in one call, the part of
/// a "photograph the model" script that came before SaveAsBitmap: activate (or create) a design
/// view representation, toggle object visibility (work features, sketches, …) and show/hide
/// occurrences picked by selectors. Display-only; nothing geometric changes. <c>capture_view</c>
/// accepts the same keys and runs this first.
/// </summary>
public sealed class SetViewStateHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_view_state";
    public bool IsReadOnly => false;

    internal static readonly Dictionary<string, Action<ObjectVisibility, bool>> VisibilityKeys = new(StringComparer.Ordinal)
    {
        ["all_work_features"] = (v, b) => v.AllWorkFeatures = b,
        ["origin_work_planes"] = (v, b) => v.OriginWorkPlanes = b,
        ["origin_work_axes"] = (v, b) => v.OriginWorkAxes = b,
        ["origin_work_points"] = (v, b) => v.OriginWorkPoints = b,
        ["user_work_planes"] = (v, b) => v.UserWorkPlanes = b,
        ["user_work_axes"] = (v, b) => v.UserWorkAxes = b,
        ["user_work_points"] = (v, b) => v.UserWorkPoints = b,
        ["sketches"] = (v, b) => v.Sketches = b,
        ["sketches_3d"] = (v, b) => v.Sketches3D = b,
        ["sketch_dimensions"] = (v, b) => v.SketchDimensions = b,
        ["ucs_triads"] = (v, b) => v.UCSTriads = b,
        ["annotations_3d"] = (v, b) => v.Annotations3D = b,
        ["welds"] = (v, b) => v.Welds = b,
    };

    public static bool HasViewStateKeys(JObject p)
        => p["design_view"] is { Type: not JTokenType.Null } || p["object_visibility"] is { Type: not JTokenType.Null }
           || p["occurrence_visibility"] is { Type: not JTokenType.Null };

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null) return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        if (!HasViewStateKeys(p))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "pass design_view, object_visibility and/or occurrence_visibility");

        var applied = new JObject();
        try
        {
            if (p["design_view"] is { Type: not JTokenType.Null } dv)
            {
                var name = (string?)dv;
                if (string.IsNullOrWhiteSpace(name)) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "design_view must be a name");
                var create = p["design_view_create"]?.Type == JTokenType.Boolean && (bool)p["design_view_create"]!;
                var reps = Representations(doc);
                if (reps is null) return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "design views need a part or assembly document");
                DesignViewRepresentation? rep = null;
                var names = new List<string>();
                foreach (DesignViewRepresentation r in reps)
                {
                    names.Add(r.Name);
                    if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) rep = r;
                }
                if (rep is null)
                {
                    if (!create)
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                            $"design view '{name}' not found. Available: {string.Join(", ", names)}. Pass design_view_create=true to create it.");
                    rep = reps.Add(name);
                    applied["design_view_created"] = true;
                }
                rep.Activate();
                applied["design_view"] = rep.Name;
            }

            if (p["object_visibility"] is { Type: not JTokenType.Null } ovt)
            {
                if (ovt is not JObject ov) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "object_visibility must be an object of booleans");
                var vis = ObjectVisibilityOf(doc);
                if (vis is null) return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "object_visibility needs a part or assembly document");
                var done = new JObject();
                foreach (var prop in ov.Properties())
                {
                    if (!VisibilityKeys.TryGetValue(prop.Name, out var set))
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                            $"unknown object_visibility key '{prop.Name}'. Keys: {string.Join(", ", VisibilityKeys.Keys)}");
                    if (prop.Value.Type != JTokenType.Boolean)
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"object_visibility.{prop.Name} must be a boolean");
                    set(vis, (bool)prop.Value);
                    done[prop.Name] = (bool)prop.Value;
                }
                applied["object_visibility"] = done;
            }

            if (p["occurrence_visibility"] is { Type: not JTokenType.Null } oct)
            {
                if (doc is not AssemblyDocument asm)
                    return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "occurrence_visibility needs an assembly document");
                var rules = oct is JArray arr ? arr.ToList() : new List<JToken> { oct };
                var counts = new JArray();
                for (var i = 0; i < rules.Count; i++)
                {
                    var field = $"occurrence_visibility[{i}]";
                    if (rules[i] is not JObject rule || rule["visible"]?.Type != JTokenType.Boolean)
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, field + " must be {selector, visible: bool}");
                    if (!OccurrenceSelection.TryResolve(asm.ComponentDefinition, rule["selector"], field + ".selector", out var items, out var error,
                            defaultLimit: 5000))
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, error!);
                    var visible = (bool)rule["visible"]!;
                    foreach (var it in items) it.Occurrence.Visible = visible;
                    counts.Add(new JObject { ["visible"] = visible, ["count"] = items.Count });
                }
                applied["occurrence_visibility"] = counts;
            }

            try { app.ActiveView?.Update(); } catch { }
            return Ok(ctx, applied);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "set_view_state failed: " + ex.Message);
        }
    }

    private static DesignViewRepresentations? Representations(global::Inventor.Document doc) => doc switch
    {
        AssemblyDocument a => a.ComponentDefinition.RepresentationsManager.DesignViewRepresentations,
        PartDocument pd => pd.ComponentDefinition.RepresentationsManager.DesignViewRepresentations,
        _ => null,
    };

    private static ObjectVisibility? ObjectVisibilityOf(global::Inventor.Document doc) => doc switch
    {
        AssemblyDocument a => a.ObjectVisibility,
        PartDocument pd => pd.ObjectVisibility,
        _ => null,
    };
}
#endif
