using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Handlers.Drawing;
using Bimwright.Ipt.Shared.Infrastructure;
using Path = System.IO.Path;
static class HandlerLive
{
    private static Inventor.Edge EntityResolverEdge(PartDocument part, string reference) => part.ComponentDefinition.SurfaceBodies[1].Edges[int.Parse(reference.Split(':').Last())];
    public static void Run()
    {
        Inventor.Application? app = null;
        var root = Path.Combine(Path.GetTempPath(), "ipt-drawing-fixture-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(root);
        System.Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", root);
        try
        {
            app = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application", true)!)!; app.Visible = true; app.SilentOperation = true;
            if (app.Documents.Count != 0) throw new Exception("Owned fixture session is not empty.");
            var commands = Bimwright.Ipt.Shared.Plugin.InventorCommandRegistry.Build(new Bimwright.Ipt.Shared.Plugin.PluginOptions(2027, false, false, 5000000));
            var dispatcher = new CommandDispatcher(commands, 5000000);
            var ctx = new InventorCommandContext { Application = app, InventorYear = 2027, TargetId = "isolated-fixture", Commands = commands }; var tg = app.TransientGeometry;
            JToken Call(string command, JObject p, bool expectFailure = false, bool readOnly = false)
            {
                var callContext = readOnly ? new InventorCommandContext { Application = app, InventorYear = 2027, TargetId = "isolated-fixture", Commands = commands, ReadOnly = true } : ctx;
                var sw = System.Diagnostics.Stopwatch.StartNew(); var r = dispatcher.Dispatch(callContext, new Bimwright.Ipt.Shared.Contracts.InventorCommandEnvelope { Id = Guid.NewGuid(), Command = command, Params = p }); Console.WriteLine(command + " " + sw.ElapsedMilliseconds + "ms " + JsonConvert.SerializeObject(r));
                System.IO.File.AppendAllText(Path.Combine(root, "results.jsonl"), JsonConvert.SerializeObject(new { command, duration_ms = sw.ElapsedMilliseconds, result = r }) + System.Environment.NewLine);
                var ok = r.Ok && r.Data?.Value<bool?>("ok") != false;
                if (ok == expectFailure) throw new Exception(command + " unexpected result.");
                if (ok && (command == "add_section_view" || command == "add_drawing_view") && r.Data is JObject v && p["position_mm"] is JArray requested) { var actual = (JArray)v["position_mm"]!; if (Math.Abs((double)actual[0] - (double)requested[0]) > 0.01 || Math.Abs((double)actual[1] - (double)requested[1]) > 0.01) throw new Exception("View position readback mismatch."); }
                return r.Data ?? new JObject { ["ok"] = r.Ok, ["error"] = r.Error == null ? null : JObject.FromObject(r.Error) };
            }
            var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject), true);
            var sketch = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]); sketch.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(0, 0), tg.CreatePoint2d(10, 6));
            var ex = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(sketch.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation); ex.SetDistanceExtent(2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection); part.ComponentDefinition.Features.ExtrudeFeatures.Add(ex);
            var hole = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]); hole.SketchCircles.AddByCenterRadius(tg.CreatePoint2d(5, 3), 1);
            var cut = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(hole.Profiles.AddForSolid(), PartFeatureOperationEnum.kCutOperation); cut.SetDistanceExtent(2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection); part.ComponentDefinition.Features.ExtrudeFeatures.Add(cut);
            var modelPath = Path.Combine(root, "block.ipt"); part.SaveAs(modelPath, false);
            var sub = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kAssemblyDocumentObject), true); var occurrence = sub.ComponentDefinition.Occurrences.Add(modelPath, tg.CreateMatrix()); occurrence.Name = "block:1"; occurrence.Grounded = true; var subPath = Path.Combine(root, "sub.iam"); sub.SaveAs(subPath, false);
            var assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kAssemblyDocumentObject), true); var nested = assembly.ComponentDefinition.Occurrences.Add(subPath, tg.CreateMatrix()); nested.Name = "sub:1"; nested.Grounded = true; assembly.ComponentDefinition.RepresentationsManager.DesignViewRepresentations.Add("FixtureView"); var assemblyPath = Path.Combine(root, "assembly.iam"); assembly.SaveAs(assemblyPath, false);
            var templateDoc = (DrawingDocument)app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject, Path.ChangeExtension(app.FileManager.GetTemplateFile(DocumentTypeEnum.kDrawingDocumentObject), ".idw"), true);
            var definition = templateDoc.SketchedSymbolDefinitions.Add("FixtureBalloon"); definition.Edit(out var symbolSketch); symbolSketch.SketchCircles.AddByCenterRadius(tg.CreatePoint2d(0, 0), 0.4); symbolSketch.TextBoxes.AddFitted(tg.CreatePoint2d(0, 0), "<Prompt>ITEM</Prompt>"); definition.ExitEdit();
            var titleDef = templateDoc.TitleBlockDefinitions.Add("FixtureTitle"); titleDef.Edit(out var titleSketch); titleSketch.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(0, 0), tg.CreatePoint2d(10, 2)); titleSketch.TextBoxes.AddFitted(tg.CreatePoint2d(0.5, 0.5), "<Prompt>DRAWING TITLE</Prompt>"); titleDef.ExitEdit();
            var template = Path.Combine(root, "fixture-template.idw"); templateDoc.SaveAs(template, false); templateDoc.Close(true);
            var np = new JObject { ["name"] = "Phase1-fixture.idw", ["template"] = template, ["projection"] = "third_angle", ["visible"] = true, ["annotation_defaults"] = new JObject { ["dimension_style"] = "Default - mm (ANSI)" } }; Call("new_drawing", np); Call("new_drawing", np);
            var d = (DrawingDocument)app.ActiveDocument; var doc = d.DisplayName; var initial = d.ActiveSheet.Name; var info = Call("get_drawing_info", new JObject { ["document"] = doc, ["include"] = "items" }); Console.WriteLine("INFO_DIRTY " + d.Dirty);
            var addSheet = new JObject { ["document"] = doc, ["name"] = "Second", ["code"] = "P2", ["size"] = "A3", ["orientation"] = "landscape", ["title_block"] = "FixtureTitle", ["prompts"] = new JObject { ["DRAWING TITLE"] = "Initial title" } };
            var second = Call("add_sheet", addSheet); var sh = (string)second["name"]!; Call("add_sheet", addSheet);
            var baseInput = new JObject { ["document"] = doc, ["sheet"] = sh, ["name"] = "front", ["kind"] = "base", ["model"] = modelPath, ["position_mm"] = new JArray(120, 120), ["scale"] = 1, ["style"] = "hidden_line_removed", ["margin_mm"]=5, ["reference_display"]="as_part", ["hidden_line_all_bodies"]=true }; Call("add_drawing_view", baseInput); Call("add_drawing_view", baseInput);
            Call("add_drawing_view", new JObject { ["document"] = doc, ["sheet"] = sh, ["name"] = "projected", ["kind"] = "projected", ["parent_view"] = "front", ["position_mm"] = new JArray(270, 120) });
            Call("add_drawing_view", new JObject { ["document"] = doc, ["sheet"] = sh, ["name"] = "arbitrary", ["kind"] = "arbitrary", ["model"] = modelPath, ["position_mm"] = new JArray(270, 230), ["eye_direction"] = new JArray(1, 1, 1), ["up_direction"] = new JArray(0, 0, 1), ["scale"] = 0.5 });
            Call("add_drawing_view", new JObject { ["document"] = doc, ["sheet"] = sh, ["name"] = "detail", ["kind"] = "detail", ["parent_view"] = "front", ["position_mm"] = new JArray(350, 230), ["detail_region_mm"] = new JObject { ["center"] = new JArray(120, 120), ["radius_mm"] = 15 }, ["scale"] = 2 });
            Call("add_section_view", new JObject { ["document"] = doc, ["sheet"] = sh, ["name"] = "section", ["parent_view"] = "front", ["position_mm"] = new JArray(120, 230), ["cut_line_mm"] = new JArray(new JArray(120, 80), new JArray(120, 160)), ["direction"] = "negative", ["depth_mm"] = 10, ["rotation_deg"] = 90 });
            var dims = new JArray();
            JObject Edge(string edge, string point) => new() { ["model_edge"] = edge, ["point_intent"] = point };
            var front = d.ActiveSheet; foreach (Sheet s in d.Sheets) if (s.Name == sh) front = s;
            var view = front.DrawingViews.Cast<DrawingView>().Single(v => v.Name == "front");
            foreach (DrawingCurve c in view.DrawingCurves[Type.Missing]) Console.WriteLine("GEOM " + c.CurveType + " " + c.StartPoint?.X + "," + c.StartPoint?.Y + " -> " + c.EndPoint?.X + "," + c.EndPoint?.Y);
            var edgeId = ""; var circleId = ""; var horizontalId = ""; var verticalId = ""; for (var i = 1; i <= part.ComponentDefinition.SurfaceBodies[1].Edges.Count; i++) { var edge = part.ComponentDefinition.SurfaceBodies[1].Edges[i]; var curves = view.DrawingCurves[edge]; if (curves.Count == 1) { if (curves[1].CurveType == CurveTypeEnum.kLineSegmentCurve && edgeId == "") edgeId = "body:1/edge:" + i; if (curves[1].CurveType == CurveTypeEnum.kLineSegmentCurve) { if (Math.Abs(curves[1].StartPoint.Y - curves[1].EndPoint.Y) < 1e-7) horizontalId = "body:1/edge:" + i; if (Math.Abs(curves[1].StartPoint.X - curves[1].EndPoint.X) < 1e-7) verticalId = "body:1/edge:" + i; } if (curves[1].CurveType == CurveTypeEnum.kCircleCurve) circleId = "body:1/edge:" + i; } }
            dims.Add(new JObject { ["name"] = "length", ["view"] = "front", ["kind"] = "aligned", ["intents"] = new JArray(Edge(edgeId, "start"), Edge(edgeId, "end")), ["text_position_mm"] = new JArray(70, 80) });
            dims.Add(new JObject { ["name"] = "diameter", ["view"] = "front", ["kind"] = "diameter", ["intents"] = new JArray(Edge(circleId, "center")), ["text_position_mm"] = new JArray(160, 80) });
            dims.Add(new JObject { ["name"] = "horizontal", ["view"] = "front", ["kind"] = "horizontal", ["intents"] = new JArray(Edge(horizontalId, "start"), Edge(horizontalId, "end")), ["text_position_mm"] = new JArray(120, 175) });
            dims.Add(new JObject { ["name"] = "vertical", ["view"] = "front", ["kind"] = "vertical", ["intents"] = new JArray(Edge(verticalId, "start"), Edge(verticalId, "end")), ["text_position_mm"] = new JArray(55, 120) });
            dims.Add(new JObject { ["name"] = "radius", ["view"] = "front", ["kind"] = "radius", ["intents"] = new JArray(Edge(circleId, "center")), ["text_position_mm"] = new JArray(180, 130) });
            dims.Add(new JObject { ["name"] = "angle", ["view"] = "front", ["kind"] = "angular", ["intents"] = new JArray(Edge(horizontalId, "mid"), Edge(verticalId, "mid")), ["text_position_mm"] = new JArray(180, 170) });
            var horizontalCurve = view.DrawingCurves[EntityResolverEdge(part, horizontalId)][1]; var verticalCurve = view.DrawingCurves[EntityResolverEdge(part, verticalId)][1]; var chainEnd = verticalCurve.StartPoint.DistanceTo(horizontalCurve.EndPoint) > verticalCurve.EndPoint.DistanceTo(horizontalCurve.EndPoint) ? "start" : "end";
            dims.Add(new JObject { ["name"] = "chain", ["view"] = "front", ["kind"] = "chain", ["direction"] = "aligned", ["intents"] = new JArray(Edge(horizontalId, "start"), Edge(horizontalId, "end"), Edge(verticalId, chainEnd)), ["text_positions_mm"] = new JArray(new JArray(120, 70), new JArray(120, 65)) });
            var dp = new JObject { ["document"] = doc, ["sheet"] = sh, ["items"] = new JArray(dims.Take(2).Select(x => x.DeepClone())) }; var measured = Call("add_drawing_dimension", dp); Call("add_drawing_dimension", dp);
            foreach (JObject item in dims.Skip(2))
            {
                var dimensionResult = Call("add_drawing_dimension", new JObject { ["document"] = doc, ["sheet"] = sh, ["items"] = new JArray(item.DeepClone()) });
                var expected = (string)item["kind"]! switch { "horizontal" => 100.0, "vertical" => 60.0, "radius" => 10.0, "angular" => 90.0, _ => 100.0 };
                if (Math.Abs(dimensionResult["items"]![0]!.Value<double>("value") - expected) > 1e-5) throw new Exception("Dimension measurement mismatch: " + item["kind"]);
                if ((string)item["kind"]! == "chain" && Math.Abs(dimensionResult["items"]![1]!.Value<double>("value") - 60) > 1e-5) throw new Exception("Second chain dimension is incorrect.");
            }
            if (Math.Abs(measured["items"]![1]!.Value<double>("value") - 20) > 1e-5) throw new Exception("Diameter is incorrect.");
            var horizontalEdge = EntityResolverEdge(part, horizontalId);
            JObject ModelPoint(Inventor.Point point) => new() { ["model_point_mm"] = new JArray(point.X * 10, point.Y * 10, point.Z * 10) };
            Call("add_drawing_dimension", new JObject { ["document"] = doc, ["sheet"] = sh, ["items"] = new JArray(new JObject { ["name"] = "model-points", ["view"] = "front", ["kind"] = "aligned", ["intents"] = new JArray(ModelPoint(horizontalEdge.StartVertex.Point), ModelPoint(horizontalEdge.StopVertex.Point)), ["text_position_mm"] = new JArray(120, 185) }) });
            var countBefore = front.DrawingDimensions.GeneralDimensions.Count;
            var invalidBatch = new JArray(((JObject)dims[0]).DeepClone(), ((JObject)dims[0]).DeepClone()); invalidBatch[0]!["name"] = "must-not-create"; invalidBatch[1]!["name"] = "missing-geometry"; invalidBatch[1]!["intents"] = new JArray(Edge("body:1/edge:999999", "start"), Edge(edgeId, "end"));
            Call("add_drawing_dimension", new JObject { ["document"] = doc, ["sheet"] = sh, ["items"] = invalidBatch }, true);
            if (front.DrawingDimensions.GeneralDimensions.Count != countBefore) throw new Exception("Invalid batch created dimensions.");
            var changed = (JObject)baseInput.DeepClone(); changed["position_mm"] = new JArray(130, 130); Call("add_drawing_view", changed, true);
            Call("set_title_block", new JObject { ["document"] = doc, ["sheet"] = sh, ["prompts"] = new JObject { ["DRAWING TITLE"] = "Generic Phase 1 fixture" }, ["iproperties"] = new JArray(new JObject { ["name"] = "Title", ["value"] = "Generic Phase 1 fixture" }) });
            Call("add_drawing_view", new JObject { ["document"] = doc, ["sheet"] = sh, ["name"] = "assembly", ["kind"] = "base", ["model"] = assemblyPath, ["design_view"]="FixtureView", ["position_mm"] = new JArray(280, 65), ["scale"] = 0.5 });
            var balloons = new JObject { ["document"] = doc, ["sheet"] = sh, ["mode"] = "symbol", ["symbol"] = "FixtureBalloon", ["items"] = new JArray(new JObject { ["name"] = "balloon-1", ["view"] = "assembly", ["occurrence_path"] = "sub:1/block:1", ["position_mm"] = new JArray(340, 80), ["text"] = "1" }) }; var balloonReadback = Call("add_balloon", balloons); Call("add_balloon", balloons);
            if ((string?)balloonReadback["items"]![0]!["text"]! != "1") throw new Exception("Balloon prompt readback mismatch.");
            var layoutBalloon = (JObject)balloons.DeepClone(); layoutBalloon["items"]![0]!["name"] = "balloon-layout"; ((JObject)layoutBalloon["items"]![0]!).Remove("position_mm"); layoutBalloon["items"]![0]!["target_region_mm"] = new JObject { ["min"] = new JArray(250, 40), ["max"] = new JArray(310, 90) };
            layoutBalloon["layout"] = new JObject { ["column_x_mm"] = 360, ["start_y_mm"] = 95, ["spacing_mm"] = 15, ["leader_angle_deg"] = 30 }; Call("add_balloon", layoutBalloon);
            Call("edit_drawing_view", new JObject { ["document"] = doc, ["sheet"] = sh, ["view"] = "front", ["scale"] = 0.8, ["position_mm"] = new JArray(125, 125) });
            Call("add_drawing_view", baseInput, true);
            foreach (var command in Bimwright.Ipt.Shared.Contracts.DrawingInput.Commands.Where(x => x != "get_drawing_info")) { var failure = Call(command, new JObject(), true, true); if ((string?)failure["error"]?["code"] != "READ_ONLY") throw new Exception("Read-only write rejection failed."); }
            var beforeRead = d.Dirty; Call("get_drawing_info", new JObject { ["document"] = doc, ["include"] = "items", ["max_items"] = 1 }, false, true); if (d.Dirty != beforeRead) throw new Exception("Query changed drawing dirty state.");
            Call("add_drawing_view",new JObject{["document"]=doc,["sheet"]=initial,["name"]="shaded",["kind"]="base",["model"]=modelPath,["position_mm"]=new JArray(120,120),["scale"]=0.5,["style"]="shaded"});
            Call("edit_drawing_view",new JObject{["document"]=doc,["sheet"]=initial,["view"]="shaded",["position_mm"]=new JArray(150,150)},true);
            var rebuilt=Call("edit_drawing_view",new JObject{["document"]=doc,["sheet"]=initial,["view"]="shaded",["position_mm"]=new JArray(150,150),["rebuild"]=true});
            if(!rebuilt.Value<bool>("rebuilt")||Math.Abs(rebuilt["position_mm"]![0]!.Value<double>()-150)>0.01)throw new Exception("Shaded rebuild failed readback.");
            var originalModelBytes = System.IO.File.ReadAllBytes(modelPath);
            part.PropertySets["Inventor Summary Information"]["Title"].Value = "Unsaved reference sentinel"; if (!part.Dirty) throw new Exception("Reference fixture was not made dirty.");
            Call("capture_sheet", new JObject { ["document"] = doc, ["sheet"] = sh, ["output_path"] = Path.Combine(root, "sheet.png") });
            Call("capture_sheet", new JObject { ["document"] = doc, ["sheet"] = sh, ["output_path"] = Path.Combine(root, "sheet.png") }, true);
            Call("capture_sheet", new JObject { ["document"] = doc, ["sheet"] = sh, ["region_mm"] = new JObject { ["min"] = new JArray(50, 70), ["max"] = new JArray(200, 190) }, ["inline"] = true, ["width"] = 800, ["height"] = 600 });
            Call("export_drawing", new JObject { ["document"] = doc, ["format"] = "pdf", ["output_path"] = Path.Combine(root, "all.pdf"), ["all_sheets"] = true });
            Call("export_drawing", new JObject { ["document"] = doc, ["format"] = "pdf", ["output_path"] = Path.Combine(root, "subset.pdf"), ["sheets"] = new JArray(sh) });
            Call("export_drawing", new JObject { ["document"] = doc, ["format"] = "pdf", ["output_path"] = Path.Combine(root, "ordered.pdf"), ["sheets"] = new JArray(sh, initial) });
            Call("export_drawing", new JObject { ["document"] = doc, ["format"] = "pdf", ["output_path"] = Path.Combine(root, "all.pdf"), ["all_sheets"] = true }, true);
            Call("export_drawing", new JObject { ["document"] = doc, ["format"] = "pdf", ["output_path"] = Path.Combine(root, "all.pdf"), ["all_sheets"] = true, ["overwrite_existing"] = true });
            Call("export_drawing", new JObject { ["document"] = doc, ["format"] = "autocad_dwg", ["output_path"] = Path.Combine(root, "autocad.dwg"), ["all_sheets"] = true });
            Call("export_drawing", new JObject { ["document"] = doc, ["format"] = "native_idw", ["output_path"] = Path.Combine(root, "copy.idw") });
            if (!part.Dirty || !originalModelBytes.SequenceEqual(System.IO.File.ReadAllBytes(modelPath))) throw new Exception("Capture/export persisted a dirty referenced model.");
            var save = new Bimwright.Ipt.Shared.Handlers.Document.SaveDocumentHandler().Execute(ctx, new JObject { ["document"] = doc, ["path"] = Path.Combine(root, "saved.idw"), ["silent"] = true }); Console.WriteLine("SAVE " + JsonConvert.SerializeObject(save)); if (!save.Ok) throw new Exception("Save failed.");
            if (!part.Dirty || !originalModelBytes.SequenceEqual(System.IO.File.ReadAllBytes(modelPath))) throw new Exception("Saving a drawing persisted or cleared a dirty referenced model.");
            d.PropertySets["Inventor Summary Information"]["Subject"].Value="Drawing-only in-place save";
            var inPlace=new Bimwright.Ipt.Shared.Handlers.Document.SaveDocumentHandler().Execute(ctx,new JObject{["document"]=doc,["silent"]=true});
            if(!inPlace.Ok||!part.Dirty||!originalModelBytes.SequenceEqual(System.IO.File.ReadAllBytes(modelPath)))throw new Exception("In-place drawing save changed reference.");
            Console.WriteLine("SAVE_IN_PLACE "+JsonConvert.SerializeObject(inPlace));
            d.Close(true); var reopened = (DrawingDocument)app.Documents.Open(Path.Combine(root, "saved.idw"), true); Console.WriteLine("REOPEN sheets=" + reopened.Sheets.Count + " missing=" + reopened.HasReferencesMissing); if (reopened.Sheets.Count != 2 || reopened.HasReferencesMissing) throw new Exception("Reopen lost sheets/references.");
            addSheet["document"] = reopened.DisplayName; var reused = Call("add_sheet", addSheet); if (reused.Value<bool>("created")) throw new Exception("Sheet identity did not survive save/reopen.");
            var afterScale = reopened.Sheets.Cast<Sheet>().Single(x => x.Name == sh); if (afterScale.SketchedSymbols.Count != 2 || afterScale.DrawingDimensions.GeneralDimensions.Cast<GeneralDimension>().Any(x => !x.Attached)) throw new Exception("Annotations lost after edit/save/reopen.");
            Console.WriteLine("ROOT " + root);
        }
        catch (Exception ex) { Console.WriteLine(ex); System.Environment.ExitCode = 1; }
        finally { if (app != null) app.Quit(); }
    }
}

