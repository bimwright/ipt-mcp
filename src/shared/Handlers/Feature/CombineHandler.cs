#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>combine</c> — boolean the active part's solid bodies (spec F4-P1; WS2 ran 9 CombineFeature
/// scripts by hand). <c>base_body</c> + <c>tool_bodies[]</c> resolve via
/// <see cref="EntityResolver.ResolveBody"/> ("1"/"body:N"/name); <c>operation</c> is
/// join|cut|intersect (new_body is meaningless for combine and rejected);
/// <c>keep_tool_bodies</c> retains the tool bodies (default false). Returns the result
/// <c>body_names</c> + <c>volume_mm3</c> — body names can change across a combine, so callers
/// should use the response rather than assuming pre-combine names survive.
/// </summary>
public sealed class CombineHandler : HandlerBase, IInventorCommand
{
    public string Name => "combine";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "combine", out var app, out var part, out var failure))
            return failure!;

        var toolToken = p["tool_bodies"];
        if (toolToken is not JArray toolArr || toolArr.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "tool_bodies must be a non-empty array of body ids or names");

        try
        {
            var baseRef = (string?)p["base_body"];
            if (string.IsNullOrWhiteSpace(baseRef))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "base_body is required (body id or name)");

            var operationStr = (string?)p["operation"];
            PartFeatureOperationEnum operation;
            try { operation = FeatureSupport.Operation(operationStr); }
            catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
            if (operation == PartFeatureOperationEnum.kNewBodyOperation)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    "operation new_body is not valid for combine (use join|cut|intersect; new bodies come from extrude)");

            var def = part.ComponentDefinition;
            var baseBody = EntityResolver.ResolveBody(def, baseRef!);

            var toolCol = app.TransientObjects.CreateObjectCollection();
            var toolNames = new JArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in toolArr)
            {
                var tool = EntityResolver.ResolveBody(def, t.ToString());
                if (string.Equals(tool.Name, baseBody.Name, StringComparison.OrdinalIgnoreCase))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        $"tool_bodies contains the base body ('{baseBody.Name}') — base and tools must differ");
                if (!seen.Add(tool.Name))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        $"tool_bodies lists body '{tool.Name}' twice — each tool body must be distinct");
                toolCol.Add(tool);
                toolNames.Add(tool.Name);
            }

            var keepToolBodies = (bool?)p["keep_tool_bodies"] ?? false;
            var feature = def.Features.CombineFeatures.Add(baseBody, toolCol, operation, keepToolBodies);

            var requestedName = (string?)p["name"];
            if (!string.IsNullOrWhiteSpace(requestedName))
                try { feature.Name = requestedName; } catch { }

            var bodyNames = new JArray();
            double cm3 = 0;
            try
            {
                foreach (SurfaceBody b in feature.SurfaceBodies)
                {
                    bodyNames.Add(b.Name);
                    try { cm3 += b.get_Volume(0.01); } catch { }
                }
            }
            catch { }

            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["operation"] = (operationStr ?? "join").Trim().ToLowerInvariant(),
                ["keep_tool_bodies"] = keepToolBodies,
                ["tool_bodies"] = toolNames,
                ["body_names"] = bodyNames,
                ["volume_mm3"] = UnitConvert.Cm3ToMm3(cm3),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, "combine failed: " + ex.Message); }
    }
}
#endif
