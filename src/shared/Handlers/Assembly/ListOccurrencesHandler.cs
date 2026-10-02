#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// <c>list_occurrences</c> (E1) — read-only. Selector-filtered occurrence report for the target
/// assembly: the typed replacement for the "walk occurrences, filter by name, read RangeBox /
/// transform / suppression" scripts. <c>fields</c> picks columns (default <see cref="DefaultFields"/>,
/// "all" = every column); <c>output=file</c> writes the rows to a spill file and returns its path.
/// Inline output above 64 KiB spills automatically.
/// </summary>
public sealed class ListOccurrencesHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_occurrences";
    public bool IsReadOnly => true;

    public static readonly string[] AllFields =
    {
        "name", "path", "depth", "file", "type", "leaf", "suppressed", "visible", "grounded",
        "bbox_mm", "transform", "material", "appearance", "mass_g", "volume_mm3",
    };

    public static readonly string[] DefaultFields = { "name", "path", "depth", "file", "suppressed", "visible", "grounded", "bbox_mm" };

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetAssembly(ctx, p, Name, out _, out var asm, out var failure))
            return failure!;

        if (!TryFields(p["fields"], out var fields, out var fieldError))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, fieldError!);

        var output = (string?)p["output"] ?? "inline";
        if (output != "inline" && output != "file")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "output must be inline or file");

        if (!OccurrenceSelection.TryResolve(asm.ComponentDefinition, p["selector"], "selector", out var items, out var error,
                allowEmpty: true))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, error!);

        if (items.Count == 0 && p["selector"] is { Type: not JTokenType.Null }
            && !OccurrenceSelection.TryResolve(asm.ComponentDefinition, p["selector"], "selector", out _, out var why))
            return Ok(ctx, new JObject { ["assembly"] = asm.DisplayName, ["count"] = 0, ["occurrences"] = new JArray(), ["note"] = why });

        var rows = new JArray();
        foreach (var it in items) rows.Add(Row(it, fields));

        var data = new JObject
        {
            ["assembly"] = asm.DisplayName,
            ["count"] = rows.Count,
            ["fields"] = new JArray(fields),
        };
        if (output == "file")
        {
            var file = new ResponseSpillWriter().Write(Name, ".json", rows.ToString(Formatting.None));
            data["file"] = file;
            data["preview"] = new JArray(rows.Take(10));
            return Ok(ctx, data);
        }
        ResponseSpillWriter.AttachResults(Name, data, rows, ResponseSpillWriterFactory.ForContext(ctx));
        if (data["results"] is JArray inline)
        {
            data.Remove("results");
            data["occurrences"] = inline;
        }
        return Ok(ctx, data);
    }

    internal static bool TryFields(JToken? token, out string[] fields, out string? error)
    {
        error = null;
        fields = DefaultFields;
        if (token is null || token.Type == JTokenType.Null) return true;
        var list = token.Type == JTokenType.String ? new List<string> { (string)token! }
                 : token is JArray a && a.All(t => t.Type == JTokenType.String) ? a.Select(t => (string)t!).ToList()
                 : null;
        if (list is null) { error = "fields must be an array of field names"; return false; }
        if (list.Count == 1 && list[0] == "all") { fields = AllFields; return true; }
        var bad = list.Where(f => !AllFields.Contains(f)).ToList();
        if (bad.Count > 0) { error = $"unknown field(s): {string.Join(", ", bad)}. Available: {string.Join(", ", AllFields)} (or \"all\")"; return false; }
        fields = list.Distinct().ToArray();
        return true;
    }

    internal static JObject Row(OccurrenceSelection.Item it, IReadOnlyCollection<string> fields)
    {
        var o = it.Occurrence;
        var row = new JObject();
        foreach (var f in fields)
        {
            try
            {
                switch (f)
                {
                    case "name": row["name"] = it.Name; break;
                    case "path": row["path"] = it.Path; break;
                    case "depth": row["depth"] = it.Depth; break;
                    case "file": row["file"] = it.File; break;
                    case "type": row["type"] = o.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject ? "assembly" : "part"; break;
                    case "leaf": row["leaf"] = it.IsLeaf; break;
                    case "suppressed": row["suppressed"] = it.Suppressed; break;
                    case "visible": row["visible"] = o.Visible; break;
                    case "grounded": row["grounded"] = o.Grounded; break;
                    case "bbox_mm": row["bbox_mm"] = it.Suppressed ? null : OccurrenceSelection.BboxMm(o.RangeBox); break;
                    case "transform": row["transform"] = OccurrenceSelection.Transform(o.Transformation); break;
                    case "material": row["material"] = Material(o); break;
                    case "appearance":
                        row["appearance"] = new JObject
                        {
                            ["name"] = o.Appearance?.DisplayName,
                            ["source"] = EnumText.Friendly(o.AppearanceSourceType.ToString(), "Appearance"),
                        };
                        break;
                    case "mass_g": row["mass_g"] = it.Suppressed ? null : Math.Round(UnitConvert.KgToG(o.MassProperties.Mass), 3); break;
                    case "volume_mm3": row["volume_mm3"] = it.Suppressed ? null : Math.Round(UnitConvert.Cm3ToMm3(o.MassProperties.Volume), 3); break;
                }
            }
            catch (Exception ex)
            {
                row[f] = null;
                row[f + "_error"] = ex.Message;
            }
        }
        return row;
    }

    private static string? Material(ComponentOccurrence o)
    {
        if (o.DefinitionDocumentType != DocumentTypeEnum.kPartDocumentObject) return null;
        var part = (PartDocument)o.Definition.Document;
        return part.ActiveMaterial?.DisplayName;
    }
}
#endif
