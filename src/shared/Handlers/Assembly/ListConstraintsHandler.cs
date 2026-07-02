#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class ListConstraintsHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_constraints";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActiveAssembly(context, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        try
        {
            var constraintsArr = new JArray();
            var def = assemblyDoc.ComponentDefinition;

            foreach (AssemblyConstraint c in def.Constraints)
            {
                string type = "other";
                if (c is MateConstraint) type = "mate";
                else if (c is FlushConstraint) type = "flush";
                else if (c is InsertConstraint) type = "insert";
                else if (c is AngleConstraint) type = "angle";

                string health = AssemblyRefResolver.HealthToString(c.HealthStatus);

                string? occName1 = null;
                string? occName2 = null;
                try { if (c.OccurrenceOne != null) occName1 = c.OccurrenceOne.Name; } catch { }
                try { if (c.OccurrenceTwo != null) occName2 = c.OccurrenceTwo.Name; } catch { }

                string kind1 = "unknown";
                string kind2 = "unknown";
                try
                {
                    object ent1 = ((dynamic)c).EntityOne;
                    if (ent1 != null)
                    {
                        if (ent1 is Face) kind1 = "face";
                        else if (ent1 is Edge) kind1 = "edge";
                        else if (ent1 is WorkPlane) kind1 = "work_plane";
                        else if (ent1 is WorkAxis) kind1 = "work_axis";
                        else if (ent1 is WorkPoint) kind1 = "work_point";
                    }
                }
                catch { }
                try
                {
                    object ent2 = ((dynamic)c).EntityTwo;
                    if (ent2 != null)
                    {
                        if (ent2 is Face) kind2 = "face";
                        else if (ent2 is Edge) kind2 = "edge";
                        else if (ent2 is WorkPlane) kind2 = "work_plane";
                        else if (ent2 is WorkAxis) kind2 = "work_axis";
                        else if (ent2 is WorkPoint) kind2 = "work_point";
                    }
                }
                catch { }

                var item = new JObject
                {
                    ["name"] = c.Name,
                    ["type"] = type,
                    ["health"] = health,
                    ["suppressed"] = c.Suppressed,
                    ["entity_one"] = new JObject
                    {
                        ["occurrence"] = occName1,
                        ["kind"] = kind1
                    },
                    ["entity_two"] = new JObject
                    {
                        ["occurrence"] = occName2,
                        ["kind"] = kind2
                    }
                };

                constraintsArr.Add(item);
            }

            return Ok(context, new JObject
            {
                ["constraints"] = constraintsArr
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to list constraints: " + ex.Message);
        }
    }
}
#endif
