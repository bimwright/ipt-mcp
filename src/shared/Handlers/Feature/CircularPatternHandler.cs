#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Handlers.Assembly;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

public sealed class CircularPatternHandler : HandlerBase, IInventorCommand
{
    public string Name => "circular_pattern";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(context, Name, out var app, out var partDoc, out var failure))
        {
            return failure!;
        }

        var featureNames = parameters["feature_names"] as JArray;
        if (featureNames == null || featureNames.Count == 0)
        {
            return Fail(context, "INVALID_ARGUMENT", "feature_names is required and must contain at least one feature name");
        }

        string axis = (string?)parameters["axis"] ?? "";
        if (string.IsNullOrWhiteSpace(axis))
        {
            return Fail(context, "INVALID_ARGUMENT", "axis is required");
        }

        int count = (int?)parameters["count"] ?? 0;
        if (count <= 1)
        {
            return Fail(context, "INVALID_ARGUMENT", "count must be greater than 1");
        }

        double angleDeg = (double?)parameters["angle_deg"] ?? 360.0;
        bool naturalDirection = (bool?)parameters["natural_direction"] ?? true;

        var def = partDoc.ComponentDefinition;

        // Collect features to pattern
        var featuresColl = app.TransientObjects.CreateObjectCollection();
        foreach (var tok in featureNames)
        {
            string fName = (string?)tok ?? "";
            try
            {
                var f = def.Features[fName];
                featuresColl.Add(f);
            }
            catch
            {
                return Fail(context, "INVALID_ARGUMENT", "Feature not found: " + fName);
            }
        }

        // Resolve axis reference against the part document (iMate -> work feature -> origin, by name).
        if (!AssemblyRefResolver.TryResolveInPart(def, axis, out var axisEntity, out var axisErr))
        {
            return Fail(context, "INVALID_ARGUMENT", $"Failed to resolve axis reference '{axis}': {axisErr}");
        }

        try
        {
            // Spike §5: the direct Add(...) throws E_FAIL on modern Inventor; use CreateDefinition + AddByDefinition.
            double angleRad = UnitConvert.DegToRad(angleDeg);
            var cpFeatures = def.Features.CircularPatternFeatures;
            var cpDef = cpFeatures.CreateDefinition(featuresColl, axisEntity!, naturalDirection, count, angleRad, true);
            var pattern = cpFeatures.AddByDefinition(cpDef);

            return Ok(context, new JObject
            {
                ["pattern_name"] = pattern.Name,
                ["count"] = count
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to create circular pattern feature: " + ex.Message);
        }
    }
}
#endif
