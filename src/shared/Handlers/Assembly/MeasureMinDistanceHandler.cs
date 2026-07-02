#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class MeasureMinDistanceHandler : HandlerBase, IInventorCommand
{
    public string Name => "measure_min_distance";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActiveAssembly(context, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        var def = assemblyDoc.ComponentDefinition;
        object entityA;
        object entityB;

        // Resolve entity A (flat wire keys: a_occurrence / a_ref)
        string aOccName = (string?)parameters["a_occurrence"] ?? "";
        string aRefName = (string?)parameters["a_ref"] ?? "";
        if (string.IsNullOrEmpty(aRefName))
        {
            if (!AssemblyRefResolver.TryFindOccurrence(def, aOccName, out var occ, out var err))
            {
                return Fail(context, "INVALID_ARGUMENT", "Failed to resolve a: " + err);
            }
            if (occ == null)
            {
                return Fail(context, "INVALID_ARGUMENT", "a.occurrence is required when a.ref is omitted");
            }
            entityA = occ;
        }
        else
        {
            if (!AssemblyRefResolver.TryResolveInAssembly(def, aOccName, aRefName, out var entity, out var err))
            {
                return Fail(context, "INVALID_ARGUMENT", "Failed to resolve a: " + err);
            }
            entityA = entity!;
        }

        // Resolve entity B
        string bOccName = (string?)parameters["b_occurrence"] ?? "";
        string bRefName = (string?)parameters["b_ref"] ?? "";
        if (string.IsNullOrEmpty(bRefName))
        {
            if (!AssemblyRefResolver.TryFindOccurrence(def, bOccName, out var occ, out var err))
            {
                return Fail(context, "INVALID_ARGUMENT", "Failed to resolve b: " + err);
            }
            if (occ == null)
            {
                return Fail(context, "INVALID_ARGUMENT", "b.occurrence is required when b.ref is omitted");
            }
            entityB = occ;
        }
        else
        {
            if (!AssemblyRefResolver.TryResolveInAssembly(def, bOccName, bRefName, out var entity, out var err))
            {
                return Fail(context, "INVALID_ARGUMENT", "Failed to resolve b: " + err);
            }
            entityB = entity!;
        }

        try
        {
            double distCm = app.MeasureTools.GetMinimumDistance(entityA, entityB);
            return Ok(context, new JObject
            {
                ["distance_mm"] = UnitConvert.CmToMm(distCm)
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Measurement failed: " + ex.Message);
        }
    }
}
#endif
