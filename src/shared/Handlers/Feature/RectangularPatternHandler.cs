#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Handlers.Assembly;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

public sealed class RectangularPatternHandler : HandlerBase, IInventorCommand
{
    public string Name => "rectangular_pattern";
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

        string dir1 = (string?)parameters["dir1"] ?? "";
        if (string.IsNullOrWhiteSpace(dir1))
        {
            return Fail(context, "INVALID_ARGUMENT", "dir1 is required");
        }

        int count1 = (int?)parameters["count1"] ?? 0;
        double spacingMm1 = (double?)parameters["spacing_mm1"] ?? 0;
        bool naturalDirection1 = (bool?)parameters["natural_direction1"] ?? true;

        string dir2 = (string?)parameters["dir2"] ?? "";
        int? count2 = (int?)parameters["count2"];
        double? spacingMm2 = (double?)parameters["spacing_mm2"];
        bool naturalDirection2 = (bool?)parameters["natural_direction2"] ?? true;

        if (!PatternInputValidator.TryValidateRectangular(
                count1, spacingMm1, dir2, count2, spacingMm2, out var validationError))
        {
            return Fail(context, "INVALID_ARGUMENT", validationError);
        }

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

        // Resolve direction 1 against the part document (iMate -> work feature -> origin, by name).
        if (!AssemblyRefResolver.TryResolveInPart(def, dir1, out var dirEntity1, out var dir1Err))
        {
            return Fail(context, "INVALID_ARGUMENT", $"Failed to resolve direction 1 reference '{dir1}': {dir1Err}");
        }
        if (dirEntity1 is not WorkAxis)
        {
            return Fail(context, "INVALID_ARGUMENT", $"Direction 1 reference '{dir1}' must resolve to a work/origin axis");
        }

        // Resolve direction 2 (optional)
        object? dirEntity2 = null;
        if (!string.IsNullOrEmpty(dir2))
        {
            if (!AssemblyRefResolver.TryResolveInPart(def, dir2, out dirEntity2, out var dir2Err))
            {
                return Fail(context, "INVALID_ARGUMENT", $"Failed to resolve direction 2 reference '{dir2}': {dir2Err}");
            }
            if (dirEntity2 is not WorkAxis)
            {
                return Fail(context, "INVALID_ARGUMENT", $"Direction 2 reference '{dir2}' must resolve to a work/origin axis");
            }
        }

        try
        {
            // Spike §5: the direct Add(...) throws E_FAIL on modern Inventor; use CreateDefinition +
            // AddByDefinition, and set the second-direction (Y*) properties directly on the definition.
            double spacingCm1 = UnitConvert.MmToCm(spacingMm1);
            var rpFeatures = def.Features.RectangularPatternFeatures;
            var rpDef = rpFeatures.CreateDefinition(featuresColl, dirEntity1!, naturalDirection1, count1, spacingCm1);

            if (dirEntity2 != null)
            {
                double spacingCm2 = UnitConvert.MmToCm(spacingMm2!.Value);
                rpDef.YDirectionEntity = dirEntity2;
                rpDef.NaturalYDirection = naturalDirection2;
                rpDef.YCount = count2!.Value;
                rpDef.YSpacing = spacingCm2;
            }

            var pattern = rpFeatures.AddByDefinition(rpDef);

            return Ok(context, new JObject
            {
                ["pattern_name"] = pattern.Name,
                ["total_instances"] = dirEntity2 != null ? count1 * count2!.Value : count1
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to create rectangular pattern feature: " + ex.Message);
        }
    }
}
#endif
