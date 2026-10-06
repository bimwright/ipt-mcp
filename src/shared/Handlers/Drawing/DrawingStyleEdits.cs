#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    private static UnitsTypeEnum StyleUnits(string units) => units switch { "mm" => UnitsTypeEnum.kMillimeterLengthUnits, "cm" => UnitsTypeEnum.kCentimeterLengthUnits, "m" => UnitsTypeEnum.kMeterLengthUnits, "inch" => UnitsTypeEnum.kInchLengthUnits, _ => throw new ArgumentException("Unknown units.") };
    private static DisplayFormatEnum StyleFormat(string format) => format switch { "decimal" => DisplayFormatEnum.kDecimalFormat, "fractional" => DisplayFormatEnum.kFractionalNotStackedFormat, "fractional_horizontal" => DisplayFormatEnum.kFractionalHorizontalStackedFormat, "fractional_diagonal" => DisplayFormatEnum.kFractionalDiagonalStackedFormat, _ => throw new ArgumentException("Unknown display format.") };
    private static LineTypeEnum StyleLine(string line) => line switch { "continuous" => LineTypeEnum.kContinuousLineType, "dashed" => LineTypeEnum.kDashedLineType, "dotted" => LineTypeEnum.kDottedLineType, "dash_dotted" => LineTypeEnum.kDashDottedLineType, "hidden" => LineTypeEnum.kDashedHiddenLineType, _ => throw new ArgumentException("Unknown line type.") };
    private static void LocalStyle(object style)
    {
        var location = style switch { TextStyle x => x.StyleLocation, DimensionStyle x => x.StyleLocation, Layer x => x.StyleLocation, ObjectDefaultsStyle x => x.StyleLocation, _ => throw new ArgumentException("Unknown local style type.") };
        if (location == StyleLocationEnum.kLibraryStyleLocation) throw new ArgumentException("Library-only style cannot be edited. Select an existing document-local definition.");
    }
    private static object DefaultStyle(DrawingDocument d, string kind, string name) => kind switch
    {
        "general_note" => Definition(d.StylesManager.TextStyles.Cast<TextStyle>(), name, null, x => x.Name),
        "symbol" => Definition(d.StylesManager.LeaderStyles.Cast<LeaderStyle>(), name, null, x => x.Name),
        "centermark" => Definition(d.StylesManager.CentermarkStyles.Cast<CentermarkStyle>(), name, null, x => x.Name),
        "table" => Definition(d.StylesManager.TableStyles.Cast<TableStyle>(), name, null, x => x.Name),
        _ => Definition(d.StylesManager.DimensionStyles.Cast<DimensionStyle>(), name, null, x => x.Name)
    };
    private static void SetDefaults(ObjectDefaultsStyle x, string kind, object? style, Layer? layer)
    {
        switch (kind)
        {
            case "linear_dimension": if (style != null) x.LinearDimensionStyle = (DimensionStyle)style; if (layer != null) x.LinearDimensionLayer = layer; break;
            case "angular_dimension": if (style != null) x.AngularDimensionStyle = (DimensionStyle)style; if (layer != null) x.AngularDimensionLayer = layer; break;
            case "diameter_dimension": if (style != null) x.DiameterDimensionStyle = (DimensionStyle)style; if (layer != null) x.DiameterDimensionLayer = layer; break;
            case "radial_dimension": if (style != null) x.RadialDimensionStyle = (DimensionStyle)style; if (layer != null) x.RadialDimensionLayer = layer; break;
            case "general_note": if (style != null) x.GeneralNoteStyle = (TextStyle)style; if (layer != null) x.GeneralNoteLayer = layer; break;
            case "leader_note": if (style != null) x.LeaderTextStyle = (DimensionStyle)style; if (layer != null) x.LeaderTextLayer = layer; break;
            case "symbol": if (style != null) x.SketchedSymbolLeaderStyle = (LeaderStyle)style; if (layer != null) x.SketchedSymbolLeaderLayer = layer; break;
            case "centermark": if (style != null) x.CentermarkStyle = (CentermarkStyle)style; if (layer != null) x.CentermarkLayer = layer; break;
            case "table": if (style != null) x.TableStyle = (TableStyle)style; if (layer != null) x.TableLayer = layer; break;
        }
    }
    private static JObject DefaultsInfo(ObjectDefaultsStyle x, string kind)
    {
        var pair = kind switch { "linear_dimension" => (x.LinearDimensionStyle.Name, x.LinearDimensionLayer.Name), "angular_dimension" => (x.AngularDimensionStyle.Name, x.AngularDimensionLayer.Name), "diameter_dimension" => (x.DiameterDimensionStyle.Name, x.DiameterDimensionLayer.Name), "radial_dimension" => (x.RadialDimensionStyle.Name, x.RadialDimensionLayer.Name), "general_note" => (x.GeneralNoteStyle.Name, x.GeneralNoteLayer.Name), "leader_note" => (x.LeaderTextStyle.Name, x.LeaderTextLayer.Name), "symbol" => (x.SketchedSymbolLeaderStyle.Name, x.SketchedSymbolLeaderLayer.Name), "centermark" => (x.CentermarkStyle.Name, x.CentermarkLayer.Name), "table" => (x.TableStyle.Name, x.TableLayer.Name), _ => throw new ArgumentException("Unknown default kind.") };
        return new JObject { ["kind"] = kind, ["style"] = pair.Item1, ["layer"] = pair.Item2 };
    }
    private static InventorCommandResult SetStyles(InventorCommandContext ctx, DrawingDocument d, JObject p)
    {
        var inventory = d.Sheets.Cast<Sheet>().SelectMany(s => Inventory(s)).ToArray();
        var defaults = d.StylesManager.ActiveStandardStyle.ActiveObjectDefaults;
        var plans = ((JObject)p["changes"]!).Properties().SelectMany(group => ((JArray)group.Value).Cast<JObject>().Select(c =>
        {
            object style = group.Name switch { "dimension_styles" => Definition(d.StylesManager.DimensionStyles.Cast<DimensionStyle>(), c.Value<string>("name"), null, x => x.Name), "text_styles" => Definition(d.StylesManager.TextStyles.Cast<TextStyle>(), c.Value<string>("name"), null, x => x.Name), "layers" => Definition(d.StylesManager.Layers.Cast<Layer>(), c.Value<string>("name"), null, x => x.Name), _ => defaults };
            LocalStyle(style);
            var reference = group.Name == "object_defaults" && DrawingInput.Present(c, "style") ? DefaultStyle(d, c.Value<string>("kind")!, c.Value<string>("style")!) : null;
            var layer = DrawingInput.Present(c, "layer") ? Definition(d.StylesManager.Layers.Cast<Layer>(), c.Value<string>("layer"), null, x => x.Name) : null;
            var text = DrawingInput.Present(c, "text_style") ? Definition(d.StylesManager.TextStyles.Cast<TextStyle>(), c.Value<string>("text_style"), null, x => x.Name) : null;
            if (style is DimensionStyle dim && DrawingInput.Present(c, "linear_precision") && !DrawingInput.Present(c, "display_format") && dim.DisplayFormat != DisplayFormatEnum.kDecimalFormat) throw new ArgumentException("linear_precision requires an existing decimal style or display_format=decimal.");
            return (group: group.Name, c, style, reference, layer, text);
        })).ToArray();
        var affected = inventory.Count(item => plans.Any(plan => plan.group switch
        {
            "dimension_styles" => item.Entity is GeneralDimension dim && dim.Style.Name == plan.c.Value<string>("name") || item.Entity is LeaderNote note && note.DimensionStyle.Name == plan.c.Value<string>("name"),
            "text_styles" => item.Entity is GeneralNote note && note.TextStyle.Name == plan.c.Value<string>("name") || item.Entity is GeneralDimension dim && dim.Style.TextStyle.Name == plan.c.Value<string>("name") || item.Entity is LeaderNote leader && leader.DimensionStyle.TextStyle.Name == plan.c.Value<string>("name"),
            "layers" => (item.Entity switch { GeneralDimension dim => dim.Layer.Name, GeneralNote note => note.Layer.Name, LeaderNote leader => leader.Layer.Name, SketchedSymbol symbol => symbol.Layer.Name, Centermark center => center.Layer.Name, CustomTable table => table.Layer.Name, Balloon balloon => balloon.Layer.Name, _ => null }) == plan.c.Value<string>("name"), _ => false
        }));
        return DrawingSupport.Atomic(ctx, d, "Set document drawing styles", () =>
        {
            var rows = new JArray();
            foreach (var plan in plans)
            {
                var c = plan.c; JObject actual;
                switch (plan.style)
                {
                    case DimensionStyle x:
                        if (DrawingInput.Present(c, "units")) x.LinearUnits = StyleUnits(c.Value<string>("units")!);
                        if (DrawingInput.Present(c, "display_format")) x.DisplayFormat = StyleFormat(c.Value<string>("display_format")!);
                        if (DrawingInput.Present(c, "linear_precision")) x.LinearPrecision = (LinearPrecisionEnum)((int)LinearPrecisionEnum.kZeroDecimalPlaceLinearPrecision + c.Value<int>("linear_precision"));
                        if (plan.text != null) x.TextStyle = plan.text;
                        actual = new JObject { ["name"] = x.Name, ["units"] = x.LinearUnits.ToString(), ["display_format"] = x.DisplayFormat.ToString(), ["linear_precision"] = x.LinearPrecision.ToString(), ["text_style"] = x.TextStyle.Name, ["location"] = x.StyleLocation.ToString() };
                        break;
                    case TextStyle x:
                        if (DrawingInput.Present(c, "font")) x.Font = c.Value<string>("font"); if (DrawingInput.Present(c, "size_mm")) x.FontSize = c.Value<double>("size_mm") / 10;
                        if (DrawingInput.Present(c, "bold")) x.Bold = c.Value<bool>("bold"); if (DrawingInput.Present(c, "italic")) x.Italic = c.Value<bool>("italic");
                        actual = new JObject { ["name"] = x.Name, ["font"] = x.Font, ["size_mm"] = x.FontSize * 10, ["bold"] = x.Bold, ["italic"] = x.Italic, ["location"] = x.StyleLocation.ToString() }; break;
                    case Layer x:
                        if (c["color_rgb"] is JArray rgb) x.Color = DrawingSupport.App(ctx).TransientObjects.CreateColor(rgb[0].Value<byte>(), rgb[1].Value<byte>(), rgb[2].Value<byte>());
                        if (DrawingInput.Present(c, "weight_mm")) x.LineWeight = c.Value<double>("weight_mm") / 10;
                        if (DrawingInput.Present(c, "line_type")) x.LineType = StyleLine(c.Value<string>("line_type")!);
                        actual = new JObject { ["name"] = x.Name, ["color_rgb"] = new JArray(x.Color.Red, x.Color.Green, x.Color.Blue), ["weight_mm"] = x.LineWeight * 10, ["line_type"] = x.LineType.ToString(), ["location"] = x.StyleLocation.ToString() }; break;
                    case ObjectDefaultsStyle x: SetDefaults(x, c.Value<string>("kind")!, plan.reference, plan.layer); actual = DefaultsInfo(x, c.Value<string>("kind")!); break;
                    default: throw new ArgumentException("Unknown style type.");
                }
                foreach (var field in c.Properties().Where(x => x.Name != "name" && x.Name != "kind"))
                {
                    JToken expected = field.Name switch {
                        "units" => new JValue(StyleUnits(field.Value.Value<string>()!).ToString()),
                        "display_format" => new JValue(StyleFormat(field.Value.Value<string>()!).ToString()),
                        "linear_precision" => new JValue(((LinearPrecisionEnum)((int)LinearPrecisionEnum.kZeroDecimalPlaceLinearPrecision + field.Value.Value<int>())).ToString()),
                        "line_type" => new JValue(StyleLine(field.Value.Value<string>()!).ToString()), _ => field.Value };
                    var value = actual[field.Name];
                    if (value == null || (expected.Type == JTokenType.Float || expected.Type == JTokenType.Integer) && Math.Abs(value.Value<double>() - expected.Value<double>()) > 0.001 || expected.Type != JTokenType.Float && expected.Type != JTokenType.Integer && !JToken.DeepEquals(value, expected)) throw new InvalidOperationException("Style field readback differs from request: " + field.Name);
                }
                actual["group"] = plan.group; rows.Add(actual);
            }
            return new JObject { ["updated"] = true, ["count"] = rows.Count, ["affected_count"] = affected, ["affected_scope"] = "Supported sheet dimensions, notes and layers; object defaults apply to future objects. Definition/sketch/view-label text is outside this count.", ["items"] = rows, ["global_style_library_written"] = false };
        });
    }
}
#endif
