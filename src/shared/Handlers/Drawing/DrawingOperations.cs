#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Text.RegularExpressions;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Properties;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using IoPath = System.IO.Path;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    internal static InventorCommandResult Execute(InventorCommandContext ctx, string command, JObject p)
    {
        try
        {
            if (command == "new_drawing") return NewDrawing(ctx, p);
            var d = DrawingSupport.Document(ctx, p);
            if (command == "get_drawing_info") return DrawingSupport.Success(ctx, Info(d, p));
            if (command == "add_sheet") return AddSheet(ctx, d, p);
            var s = DrawingSupport.Sheet(d, p);
            return command switch
            {
                "set_title_block" => SetTitleBlock(ctx, d, s, p),
                "add_drawing_view" => AddView(ctx, d, s, p),
                "add_section_view" => AddSection(ctx, d, s, p),
                "edit_drawing_view" => EditView(ctx, d, s, p),
                "add_drawing_dimension" => AddDimensions(ctx, d, s, p),
                "add_balloon" => AddBalloons(ctx, d, s, p),
                "export_drawing" => Export(ctx, d, p),
                "capture_sheet" => Capture(ctx, d, s, p),
                "add_drawing_note" => AddNote(ctx, d, s, p),
                "add_drawing_table" => AddTable(ctx, d, s, p),
                "add_drawing_symbol" => AddSymbol(ctx, d, s, p),
                "edit_drawing_annotation" => EditAnnotations(ctx, d, s, p),
                "delete_drawing_items" => DeleteItems(ctx, d, s, p),
                "edit_drawing_table" => EditTable(ctx, d, s, p),
                "set_drawing_styles" => SetStyles(ctx, d, p),
                "edit_sheet" => EditSheet(ctx, d, s, p),
                "find_view_geometry" => FindGeometry(ctx, d, s, p),
                "sketch_on_view" => SketchOnView(ctx, d, s, p),
                "hide_view_edges" => HideEdges(ctx, d, s, p),
                _ => throw new ArgumentException("Unknown drawing command.")
            };
        }
        catch (DrawingFailure ex) { return InventorCommandResult.Fail(Guid.Empty, ex.Code, ex.Message, new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear }); }
    }

    private static JObject Page(IEnumerable<JToken> all, int max, int offset)
    {
        var rows = all.ToArray();
        return new JObject { ["items"] = new JArray(rows.Skip(offset).Take(max)), ["total"] = rows.Length, ["offset"] = offset, ["has_more"] = offset + max < rows.Length };
    }
    private static JObject Info(DrawingDocument d, JObject p)
    {
        var max = p.Value<int?>("max_items") ?? 200; var offset = p.Value<int?>("offset") ?? 0;
        var sheets = string.IsNullOrWhiteSpace((string?)p["sheet"]) ? d.Sheets.Cast<Sheet>().ToArray() : new[] { DrawingSupport.Sheet(d, p) };
        var sheetInfo = new List<JToken>();
        foreach (var s in sheets)
        {
            // Cold inactive sheets can expose empty annotation collections until activated.
            // A query must not activate/update the sheet or present unavailable data as zero.
            var status = s.Status;
            var annotationsAvailable = DrawingSupport.AnnotationsAvailable(status);
            var row = DrawingSupport.SheetInfo(s); row["prompts"] = TitlePrompts(s.TitleBlock);
            row["sheet_status_bits"] = (int)status;
            row["annotation_data_available"] = annotationsAvailable;
            if (!annotationsAvailable)
            {
                foreach (var key in new[] { "dimension_count", "symbol_count", "balloon_count", "note_count", "table_count", "centermark_count" }) row[key] = JValue.CreateNull();
                row["readback_hint"] = "Activate the sheet in Inventor, then query again. Do not recreate annotations from unavailable data.";
            }
            row["views"] = Page(s.DrawingViews.Cast<DrawingView>().Select(v => (JToken)DrawingSupport.ViewInfo(v)), max, offset);
            if ((string?)p["include"] == "items" && !annotationsAvailable)
            {
                row["dimensions"] = JValue.CreateNull(); row["symbols"] = JValue.CreateNull();
                row["notes"] = JValue.CreateNull(); row["tables"] = JValue.CreateNull();
                row["centermarks"] = JValue.CreateNull(); row["balloons"] = JValue.CreateNull(); row["contents"] = JValue.CreateNull();
            }
            else if ((string?)p["include"] == "items")
            {
                var inventory = Inventory(s);
                row["contents"] = Page(Inventory(s, true).Select(i => (JToken)i.Target(d, s)), max, offset);
                row["dimensions"] = Page(inventory.Where(i => i.Kind == "dimension").Select(i => { var info = ItemInfo(d, s, i); info["kind"] = ((GeneralDimension)i.Entity).Type.ToString(); return (JToken)info; }), max, offset);
                row["symbols"] = Page(inventory.Where(i => i.Entity is SketchedSymbol).Select(i => (JToken)ItemInfo(d, s, i)), max, offset);
                row["notes"] = Page(inventory.Where(i => i.Kind == "note").Select(i => (JToken)ItemInfo(d, s, i)), max, offset);
                row["centermarks"] = Page(inventory.Where(i => i.Kind == "centermark").Select(i => (JToken)ItemInfo(d, s, i)), max, offset);
                row["balloons"] = Page(inventory.Where(i => i.Kind == "balloon").Select(i => (JToken)ItemInfo(d, s, i)), max, offset);
                row["tables"] = Page(inventory.Where(i => i.Kind == "table").Select(i => { var info = TableQueryInfo((CustomTable)i.Entity, max, offset); info["selection"] = i.Target(d, s); info["locator"] = info["selection"]!["locator"]?.DeepClone(); return (JToken)info; }), max, offset);
            }
            sheetInfo.Add(row);
        }
        var result = new JObject { ["document"] = d.DisplayName, ["path"] = d.FullFileName, ["dirty"] = d.Dirty, ["type"] = d.DocumentType.ToString(), ["projection"] = d.StylesManager.ActiveStandardStyle.FirstAngleProjection ? "first_angle" : "third_angle", ["sheets"] = Page(sheetInfo, max, offset), ["title_block_definitions"] = Page(d.TitleBlockDefinitions.Cast<TitleBlockDefinition>().Select(x => (JToken)new JValue(x.Name)), max, offset), ["border_definitions"] = Page(d.BorderDefinitions.Cast<BorderDefinition>().Select(x => (JToken)new JValue(x.Name)), max, offset), ["symbol_definitions"] = Page(d.SketchedSymbolDefinitions.Cast<SketchedSymbolDefinition>().Select(x => (JToken)new JValue(x.Name)), max, offset), ["dimension_styles"] = Page(d.StylesManager.DimensionStyles.Cast<DimensionStyle>().Select(x => (JToken)new JValue(x.Name)), max, offset) };
        if (p.Value<bool?>("references") != false) result["references"] = Page(d.ReferencedDocumentDescriptors.Cast<DocumentDescriptor>().Select(x => (JToken)new JObject { ["path"] = x.FullDocumentName, ["missing"] = x.ReferenceMissing }), max, offset);
        result["styles"] = new JObject {
            ["text_styles"] = Page(d.StylesManager.TextStyles.Cast<TextStyle>().Select(x => (JToken)new JObject { ["name"] = x.Name, ["location"] = x.StyleLocation.ToString() }), max, offset),
            ["layers"] = Page(d.StylesManager.Layers.Cast<Layer>().Select(x => (JToken)new JObject { ["name"] = x.Name, ["location"] = x.StyleLocation.ToString() }), max, offset),
            ["leader_styles"] = Page(d.StylesManager.LeaderStyles.Cast<LeaderStyle>().Select(x => (JToken)new JObject { ["name"] = x.Name, ["location"] = x.StyleLocation.ToString() }), max, offset),
            ["centermark_styles"] = Page(d.StylesManager.CentermarkStyles.Cast<CentermarkStyle>().Select(x => (JToken)new JObject { ["name"] = x.Name, ["location"] = x.StyleLocation.ToString() }), max, offset),
            ["table_styles"] = Page(d.StylesManager.TableStyles.Cast<TableStyle>().Select(x => (JToken)new JObject { ["name"] = x.Name, ["location"] = x.StyleLocation.ToString() }), max, offset)
        };
        return result;
    }
    private static InventorCommandResult NewDrawing(InventorCommandContext ctx, JObject p)
    {
        var app = DrawingSupport.App(ctx); var name = (string)p["name"]!; var path = (string)p["template"]!;
        if (!IoPath.IsPathFullyQualified(path) || !File.Exists(path) || !new[] { ".idw", ".dwg" }.Contains(IoPath.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("template must be an absolute existing Inventor IDW/native DWG file.");
        var existing = app.Documents.Cast<Inventor.Document>().Where(x => x.DisplayName == name).ToArray();
        if (existing.Length > 0) { if (existing.Length != 1 || existing[0] is not DrawingDocument found) throw new ArgumentException("Document name conflicts."); DrawingSupport.Existing(found.AttributeSets, p); return DrawingSupport.Success(ctx, new JObject { ["created"] = false, ["existing"] = true, ["document"] = found.DisplayName, ["path"] = found.FullFileName }); }
        DrawingDocument? d = null;
        try
        {
            d = (DrawingDocument)app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject, path, p.Value<bool?>("visible") ?? true); d.DisplayName = name;
            if (p["annotation_defaults"] is JObject defaults)
            {
                if (defaults.Properties().Any(x => x.Name != "dimension_style")) throw new ArgumentException("annotation_defaults currently accepts dimension_style only.");
                DrawingInput.Required(defaults, "dimension_style");
                var style = Definition(d.StylesManager.DimensionStyles.Cast<DimensionStyle>(), (string?)defaults["dimension_style"], null, x => x.Name);
                var objectDefaults = d.StylesManager.ActiveStandardStyle.ActiveObjectDefaults;
                objectDefaults.LinearDimensionStyle = style; objectDefaults.AngularDimensionStyle = style; objectDefaults.DiameterDimensionStyle = style; objectDefaults.RadialDimensionStyle = style; objectDefaults.ChainDimensionStyle = style;
            }
            else if (DrawingInput.Present(p, "annotation_defaults")) throw new ArgumentException("annotation_defaults must be {dimension_style:existing_name}.");
            var projection = (string?)p["projection"] ?? "template";
            if (projection != "template") d.StylesManager.ActiveStandardStyle.FirstAngleProjection = projection == "first_angle";
            DrawingSupport.Mark(d.AttributeSets, name, p);
            return DrawingSupport.Success(ctx, new JObject { ["created"] = true, ["document"] = d.DisplayName, ["path"] = d.FullFileName, ["template"] = path, ["projection"] = d.StylesManager.ActiveStandardStyle.FirstAngleProjection ? "first_angle" : "third_angle", ["saved"] = false });
        }
        catch { if (d != null) d.Close(true); throw; }
    }
    private static T Definition<T>(IEnumerable<T> definitions, string? requested, string? templateName, Func<T, string> name)
    {
        var all = definitions.ToArray();
        if (requested == null && templateName != null) requested = templateName;
        var candidates = requested == null ? all : all.Where(x => name(x) == requested).ToArray();
        if (candidates.Length != 1) throw new ArgumentException("Definition must resolve uniquely. Available: " + string.Join(", ", all.Select(name)));
        return candidates[0];
    }
    private static TitleBlockDefinition TitleDefinition(DrawingDocument d, Sheet s, JObject p) => Definition(d.TitleBlockDefinitions.Cast<TitleBlockDefinition>(), (string?)p["title_block"], s.TitleBlock?.Definition.Name, x => x.Name);
    private static InventorCommandResult AddSheet(InventorCommandContext ctx, DrawingDocument d, JObject p)
    {
        var name = (string)p["name"]!; var code = (string?)p["code"] ?? name;
        var found = d.Sheets.Cast<Sheet>().Where(s => s.Name == name || DrawingSupport.Read(s.AttributeSets, "name") == name || DrawingSupport.Read(s.AttributeSets, "code") == code).ToArray();
        if (found.Length > 0) { if (found.Length != 1) throw new ArgumentException("Sheet name/code conflict."); DrawingSupport.Existing(found[0].AttributeSets, p); var row = DrawingSupport.SheetInfo(found[0]); row["created"] = false; return DrawingSupport.Success(ctx, row); }
        var title = TitleDefinition(d, d.ActiveSheet, p);
        var border = Definition(d.BorderDefinitions.Cast<BorderDefinition>(), (string?)p["border"], d.ActiveSheet.Border?.Definition.Name, x => x.Name);
        var promptStrings = Prompts(title.Sketch, p["prompts"] as JObject);
        var borderPrompts = border.IsDefault ? Array.Empty<string>() : Prompts(border.Sketch, null);
        var size = (string?)p["size"] ?? "A1";
        var dimensions = size switch { "A0" => new[] { 1189.0, 841.0 }, "A1" => new[] { 841.0, 594.0 }, "A2" => new[] { 594.0, 420.0 }, "A3" => new[] { 420.0, 297.0 }, "A4" => new[] { 297.0, 210.0 }, _ => new[] { (double)p["width_mm"]!, (double)p["height_mm"]! } };
        var portrait = (string?)p["orientation"] == "portrait";
        if (size != "custom" && portrait) Array.Reverse(dimensions);
        return DrawingSupport.Atomic(ctx, d, "Add drawing sheet", () =>
        {
            var nativeSize = size switch { "A0" => DrawingSheetSizeEnum.kA0DrawingSheetSize, "A1" => DrawingSheetSizeEnum.kA1DrawingSheetSize, "A2" => DrawingSheetSizeEnum.kA2DrawingSheetSize, "A3" => DrawingSheetSizeEnum.kA3DrawingSheetSize, "A4" => DrawingSheetSizeEnum.kA4DrawingSheetSize, _ => DrawingSheetSizeEnum.kCustomDrawingSheetSize };
            var sheet = d.Sheets.Add(nativeSize, portrait ? PageOrientationTypeEnum.kPortraitPageOrientation : PageOrientationTypeEnum.kLandscapePageOrientation, name, size == "custom" ? (object)(dimensions[0] / 10) : Type.Missing, size == "custom" ? (object)(dimensions[1] / 10) : Type.Missing);
            DrawingSupport.Mark(sheet.AttributeSets, name, p); DrawingSupport.Write(sheet.AttributeSets, "code", code);
            if (border.IsDefault) sheet.AddDefaultBorder(); else sheet.AddBorder(border, borderPrompts); sheet.AddTitleBlock(title, PromptStrings: promptStrings);
            var row = DrawingSupport.SheetInfo(sheet); row["created"] = true; row["prompts"] = TitlePrompts(sheet.TitleBlock); return row;
        });
    }
    private static TextBox[] PromptBoxes(DrawingSketch sketch) => sketch.TextBoxes.Cast<TextBox>().Where(x => x.FormattedText.IndexOf("<Prompt", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
    private static string PromptLabel(TextBox box) => Regex.Replace(box.Text, "<[^>]+>", "");
    private static string[] Prompts(DrawingSketch sketch, JObject? values, bool requireAll = true)
    {
        var boxes = PromptBoxes(sketch); var labels = boxes.Select(PromptLabel).ToArray();
        if (labels.Distinct().Count() != labels.Length) throw new ArgumentException("Definition has duplicate prompt labels.");
        if (values != null && values.Properties().Any(x => !labels.Contains(x.Name) || x.Value.Type != JTokenType.String)) throw new ArgumentException("Unknown prompt label/non-string value. Available: " + string.Join(", ", labels));
        if (requireAll && labels.Any(x => values?[x] == null)) throw new ArgumentException("Prompt values required: " + string.Join(", ", labels));
        return labels.Select(x => (string?)values?[x] ?? "").ToArray();
    }
    private static JObject TitlePrompts(TitleBlock? title)
    {
        var result = new JObject(); if (title == null) return result;
        foreach (var b in PromptBoxes(title.Definition.Sketch)) result[PromptLabel(b)] = title.GetResultText(b);
        return result;
    }
    private static InventorCommandResult SetTitleBlock(InventorCommandContext ctx, DrawingDocument d, Sheet sheet, JObject p)
    {
        var def = TitleDefinition(d, sheet, p); var replacing = sheet.TitleBlock == null || sheet.TitleBlock!.Definition.Name != def.Name;
        var prompts = p["prompts"] as JObject; var values = Prompts(def.Sketch, prompts, replacing);
        var props = new List<(Inventor.Property property, JToken value)>();
        if (p["iproperties"] is JArray properties) foreach (var t in properties)
        {
            if (t is not JObject item) throw new ArgumentException("iproperties must contain {set?,name,value}."); DrawingInput.Required(item, "name", "value");
            var name = (string)item["name"]!; var set = PropertyAccess.FindSet((Inventor.Document)(object)d, (string?)item["set"] ?? (name.Equals("Title", StringComparison.OrdinalIgnoreCase) ? "Summary Information" : "Design Tracking Properties"));
            var prop = set == null ? null : PropertyAccess.FindProperty(set, name); if (prop == null) throw new ArgumentException("iProperty not found: " + name);
            if (item["value"] is not JValue) throw new ArgumentException("iProperty value must be scalar."); props.Add((prop, item["value"]!));
        }
        else if (DrawingInput.Present(p, "iproperties")) throw new ArgumentException("iproperties must be an array.");
        return DrawingSupport.Atomic(ctx, d, "Update drawing title block", () =>
        {
            if (replacing) { sheet.TitleBlock?.Delete(); sheet.AddTitleBlock(def, PromptStrings: values); }
            else if (prompts != null) foreach (var b in PromptBoxes(def.Sketch)) if (prompts[PromptLabel(b)] != null) sheet.TitleBlock!.SetPromptResultText(b, (string)prompts[PromptLabel(b)]!);
            foreach (var (property, value) in props) property.Value = ((JValue)value).Value;
            return new JObject { ["updated"] = true, ["sheet"] = sheet.Name, ["title_block"] = sheet.TitleBlock!.Definition.Name, ["prompts"] = TitlePrompts(sheet.TitleBlock), ["iproperties"] = new JArray(props.Select(x => new JObject { ["name"] = x.property.Name, ["value"] = JToken.FromObject(x.property.Value) })) };
        });
    }
}
#endif
