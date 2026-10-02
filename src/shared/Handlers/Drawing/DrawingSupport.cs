#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static class DrawingSupport
{
    internal const string Attributes = "BimwrightDrawing";
    internal static bool AnnotationsAvailable(DrawingSheetStatusBits status) => (status & (DrawingSheetStatusBits.kUnknownOutOfDateDrawingSheet | DrawingSheetStatusBits.kNoDataDrawingSheet)) == 0;
    internal static void RequireAnnotations(Sheet sheet)
    {
        if (!AnnotationsAvailable(sheet.Status)) throw new DrawingFailure(InventorErrorCodes.API_ERROR, "Annotation data unavailable on sheet '" + sheet.Name + "'. Activate the sheet in Inventor and query before writing; no mutation was applied.");
    }
    internal static Application App(InventorCommandContext ctx) => (Application)ctx.Application!;
    internal static DrawingDocument Document(InventorCommandContext ctx, JObject p)
    {
        var d = ActiveDocumentSupport.ResolveTarget(ctx, p, out var fail);
        if (fail != null) throw new ArgumentException(fail.Error!.Message);
        if (d == null) throw new DrawingFailure(InventorErrorCodes.NO_DOCUMENT, "No loaded drawing document.");
        if (d is not DrawingDocument drawing) throw new DrawingFailure(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "A drawing document is required.");
        return drawing;
    }
    internal static Sheet Sheet(DrawingDocument d, JObject p)
    {
        var name = (string?)p["sheet"];
        if (string.IsNullOrWhiteSpace(name)) return d.ActiveSheet;
        var found = d.Sheets.Cast<Sheet>().Where(s => s.Name == name || Read(s.AttributeSets, "code") == name).ToArray();
        if (found.Length != 1) throw new ArgumentException("Sheet must resolve uniquely: " + name);
        return found[0];
    }
    internal static DrawingView View(Sheet sheet, string? name)
    {
        var matches = sheet.DrawingViews.Cast<DrawingView>().Where(v => v.Name == name).ToArray();
        if (matches.Length != 1) throw new ArgumentException("View must resolve uniquely on the selected sheet: " + name);
        return matches[0];
    }
    internal static Point2d Point(Application app, JToken? t)
    {
        DrawingInput.Point(t, 2); return app.TransientGeometry.CreatePoint2d((double)t![0]! / 10, (double)t[1]! / 10);
    }
    internal static string? Read(AttributeSets sets, string key)
    {
        if (!sets.NameIsUsed[Attributes]) return null;
        var set = sets[Attributes]; return set.NameIsUsed[key] ? (string)set[key].Value : null;
    }
    internal static void Write(AttributeSets sets, string key, string value)
    {
        var set = sets.NameIsUsed[Attributes] ? sets[Attributes] : sets.Add(Attributes);
        if (set.NameIsUsed[key]) set[key].Value = value; else set.Add(key, ValueTypeEnum.kStringType, value);
    }
    internal static void Mark(AttributeSets sets, string name, JObject input) { Write(sets, "name", name); Write(sets, "signature", DrawingInput.Signature(input)); }
    internal static bool Existing(AttributeSets sets, JObject p)
    {
        if (Read(sets, "signature") != DrawingInput.Signature(p)) throw new ArgumentException("Name exists with different or unmanaged inputs. Query and edit explicitly.");
        return true;
    }
    internal static void ExistingView(DrawingView v, JObject p)
    {
        Existing(v.AttributeSets, p);
        if (Math.Abs(v.Position.X - (double)p["position_mm"]![0]! / 10) > 1e-6 || Math.Abs(v.Position.Y - (double)p["position_mm"]![1]! / 10) > 1e-6 || DrawingInput.Present(p, "scale") && Math.Abs(v.Scale - p.Value<double>("scale")) > 1e-6 || DrawingInput.Present(p, "style") && v.ViewStyle != Style((string?)p["style"])) throw new ArgumentException("Managed view no longer matches requested position/scale/style; query and edit explicitly.");
    }
    internal static JObject ViewInfo(DrawingView v)
    {
        var result = new JObject { ["name"] = v.Name, ["kind"] = v.ViewType.ToString(), ["scale"] = v.Scale, ["position_mm"] = new JArray(v.Position.X * 10, v.Position.Y * 10), ["box_mm"] = new JObject { ["min"] = new JArray(v.Left * 10, (v.Top - v.Height) * 10), ["max"] = new JArray((v.Left + v.Width) * 10, v.Top * 10) }, ["style"] = v.ViewStyle.ToString(), ["rotation_deg"] = v.Rotation * 180 / Math.PI, ["suppressed"] = v.Suppressed, ["margin_mm"] = v.Margin * 10, ["reference_display"] = v.ReferenceDataDisplayStyle.ToString(), ["hidden_line_all_bodies"] = v.HiddenLineCalculationForAllBodies };
        try { result["model"] = v.ReferencedDocumentDescriptor.FullDocumentName; result["missing_reference"] = v.ReferencedDocumentDescriptor.ReferenceMissing; } catch { result["missing_reference"] = true; }
        try { result["parent_view"] = v.ParentView?.Name; } catch { }
        try { result["design_view"] = v.ActiveDesignViewRepresentation; } catch { }
        return result;
    }
    internal static JObject SheetInfo(Sheet s) => new JObject { ["name"] = s.Name, ["code"] = Read(s.AttributeSets, "code"), ["width_mm"] = s.Width * 10, ["height_mm"] = s.Height * 10, ["border"] = s.Border?.Definition.Name, ["title_block"] = s.TitleBlock?.Definition.Name, ["view_count"] = s.DrawingViews.Count, ["dimension_count"] = s.DrawingDimensions.GeneralDimensions.Count, ["symbol_count"] = s.SketchedSymbols.Count, ["balloon_count"] = s.Balloons.Count };
    internal static InventorCommandResult Success(InventorCommandContext ctx, JToken data) => InventorCommandResult.Success(Guid.Empty, data, new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear });
    internal static InventorCommandResult Atomic(InventorCommandContext ctx, DrawingDocument doc, string title, Func<JToken> action)
    {
        var transaction = App(ctx).TransactionManager.StartTransaction((Inventor._Document)(object)doc, title);
        try { var data = action(); doc.Update(); transaction.End(); return Success(ctx, data); }
        catch (Exception ex)
        {
            var aborted = false; string? abortError = null;
            try { transaction.Abort(); aborted = true; } catch (Exception rollback) { abortError = rollback.Message; }
            return Success(ctx, new JObject { ["ok"] = false, ["error"] = new JObject { ["code"] = ex is ArgumentException ? InventorErrorCodes.INVALID_ARGUMENT : ex is NotSupportedException ? InventorErrorCodes.UNSUPPORTED_HOST : InventorErrorCodes.API_ERROR, ["message"] = ex.Message }, ["rolled_back"] = aborted, ["mutation_applied"] = aborted ? new JValue(false) : JValue.CreateNull(), ["abort_error"] = abortError, ["readback_required"] = !aborted, ["sheet_count"] = doc.Sheets.Count });
        }
    }
    internal static DrawingViewStyleEnum Style(string? s) => s switch { null or "hidden_line_removed" => DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle, "hidden_line" => DrawingViewStyleEnum.kHiddenLineDrawingViewStyle, "shaded" => DrawingViewStyleEnum.kShadedDrawingViewStyle, "shaded_hidden_line" => DrawingViewStyleEnum.kShadedHiddenLineDrawingViewStyle, _ => throw new ArgumentException("Unknown drawing style.") };
}
internal sealed class DrawingFailure : Exception
{
    internal string Code { get; }
    internal DrawingFailure(string code, string message) : base(message) => Code = code;
}
#endif
