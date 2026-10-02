using Inventor;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Path = System.IO.Path;

internal static class Phase2Live
{
    internal static void Run()
    {
        Inventor.Application? app = null;
        var root = Path.Combine(Path.GetTempPath(), "ipt-phase2-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            app = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application", true)!)!;
            app.Visible = true; app.SilentOperation = true;
            if (app.Documents.Count != 0) throw new Exception("Owned session is not empty.");
            Console.WriteLine("ROOT " + root);
            var commands = Bimwright.Ipt.Shared.Plugin.InventorCommandRegistry.Build(new Bimwright.Ipt.Shared.Plugin.PluginOptions(2027, false, false, 5000000));
            var dispatcher = new CommandDispatcher(commands, 5000000);
            var ctx = new InventorCommandContext { Application = app, InventorYear = 2027, TargetId = "phase2-owned-fixture", Commands = commands };
            var tg = app.TransientGeometry;
            var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject), true);
            var sk = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]); sk.SketchCircles.AddByCenterRadius(tg.CreatePoint2d(0, 0), 2);
            var ex = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(sk.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation);
            ex.SetDistanceExtent(1, PartFeatureExtentDirectionEnum.kPositiveExtentDirection); part.ComponentDefinition.Features.ExtrudeFeatures.Add(ex);
            var model = Path.Combine(root, "disc.ipt"); part.SaveAs(model, false);
            var d = (DrawingDocument)app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject, Path.ChangeExtension(app.FileManager.GetTemplateFile(DocumentTypeEnum.kDrawingDocumentObject), ".idw"), true);
            var s = d.ActiveSheet;
            var view = s.DrawingViews.AddBaseView((Inventor._Document)(object)part, tg.CreatePoint2d(12, 12), 1, ViewOrientationTypeEnum.kFrontViewOrientation, DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); view.Name = "front"; d.Update();
            var textStyle = d.StylesManager.TextStyles[1].Name; var dimStyle = d.StylesManager.DimensionStyles[1].Name; var layer = d.StylesManager.Layers[1].Name;
            JToken Call(string command, JObject input, bool failure = false, bool readOnly = false)
            {
                var callCtx = readOnly ? new InventorCommandContext { Application = app, InventorYear = 2027, TargetId = ctx.TargetId, Commands = commands, ReadOnly = true } : ctx;
                var p = (JObject)input.DeepClone(); p["document"] = d.DisplayName; p["sheet"] = s.Name;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = dispatcher.Dispatch(callCtx, new InventorCommandEnvelope { Id = Guid.NewGuid(), Command = command, Params = p });
                var json = JsonConvert.SerializeObject(new { command, duration_ms = sw.ElapsedMilliseconds, result });
                Console.WriteLine(json); System.IO.File.AppendAllText(Path.Combine(root, "results.jsonl"), json + System.Environment.NewLine);
                if ((result.Ok && result.Data?.Value<bool?>("ok") != false) == failure) throw new Exception("Unexpected " + command + " outcome.");
                return result.Data ?? JObject.FromObject(result);
            }
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            var general = JObject.Parse("{name:'note',text:'Literal <200 & 300> °',position_mm:[30,230],kind:'general'}"); general["style"] = textStyle; general["layer"] = layer;
            Check(Call("add_drawing_note", general).Value<bool>("created"), "General note creation");
            Check(!Call("add_drawing_note", general).Value<bool>("created"), "General note reuse");
            var boxed = (JObject)general.DeepClone(); boxed["name"] = "boxed"; boxed["text"] = "Boxed text"; boxed["position_mm"] = new JArray(30, 200); boxed["box_mm"] = new JObject { ["width"] = 60, ["height"] = 25 };
            var box = Call("add_drawing_note", boxed); Check(Math.Abs(box.Value<double>("width_mm") - 60) < 0.001 && Math.Abs(box.Value<double>("height_mm") - 25) < 0.001, "Box dimensions"); Call("add_drawing_note", boxed);
            var title = (JObject)general.DeepClone(); title["name"] = "sheet-title"; title["text"] = "Phase 2 fixture"; title["kind"] = "sheet_title"; title["position_mm"] = new JArray(100, 260); Call("add_drawing_note", title);
            var circle = view.DrawingCurves.Cast<DrawingCurve>().First(x => x.ProjectedCurveType == Curve2dTypeEnum.kCircleCurve2d);
            var edge = (Inventor.Edge)circle.ModelGeometry;
            var edgeId = Enumerable.Range(1, part.ComponentDefinition.SurfaceBodies[1].Edges.Count).First(i => part.ComponentDefinition.SurfaceBodies[1].Edges[i].Equals(edge));
            var leader = JObject.Parse("{name:'leader',text:'Attached note',position_mm:[180,150],kind:'leader',view:'front'}"); leader["style"] = dimStyle; leader["layer"] = layer;
            leader["intent"] = new JObject { ["model_edge"] = "body:1/edge:" + edgeId, ["point_intent"] = "center" };
            Check(Call("add_drawing_note", leader).Value<bool>("attached"), "Leader attachment"); Call("add_drawing_note", leader);
            var table = JObject.Parse("{name:'table',columns:[{heading:'ITEM',width_mm:35},{heading:'VALUE',width_mm:45}],rows:[['A','10'],['B','20']],position_mm:[220,210],row_heights_mm:[7,9],title:'Fixture table'}"); table["style"] = d.StylesManager.TableStyles[1].Name;
            Call("add_drawing_table", table); Check(!Call("add_drawing_table", table).Value<bool>("created"), "Table reuse");
            var bottomStyle = (TableStyle)d.StylesManager.TableStyles[1].Copy("FixtureBottomUp"); bottomStyle.TableDirection = TableDirectionEnum.kBottomUpDirection;
            var bottom = (JObject)table.DeepClone(); bottom["name"] = "bottom-style"; bottom["style"] = bottomStyle.Name; bottom["position_mm"] = new JArray(320, 210);
            var anchored = Call("add_drawing_table", bottom); Check(Math.Abs(anchored["box_mm"]!["max"]![1]!.Value<double>() - 210) < 0.001, "Bottom-up template changed top-left anchor"); Call("add_drawing_table", bottom);
            var empty = (JObject)table.DeepClone(); empty["name"] = "empty"; empty["rows"] = new JArray(); empty["row_heights_mm"] = new JArray(); empty["position_mm"] = new JArray(220, 150); empty["title"] = "Empty table";
            Check(((JArray)Call("add_drawing_table", empty)["rows"]!).Count == 0, "Native default rows were not removed"); Call("add_drawing_table", empty);
            var beforeNotes = s.DrawingNotes.GeneralNotes.Count + s.DrawingNotes.LeaderNotes.Count; var beforeTables = s.CustomTables.Count;
            var changed = (JObject)general.DeepClone(); changed["text"] = "Conflict"; Call("add_drawing_note", changed, true);
            var conflicting = (JObject)table.DeepClone(); conflicting["name"] = "note"; Call("add_drawing_table", conflicting, true);
            var changedTable = (JObject)table.DeepClone(); changedTable["rows"]![0]![1] = "changed"; Call("add_drawing_table", changedTable, true);
            var badStyle = (JObject)general.DeepClone(); badStyle["name"] = "bad-style"; badStyle["style"] = "does-not-exist"; Call("add_drawing_note", badStyle, true);
            var duplicate = (JObject)general.DeepClone(); duplicate["name"] = "table"; Call("add_drawing_note", duplicate, true);
            Check(beforeNotes == s.DrawingNotes.GeneralNotes.Count + s.DrawingNotes.LeaderNotes.Count && beforeTables == s.CustomTables.Count, "Conflict mutated drawing");
            foreach (var command in new[] { "add_drawing_note", "add_drawing_table" }) Check(Call(command, new JObject(), true, true)["error"]!.Value<string>("code") == "READ_ONLY", "Readonly write rejection");
            var path = Path.Combine(root, "phase2.idw"); d.SaveAs(path, false);
            Check(!d.Dirty, "Fixture save dirty state");
            var paged = Call("get_drawing_info", new JObject { ["include"] = "items", ["max_items"] = 1 }, false, true); Check(!d.Dirty, "Query dirtied fixture");
            var queriedTable = paged["sheets"]!["items"]![0]!["tables"]!["items"]![0]!;
            Check(queriedTable["rows"]!.Value<int>("total") == 2 && ((JArray)queriedTable["rows"]!["items"]!).Count == 1, "Table data rows are not bounded");
            d.Close(true); d = (DrawingDocument)app.Documents.Open(path, true); s = d.ActiveSheet;
            var info = Call("get_drawing_info", new JObject { ["include"] = "items" }, false, true);
            Check(info["sheets"]!["items"]![0]!["notes"]!.Value<int>("total") == 4 && info["sheets"]!["items"]![0]!["tables"]!.Value<int>("total") == 3, "Persistent item inventory");
            Call("add_drawing_note", general); Call("add_drawing_table", table); Check(!d.Dirty, "Persistent reuse dirtied document");
            var native = s.CustomTables.Cast<CustomTable>().Single(t => t.AttributeSets["BimwrightDrawing"]["name"].Value.ToString() == "table"); native.Rows[1][2].Value = "manual edit";
            Call("add_drawing_table", table, true); Check(native.Rows[1][2].Value == "manual edit", "Repeat overwrote manual edit");
            Console.WriteLine("PASS phase2 note/table fixture " + root);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); System.Environment.ExitCode = 1; }
        finally { if (app != null) { try { app.Quit(); } catch (Exception ex) { Console.Error.WriteLine("Owned-session cleanup failed: " + ex); System.Environment.ExitCode = 1; } } }
    }
}
