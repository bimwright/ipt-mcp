using Inventor;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Path = System.IO.Path;
using File = System.IO.File;
using Environment = System.Environment;

internal static class Phase3Live
{
    internal static void Run()
    {
        Inventor.Application? app = null;
        var root = Path.Combine(Path.GetTempPath(), "ipt-phase3-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Console.WriteLine("ROOT " + root);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("CHECK " + message); }
        try
        {
            app = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application", true)!)!;
            app.Visible = true; app.SilentOperation = true;
            Check(app.Documents.Count == 0, "Owned session empty");
            Console.WriteLine("HOST " + app.SoftwareVersion.DisplayVersion + " CANDIDATE " + typeof(Bimwright.Ipt.Shared.Handlers.Drawing.DrawingCommandHandler).Assembly.Location);
            var commands = Bimwright.Ipt.Shared.Plugin.InventorCommandRegistry.Build(new Bimwright.Ipt.Shared.Plugin.PluginOptions(2027, false, false, 5000000));
            var dispatcher = new CommandDispatcher(commands, 5000000);
            var ctx = new InventorCommandContext { Application = app, InventorYear = 2027, TargetId = "phase3-owned-fixture", Commands = commands };
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                if (e.Exception is System.Runtime.InteropServices.COMException or ArgumentException)
                    File.AppendAllText(Path.Combine(root, "native-exceptions.log"), e.Exception + Environment.NewLine);
            };
            JToken Call(string command, JObject input, bool fail = false, bool readOnly = false)
            {
                var callCtx = readOnly ? new InventorCommandContext { Application = app, InventorYear = 2027, TargetId = ctx.TargetId, Commands = commands, ReadOnly = true } : ctx;
                var sw = System.Diagnostics.Stopwatch.StartNew(); var result = dispatcher.Dispatch(callCtx, new InventorCommandEnvelope { Id = Guid.NewGuid(), Command = command, Params = input });
                var json = JsonConvert.SerializeObject(new { command, duration_ms = sw.ElapsedMilliseconds, result }); File.AppendAllText(Path.Combine(root, "results.jsonl"), json + Environment.NewLine); Console.WriteLine(json);
                if (command == "sketch_on_view" && !result.Ok) Console.WriteLine("UI " + JsonConvert.SerializeObject(new { doc = app.ActiveDocument?.DisplayName, edit_type = app.ActiveEditObject == null ? "null" : ((dynamic)app.ActiveEditObject).Type.ToString(), selection = app.ActiveDocument?.SelectSet.Count }));
                if (result.Ok == fail) throw new Exception("Unexpected " + command + " outcome: " + result.Error?.Message);
                return result.Data ?? JObject.FromObject(result);
            }
            var tg = app.TransientGeometry;
            var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject), true);
            var sk = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]); sk.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(0, 0), tg.CreatePoint2d(10, 6));
            var ex = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(sk.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation); ex.SetDistanceExtent(2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection); part.ComponentDefinition.Features.ExtrudeFeatures.Add(ex);
            var hole = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]); hole.SketchCircles.AddByCenterRadius(tg.CreatePoint2d(5, 3), 1);
            var cut = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(hole.Profiles.AddForSolid(), PartFeatureOperationEnum.kCutOperation); cut.SetDistanceExtent(2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection); part.ComponentDefinition.Features.ExtrudeFeatures.Add(cut);
            var partPath = Path.Combine(root, "block.ipt"); part.SaveAs(partPath, false);
            var drawing = (DrawingDocument)app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject, Path.ChangeExtension(app.FileManager.GetTemplateFile(DocumentTypeEnum.kDrawingDocumentObject), ".idw"), true);
            var sheet = drawing.ActiveSheet;
            var view = sheet.DrawingViews.AddBaseView((_Document)(object)part, tg.CreatePoint2d(12, 12), 1, ViewOrientationTypeEnum.kFrontViewOrientation, DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); view.Name = "Front";
            var other = sheet.DrawingViews.AddBaseView((_Document)(object)part, tg.CreatePoint2d(30, 12), 0.5, ViewOrientationTypeEnum.kFrontViewOrientation, DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); other.Name = "Other";
            drawing.Update(); var drawingPath = Path.Combine(root, "phase3.idw"); drawing.SaveAs(drawingPath, false);
            JObject Drawing(JObject p) { p["document"] = drawing.DisplayName; p["sheet"] = sheet.Name; return p; }
            JObject Region(string name = "Front") => Drawing(JObject.Parse("{view:'" + name + "',region_mm:{min:[0,0],max:[1000,1000]},visible_only:false,max_items:1000}"));
            var from = Environment.GetEnvironmentVariable("IPT_PHASE3_FROM") ?? "geometry";
            Console.WriteLine("FROM " + from);
            var query = Call("find_view_geometry", Region(), readOnly: true); var rows = (JArray)query["geometry"]!["items"]!;
            if (from == "geometry")
            {
            Check(!drawing.Dirty && rows.Any(x => x.Value<string>("kind") == "line") && rows.Any(x => x.Value<string>("kind") == "circle"), "Query read-only with known lines/circle");
            var paging = Region(); paging["max_items"] = 1; paging["offset"] = 1; var paged = Call("find_view_geometry", paging);
            Check(paged["geometry"]!["items"]!.Count() == 1 && paged["geometry"]!.Value<bool>("has_more") && paged.Value<string>("revision") == query.Value<string>("revision"), "Paging and stable revision");
            var circle = rows.First(x => x.Value<string>("kind") == "circle" && x.Value<bool>("intent_supported"));
            var intent = new JObject { ["geometry_id"] = circle["geometry_id"]!.DeepClone(), ["revision"] = query["revision"]!.DeepClone(), ["point_intent"] = "center" };
            var dim = Drawing(new JObject { ["items"] = new JArray(new JObject { ["name"] = "Diameter", ["view"] = view.Name, ["kind"] = "diameter", ["intents"] = new JArray(intent), ["text_position_mm"] = new JArray(140, 150) }) });
            Check(Call("add_drawing_dimension", dim)["items"]![0]!.Value<bool>("attached"), "Geometry ID creates attached dimension");
            view.Scale = 0.75; drawing.Update();
            ((JObject)dim["items"]![0]!)["name"] = "Stale"; var dimensionsBefore = sheet.DrawingDimensions.GeneralDimensions.Count;
            Call("add_drawing_dimension", dim, fail: true); Check(sheet.DrawingDimensions.GeneralDimensions.Count == dimensionsBefore, "Manual scale invalidates ID before mutation");
            }
            if (from == "geometry" || from == "sketch")
            {
            var entities = JArray.Parse("[{line:{from:[100,100],to:[120,105]}},{circle:{center:[115,115],radius_mm:3}},{arc:{center:[125,115],radius_mm:4,start_deg:20,sweep_deg:-90}},{text:{text:'<literal> & text',position:[105,125],font_size_mm:3,rotation_deg:15}}]");
            var markup = Drawing(new JObject { ["name"] = "Markup", ["view"] = "Front", ["entities"] = entities, ["layer"] = drawing.StylesManager.Layers[1].Name, ["color_rgb"] = new JArray(20, 80, 160), ["weight_mm"] = 0.3 });
            drawing.SelectSet.Select(view); var sketch = Call("sketch_on_view", markup);
            Check(sketch.Value<int>("entity_count") == 4 && sketch.Value<bool>("ui_restored"), "All sketch primitives and selection restored");
            Check(!Call("sketch_on_view", markup).Value<bool>("created"), "Sketch repeat reuses native content");
            var conflict = (JObject)markup.DeepClone(); ((JObject)conflict["entities"]![0]!["line"]!)["to"] = new JArray(130, 105); Call("sketch_on_view", conflict, fail: true);
            Call("sketch_on_view", Drawing(JObject.Parse("{name:'SheetMark',entities:[{line:{from:[30,30],to:[40,30]}}]}")));
            Call("sketch_on_view", Drawing(JObject.Parse("{name:'LocalMark',view:'Front',space:'view_local',entities:[{line:{from:[0,0],to:[10,0]}}]}")));
            var viewSketch = view.Sketches.Cast<DrawingSketch>().Single(x => x.Name == "LocalMark"); var sheetSketch = sheet.Sketches.Cast<DrawingSketch>().Single(x => x.Name == "SheetMark");
            var beforeLocal = viewSketch.SketchToSheetSpace(viewSketch.SketchLines[1].EndSketchPoint.Geometry); var beforeSheet = sheetSketch.SketchToSheetSpace(sheetSketch.SketchLines[1].EndSketchPoint.Geometry);
            view.Position = tg.CreatePoint2d(view.Position.X + 2, view.Position.Y); drawing.Update();
            Check(viewSketch.SketchToSheetSpace(viewSketch.SketchLines[1].EndSketchPoint.Geometry).DistanceTo(beforeLocal) > 1 && sheetSketch.SketchToSheetSpace(sheetSketch.SketchLines[1].EndSketchPoint.Geometry).DistanceTo(beforeSheet) < 1e-6, "View sketch tracks parent; sheet sketch stays fixed");
            }
            if (from is "geometry" or "sketch" or "hide")
            {
            var otherState = other.DrawingCurves[Type.Missing].Cast<DrawingCurve>().SelectMany(c => c.Segments.Cast<DrawingCurveSegment>()).Select(s => s.Visible).ToArray();
            var hide = Drawing(JObject.Parse("{view:'Front',max_model_size_mm:1000,dry_run:true}")); var preview = Call("hide_view_edges", hide); Check(preview.Value<int>("candidate_count") > 0, "Hide preview candidates");
            hide["dry_run"] = false; var hidden = Call("hide_view_edges", hide); Check(hidden.Value<int>("hidden_count") == preview.Value<int>("candidate_count"), "Hidden count equals preview");
            Check(Call("hide_view_edges", hide).Value<int>("hidden_count") == 0 && other.DrawingCurves[Type.Missing].Cast<DrawingCurve>().SelectMany(c => c.Segments.Cast<DrawingCurveSegment>()).Select(s => s.Visible).SequenceEqual(otherState), "Repeat skips hidden; second view unchanged");
            }
            foreach (var command in new[] { "sketch_on_view", "hide_view_edges", "create_design_view" }) Call(command, new JObject(), fail: true, readOnly: true);
            var sub = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kAssemblyDocumentObject), true); var child = sub.ComponentDefinition.Occurrences.Add(partPath, tg.CreateMatrix()); child.Name = "Child:1"; var subPath = Path.Combine(root, "sub.iam"); sub.SaveAs(subPath, false);
            var assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kAssemblyDocumentObject), true); var nested = assembly.ComponentDefinition.Occurrences.Add(subPath, tg.CreateMatrix()); nested.Name = "Sub:1"; var topMatrix = tg.CreateMatrix(); topMatrix.SetTranslation(tg.CreateVector(20, 0, 0)); var top = assembly.ComponentDefinition.Occurrences.Add(partPath, topMatrix); top.Name = "Top:1";
            if (from == "extended")
            {
                var primary = assembly.ComponentDefinition.RepresentationsManager.ActiveDesignViewRepresentation;
                var defaultCopy = new JObject { ["document"] = assembly.DisplayName, ["name"] = "DefaultCopy" };
                Check(Call("create_design_view", defaultCopy).Value<bool>("created") && primary.Equals(assembly.ComponentDefinition.RepresentationsManager.ActiveDesignViewRepresentation), "Default Primary source copies and restores");
                Check(!Call("create_design_view", defaultCopy).Value<bool>("created"), "Default-source copy is idempotent");
            }
            var source = assembly.ComponentDefinition.RepresentationsManager.DesignViewRepresentations.Add("Source"); source.Activate(); var originalAsset = top.Appearance.Name; var originalVisible = top.Visible;
            var asset = assembly.Assets.Add(AssetTypeEnum.kAssetTypeAppearance, "Generic", "Phase3Blue", "Phase3Blue"); ((ColorAssetValue)asset["generic_diffuse"]).Value = app.TransientObjects.CreateColor(10, 30, 200); var assetName = asset.Name;
            var assemblyPath = Path.Combine(root, "phase3.iam"); assembly.SaveAs(assemblyPath, false);
            if (from is "extended" or "nested")
            {
                var assemblyView = sheet.DrawingViews.AddBaseView((_Document)(object)assembly, tg.CreatePoint2d(20, 22), 0.3, ViewOrientationTypeEnum.kFrontViewOrientation, DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); assemblyView.Name = "Assembly"; drawing.Activate(); drawing.Update();
                var nestedQuery = Drawing(JObject.Parse("{view:'Assembly',occurrence_path:'Sub:1/Child:1',visible_only:false,max_items:1000}"));
                var nestedRows = (JArray)Call("find_view_geometry", nestedQuery)["geometry"]!["items"]!;
                Check(nestedRows.Count == 5 && nestedRows.All(row => row.Value<bool>("intent_supported") && row.Value<string>("occurrence_path") == "Sub:1/Child:1"), "Nested query returns resolvable model references and exact path");
                var topSegments = assemblyView.DrawingCurves[top].Cast<DrawingCurve>().SelectMany(curve => curve.Segments.Cast<DrawingCurveSegment>()).ToArray(); var topVisibility = topSegments.Select(segment => segment.Visible).ToArray();
                var nestedHide = Drawing(JObject.Parse("{view:'Assembly',occurrences:['Sub:1/Child:1'],max_model_size_mm:70,dry_run:true}"));
                var nestedPreview = Call("hide_view_edges", nestedHide); Check(nestedPreview.Value<int>("candidate_count") == 3, "Nested occurrence AND model-size threshold selects three segments");
                nestedHide["dry_run"] = false; Check(Call("hide_view_edges", nestedHide).Value<int>("hidden_count") == 3 && topSegments.Select(segment => segment.Visible).SequenceEqual(topVisibility), "Nested hiding preserves unselected occurrence");
                Call("sketch_on_view", Drawing(JObject.Parse("{name:'ScaleLocal',view:'Other',space:'view_local',entities:[{line:{from:[0,0],to:[10,0]}}]}")));
                Call("sketch_on_view", Drawing(JObject.Parse("{name:'ScaleSheet',entities:[{line:{from:[10,10],to:[20,10]}}]}")));
                var scaleLocal = other.Sketches.Cast<DrawingSketch>().Single(sketch => sketch.Name == "ScaleLocal"); var scaleSheet = sheet.Sketches.Cast<DrawingSketch>().Single(sketch => sketch.Name == "ScaleSheet");
                double Length(DrawingSketch sketch) => sketch.SketchToSheetSpace(sketch.SketchLines[1].StartSketchPoint.Geometry).DistanceTo(sketch.SketchToSheetSpace(sketch.SketchLines[1].EndSketchPoint.Geometry));
                var localLength = Length(scaleLocal); var sheetLength = Length(scaleSheet); other.Scale *= 2; drawing.Update();
                Check(Math.Abs(Length(scaleLocal) - localLength * 2) < 1e-6 && Math.Abs(Length(scaleSheet) - sheetLength) < 1e-6, "View markup scales while sheet markup stays fixed");
                scaleSheet.Edit(); try { Call("sketch_on_view", Drawing(JObject.Parse("{name:'InEdit',entities:[{line:{from:[0,0],to:[1,0]}}]}")), fail: true); } finally { scaleSheet.ExitEdit(); }
                assembly.Activate();
                Console.WriteLine("PASS phase3 extended native fixture " + root);
                return;
            }
            var design = new JObject { ["document"] = assembly.DisplayName, ["name"] = "Detail", ["source"] = "Source", ["occurrence_visibility"] = JArray.Parse("[{occurrence_path:'Sub:1/Child:1',visible:false}]"), ["appearance"] = JArray.Parse("[{occurrence_path:'Top:1',asset:'Phase3Blue'}]") };
            var representation = Call("create_design_view", design); Check(representation.Value<bool>("created") && source.Equals(assembly.ComponentDefinition.RepresentationsManager.ActiveDesignViewRepresentation) && top.Visible == originalVisible && top.Appearance.Name == originalAsset, "Design view preserves original state");
            Check(!Call("create_design_view", design).Value<bool>("created"), "Design view repeats without duplicate");
            design["activate"] = true; Call("create_design_view", design); Check(top.Appearance.Name == assetName && !nested.SubOccurrences[1].Visible, "Nested visibility and assembly appearance persist on activation");
            var invalid = (JObject)design.DeepClone(); invalid["name"] = "Invalid"; invalid["appearance"] = JArray.Parse("[{occurrence_path:'Missing:1',asset:'Missing'}]"); var repCount = assembly.ComponentDefinition.RepresentationsManager.DesignViewRepresentations.Count; Call("create_design_view", invalid, fail: true); Check(repCount == assembly.ComponentDefinition.RepresentationsManager.DesignViewRepresentations.Count, "Bad target fails before representation creation");
            drawing.Save(); assembly.Save(); drawing.Close(true); assembly.Close(true);
            assembly = (AssemblyDocument)app.Documents.Open(assemblyPath, true); var detail = assembly.ComponentDefinition.RepresentationsManager.DesignViewRepresentations.Cast<DesignViewRepresentation>().Single(x => x.Name == "Detail"); detail.Activate();
            Check(assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().Single(x => x.Name == "Top:1").Appearance.Name == assetName && !assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().Single(x => x.Name == "Sub:1").SubOccurrences[1].Visible, "Saved design view survives reopen");
            drawing = (DrawingDocument)app.Documents.Open(drawingPath, true); sheet = drawing.ActiveSheet; var reopened = Call("find_view_geometry", Region(), readOnly: true);
            Check(reopened.Value<string>("revision") != query.Value<string>("revision"), "Reopened drawing rejects prior lifetime revision");
            Console.WriteLine("PASS phase3 four-tool native fixture " + root);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { if (app != null) try { app.Quit(); } catch (Exception ex) { Console.Error.WriteLine("Owned-session cleanup failed: " + ex); Environment.ExitCode = 1; } }
    }
}
