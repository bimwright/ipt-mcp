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

public sealed class GetAssemblyBomHandler : HandlerBase, IInventorCommand
{
    public string Name => "get_assembly_bom";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActiveAssembly(context, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        int maxRows = (int?)parameters["max_rows"] ?? 500;
        if (maxRows <= 0) maxRows = 500;

        try
        {
            var occurrencesArr = new JArray();
            var bomDict = new Dictionary<string, (string partNum, string desc, string path, int qty, double mass)>();
            bool truncated = false;

            WalkOccurrences(
                assemblyDoc.ComponentDefinition.Occurrences,
                1,
                maxRows,
                occurrencesArr,
                bomDict,
                ref truncated
            );

            var bomArr = new JArray();
            foreach (var kvp in bomDict.Values)
            {
                bomArr.Add(new JObject
                {
                    ["part_number"] = kvp.partNum,
                    ["description"] = string.IsNullOrEmpty(kvp.desc) ? null : kvp.desc,
                    ["path"] = kvp.path,
                    ["qty"] = kvp.qty,
                    ["unit_mass_g"] = kvp.mass
                });
            }

            return Ok(context, new JObject
            {
                ["occurrences"] = occurrencesArr,
                ["bom"] = bomArr,
                ["truncated"] = truncated
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to retrieve BOM: " + ex.Message);
        }
    }

    private void WalkOccurrences(
        System.Collections.IEnumerable occurrences,
        int depth,
        int maxRows,
        JArray occurrencesArr,
        Dictionary<string, (string partNum, string desc, string path, int qty, double mass)> bomDict,
        ref bool truncated)
    {
        foreach (ComponentOccurrence occ in occurrences)
        {
            if (occurrencesArr.Count >= maxRows)
            {
                truncated = true;
                return;
            }

            string name = occ.Name;
            string path = "";
            bool suppressed = occ.Suppressed;
            bool grounded = false;
            try { grounded = occ.Grounded; } catch { }

            int transCount = 0;
            int rotCount = 0;

            if (!suppressed)
            {
                try
                {
                    path = ((global::Inventor.Document)occ.Definition.Document).FullFileName;
                }
                catch { }

                try
                {
                    ObjectsEnumerator transVecs;
                    ObjectsEnumerator rotVecs;
                    Point dofCenter;
                    occ.GetDegreesOfFreedom(out transCount, out transVecs, out rotCount, out rotVecs, out dofCenter);
                }
                catch { }
            }

            var item = new JObject
            {
                ["name"] = name,
                ["path"] = string.IsNullOrEmpty(path) ? null : path,
                ["depth"] = depth,
                ["grounded"] = grounded,
                ["dof_translation"] = transCount,
                ["dof_rotation"] = rotCount,
                ["suppressed"] = suppressed
            };
            occurrencesArr.Add(item);

            if (!suppressed)
            {
                try
                {
                    var doc = (global::Inventor.Document)occ.Definition.Document;
                    var propSet = doc.PropertySets["{32853F0F-3444-11d1-9E93-0060B03C1CA6}"];
                    string partNum = propSet["Part Number"]?.Value?.ToString() ?? "";
                    if (string.IsNullOrEmpty(partNum))
                    {
                        partNum = System.IO.Path.GetFileNameWithoutExtension(doc.FullFileName);
                    }
                    string desc = propSet["Description"]?.Value?.ToString() ?? "";
                    double massG = 0;
                    try
                    {
                        if (doc is PartDocument pd)
                        {
                            massG = pd.ComponentDefinition.MassProperties.Mass * 1000.0;
                        }
                        else if (doc is AssemblyDocument ad)
                        {
                            massG = ad.ComponentDefinition.MassProperties.Mass * 1000.0;
                        }
                    }
                    catch { }

                    string bomKey = partNum + "|" + doc.FullFileName;
                    if (bomDict.ContainsKey(bomKey))
                    {
                        var val = bomDict[bomKey];
                        bomDict[bomKey] = (val.partNum, val.desc, val.path, val.qty + 1, val.mass);
                    }
                    else
                    {
                        bomDict[bomKey] = (partNum, desc, doc.FullFileName, 1, massG);
                    }
                }
                catch { }
            }

            if (occ.SubOccurrences != null && occ.SubOccurrences.Count > 0)
            {
                WalkOccurrences(occ.SubOccurrences, depth + 1, maxRows, occurrencesArr, bomDict, ref truncated);
            }
        }
    }
}
#endif
