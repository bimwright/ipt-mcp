#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class AddConstraintHandler : HandlerBase, IInventorCommand
{
    public string Name => "add_constraint";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActiveAssembly(context, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        string type = ((string?)parameters["type"] ?? "").Trim().ToLowerInvariant();
        string aOccurrence = (string?)parameters["a_occurrence"] ?? "";
        string aRef = (string?)parameters["a_ref"] ?? "";
        string bOccurrence = (string?)parameters["b_occurrence"] ?? "";
        string bRef = (string?)parameters["b_ref"] ?? "";
        double offsetMm = (double?)parameters["offset_mm"] ?? 0;
        double? angleDeg = (double?)parameters["angle_deg"];
        bool insertOpposed = (bool?)parameters["insert_opposed"] ?? true;

        var def = assemblyDoc.ComponentDefinition;

        // Resolve reference A
        if (!AssemblyRefResolver.TryResolveInAssembly(def, aOccurrence, aRef, out var entityA, out var errA))
        {
            return FailWithAvailable(context, "Failed to resolve a: " + errA, AvailableForScope(def, aOccurrence));
        }

        // Resolve reference B
        if (!AssemblyRefResolver.TryResolveInAssembly(def, bOccurrence, bRef, out var entityB, out var errB))
        {
            return FailWithAvailable(context, "Failed to resolve b: " + errB, AvailableForScope(def, bOccurrence));
        }

        try
        {
            AssemblyConstraint constraint;
            double offsetCm = UnitConvert.MmToCm(offsetMm);

            switch (type)
            {
                case "mate":
                    constraint = (AssemblyConstraint)def.Constraints.AddMateConstraint(entityA, entityB, offsetCm);
                    break;
                case "flush":
                    constraint = (AssemblyConstraint)def.Constraints.AddFlushConstraint(entityA, entityB, offsetCm);
                    break;
                case "insert":
                    constraint = (AssemblyConstraint)def.Constraints.AddInsertConstraint(entityA, entityB, insertOpposed, offsetCm);
                    break;
                case "angle":
                    if (angleDeg == null)
                    {
                        return Fail(context, "INVALID_ARGUMENT", "angle_deg is required for type=angle");
                    }
                    double angleRad = UnitConvert.DegToRad(angleDeg.Value);
                    constraint = (AssemblyConstraint)def.Constraints.AddAngleConstraint(entityA, entityB, angleRad);
                    break;
                default:
                    return Fail(context, "INVALID_ARGUMENT", "Unsupported constraint type: " + type);
            }

            string health = AssemblyRefResolver.HealthToString(constraint.HealthStatus);
            return Ok(context, new JObject
            {
                ["constraint_name"] = constraint.Name,
                ["type"] = type,
                ["health"] = health
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to add constraint: " + ex.Message);
        }
    }

    // Structured available-names for the scope a failed ref was searched in (spec §9): assembly origin
    // when the occurrence is empty, otherwise the occurrence's own part/assembly definition.
    private static JObject AvailableForScope(AssemblyComponentDefinition def, string occName)
    {
        if (string.IsNullOrWhiteSpace(occName))
        {
            return AssemblyRefResolver.AvailableNamesStructured(def);
        }
        if (AssemblyRefResolver.TryFindOccurrence(def, occName, out var occ, out _) && occ != null)
        {
            return AssemblyRefResolver.AvailableNamesStructured(occ.Definition);
        }
        return AssemblyRefResolver.AvailableNamesStructured(def);
    }

    private InventorCommandResult FailWithAvailable(InventorCommandContext context, string message, JObject available)
    {
        var result = InventorCommandResult.Fail(System.Guid.Empty, "INVALID_ARGUMENT", message, new InventorResponseMeta
        {
            TargetId = context.TargetId,
            InventorYear = context.InventorYear == 0 ? (int?)null : context.InventorYear
        });
        result.Data = new JObject { ["error"] = message, ["available"] = available };
        return result;
    }
}
#endif
