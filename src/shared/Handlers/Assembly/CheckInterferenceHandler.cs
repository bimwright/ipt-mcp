#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class CheckInterferenceHandler : HandlerBase, IInventorCommand
{
    public string Name => "check_interference";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActiveAssembly(context, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        var occurrencesParam = parameters["occurrences"] as JArray;
        var def = assemblyDoc.ComponentDefinition;
        var coll = app.TransientObjects.CreateObjectCollection();

        if (occurrencesParam == null || occurrencesParam.Count == 0)
        {
            foreach (ComponentOccurrence o in def.Occurrences)
            {
                coll.Add(o);
            }
        }
        else
        {
            foreach (var tok in occurrencesParam)
            {
                string name = (string?)tok ?? "";
                if (!AssemblyRefResolver.TryFindOccurrence(def, name, out var occ, out var err))
                {
                    return Fail(context, "INVALID_ARGUMENT", err!);
                }
                if (occ != null)
                {
                    coll.Add(occ);
                }
            }
        }

        try
        {
            if (coll.Count == 0)
            {
                return Ok(context, new JObject
                {
                    ["count"] = 0,
                    ["bodies"] = 0,
                    ["total_volume_mm3"] = 0.0,
                    ["pairs"] = new JArray()
                });
            }

            InterferenceResults results = def.AnalyzeInterference(coll);
            int rawBodies = results.Count;

            var aggregated = new Dictionary<(string, string), double>();
            double totalVolCm3 = 0;

            for (int i = 1; i <= rawBodies; i++)
            {
                InterferenceResult res = results[i];
                string name1 = res.OccurrenceOne?.Name ?? "Unknown";
                string name2 = res.OccurrenceTwo?.Name ?? "Unknown";
                double volCm3 = res.Volume;

                totalVolCm3 += volCm3;

                // Sort names alphabetically to aggregate symmetrically
                var key = string.Compare(name1, name2, StringComparison.OrdinalIgnoreCase) <= 0
                    ? (name1, name2)
                    : (name2, name1);

                if (aggregated.ContainsKey(key))
                {
                    aggregated[key] += volCm3;
                }
                else
                {
                    aggregated[key] = volCm3;
                }
            }

            var pairsArr = new JArray();
            foreach (var kvp in aggregated)
            {
                pairsArr.Add(new JObject
                {
                    ["a"] = kvp.Key.Item1,
                    ["b"] = kvp.Key.Item2,
                    ["volume_mm3"] = UnitConvert.Cm3ToMm3(kvp.Value)
                });
            }

            return Ok(context, new JObject
            {
                ["count"] = aggregated.Count,
                ["bodies"] = rawBodies,
                ["total_volume_mm3"] = UnitConvert.Cm3ToMm3(totalVolCm3),
                ["pairs"] = pairsArr
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Interference check failed: " + ex.Message);
        }
    }
}
#endif
