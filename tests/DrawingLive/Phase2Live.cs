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
                var p = (JObject)input.DeepClone(); p["document"] = d.DisplayName; if (command != "export_drawing") p["sheet"] = s.Name;
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
            foreach (var command in new[] { "add_drawing_note", "add_drawing_table", "add_drawing_symbol", "edit_drawing_annotation", "delete_drawing_items", "edit_drawing_table", "set_drawing_styles", "edit_sheet" }) Check(Call(command, new JObject(), true, true)["error"]!.Value<string>("code") == "READ_ONLY", "Readonly write rejection");
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
            view = s.DrawingViews.Cast<DrawingView>().Single(x => x.Name == "front");
            var definition = d.SketchedSymbolDefinitions.Add("FixtureLevel"); definition.Edit(out var symbolSketch);
            symbolSketch.SketchCircles.AddByCenterRadius(tg.CreatePoint2d(0, 0), 0.3);
            var symbolTextStyle = (TextStyle)d.StylesManager.TextStyles[1].Copy("FixtureSymbolText"); symbolTextStyle.Font = "Arial"; symbolTextStyle.FontSize = 0.18;
            var symbolTextBox = symbolSketch.TextBoxes.AddFitted(tg.CreatePoint2d(0, 0), "<Prompt>LEVEL</Prompt>", symbolTextStyle); symbolTextBox.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextCenter; symbolTextBox.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextMiddle; definition.ExitEdit();
            var symbol = JObject.Parse("{name:'level',kind:'symbol',definition:'FixtureLevel',position_mm:[180,100],prompts:{LEVEL:'100'},rotation_deg:15,scale:1.2}");
            var bare = Call("add_drawing_symbol", symbol); Check(bare.Value<bool>("created"), "Bare symbol creation"); Check(!Call("add_drawing_symbol", symbol).Value<bool>("created"), "Bare symbol reuse");
            var attached = (JObject)symbol.DeepClone(); attached["name"] = "attached-level"; attached["position_mm"] = new JArray(180, 90); attached["view"] = "front"; attached["intent"] = leader["intent"]!.DeepClone();
            var attachedCreated = Call("add_drawing_symbol", attached); Check(attachedCreated.Value<bool>("attached"), "Symbol leader attachment"); Call("add_drawing_symbol", attached);
            var center = new JObject { ["name"] = "center", ["kind"] = "centermark", ["view"] = "front", ["intent"] = leader["intent"]!.DeepClone() };
            Check(Call("add_drawing_symbol", center).Value<bool>("attached"), "Centermark attachment"); Call("add_drawing_symbol", center);
            var diameter = new JObject { ["items"] = new JArray(new JObject { ["name"] = "diameter", ["view"] = "front", ["kind"] = "diameter", ["intents"] = new JArray(new JObject { ["model_edge"] = "body:1/edge:" + edgeId, ["point_intent"] = "center" }), ["text_position_mm"] = new JArray(160, 180) }) };
            Call("add_drawing_dimension", diameter);
            var beforeValue = s.DrawingDimensions.GeneralDimensions[1].ModelValue;
            JObject Edit(string kind, string name, JObject changes) => new JObject { ["items"] = new JArray(new JObject { ["kind"] = kind, ["name"] = name, ["changes"] = changes }) };
            var editedDim = Call("edit_drawing_annotation", Edit("dimension", "diameter", new JObject { ["text_override"] = "Diameter <40 & 40>", ["precision"] = 3, ["text_position_mm"] = new JArray(160, 190), ["style"] = dimStyle, ["layer"] = layer }));
            Check(Math.Abs(s.DrawingDimensions.GeneralDimensions[1].ModelValue - beforeValue) < 1e-9 && s.DrawingDimensions.GeneralDimensions[1].Precision == 3, "Edited dimension changed measured value/precision");
            var angularSketch = s.Sketches.Add(); angularSketch.Edit();
            var angleOne = angularSketch.SketchLines.AddByTwoPoints(tg.CreatePoint2d(14, 12), tg.CreatePoint2d(12, 14));
            var angleThree = angularSketch.SketchLines.AddByTwoPoints(angleOne.EndSketchPoint, tg.CreatePoint2d(10, 12)); angularSketch.ExitEdit();
            var angular = s.DrawingDimensions.GeneralDimensions.AddAngular(tg.CreatePoint2d(12, 16), s.CreateGeometryIntent(angleOne, PointIntentEnum.kStartPointIntent), s.CreateGeometryIntent(angleOne, PointIntentEnum.kEndPointIntent), s.CreateGeometryIntent(angleThree, PointIntentEnum.kEndPointIntent));
            ((GeneralDimension)angular).AttributeSets.Add("BimwrightDrawing").Add("name", ValueTypeEnum.kStringType, "three-point-angle"); d.Update();
            var angularValue = angular.ModelValue;
            Check(s.DrawingDimensions.GeneralDimensions.Count == 2, "Three-point angular dimension persisted in native collection");
            Call("edit_drawing_annotation", Edit("dimension", "three-point-angle", JObject.Parse("{text_override:'Three-point angle',precision:3}")));
            Check(angular.IntentThree != null && Math.Abs(angular.ModelValue - angularValue) < 1e-9 && angular.Attached, "Three-intent angular attachment/value");
            Call("delete_drawing_items", JObject.Parse("{dry_run:false,items:[{kind:'dimension',name:'three-point-angle'}]}"));
            var linear = s.DrawingDimensions.GeneralDimensions.AddLinear(tg.CreatePoint2d(12, 10), s.CreateGeometryIntent(angleOne, PointIntentEnum.kStartPointIntent), s.CreateGeometryIntent(angleThree, PointIntentEnum.kEndPointIntent), DimensionTypeEnum.kHorizontalDimensionType);
            ((GeneralDimension)linear).AttributeSets.Add("BimwrightDrawing").Add("name", ValueTypeEnum.kStringType, "two-point-linear"); d.Update();
            var linearValue = linear.ModelValue;
            Call("edit_drawing_annotation", Edit("dimension", "two-point-linear", JObject.Parse("{text_override:'Two-point linear',precision:3}")));
            Check(Math.Abs(linear.ModelValue - linearValue) < 1e-9 && linear.Attached, "Two-intent linear attachment/value");
            Call("delete_drawing_items", JObject.Parse("{dry_run:false,items:[{kind:'dimension',name:'two-point-linear'}]}"));
            angularSketch.Delete();
            Call("edit_drawing_annotation", Edit("note", "note", new JObject { ["text_override"] = "Edited literal < & >", ["text_position_mm"] = new JArray(35, 235) }));
            var arrow = Call("edit_drawing_annotation", Edit("note", "leader", new JObject { ["text_override"] = "Edited attached note", ["leader_arrowhead"] = "open", ["text_position_mm"] = new JArray(185, 155) }));
            Check(s.DrawingNotes.LeaderNotes[1].DimensionStyle.LeaderStyle.ArrowheadType == ArrowheadTypeEnum.kOpenArrowheadType, "Note arrowhead readback");
            var moved = Edit("symbol", "attached-level", new JObject { ["text_position_mm"] = new JArray(190, 95), ["text_override"] = "200", ["leader_arrowhead"] = "filled" });
            Call("edit_drawing_annotation", moved, true); moved["rebuild"] = true; var movedResult = Call("edit_drawing_annotation", moved); Check(movedResult["items"]![0]!.Value<bool>("attached"), "Recreated symbol lost attachment");
            Check(movedResult["items"]![0]!["prompts"]!.Value<string>("LEVEL") == "200", "Symbol prompt edit");
            var stylesBeforeRepeat = d.StylesManager.LeaderStyles.Count;
            var repeatedMove = Call("edit_drawing_annotation", moved); Check(!repeatedMove["items"]![0]!.Value<bool>("rebuilt") && d.StylesManager.LeaderStyles.Count == stylesBeforeRepeat && JToken.DeepEquals(repeatedMove["items"]![0]!["locator"], movedResult["items"]![0]!["locator"]), "Annotation repeat recreated symbol/style");
            Call("add_drawing_symbol", attached, true);
            Call("edit_drawing_annotation", new JObject { ["items"] = new JArray(new JObject { ["kind"] = "symbol", ["locator"] = attachedCreated["locator"]!.DeepClone(), ["changes"] = new JObject { ["text_override"] = "Stale" } }) }, true);
            var foreign = Edit("note", "note", new JObject { ["text_override"] = "Forbidden" });
            var target = (JObject)foreign["items"]![0]!; target.Remove("name"); target["locator"] = info["sheets"]!["items"]![0]!["notes"]!["items"]![0]!["locator"]!.DeepClone(); target["locator"]!["document_id"] = "foreign"; Call("edit_drawing_annotation", foreign, true);
            // Rebuild is tested both as an accepted edit and as a failed write that must abort.
            var tableEdit = JObject.Parse("{name:'table',changes:{delete_rows:[2],insert_rows:[{index:2,rows:[['C','30']]}],cells:[{row:1,column:2,text:'11'}],column_widths_mm:[36,46],row_heights_mm:[8,10],title:'Edited fixture table',position_mm:[220,215]}}");
            var corrected = Call("edit_drawing_table", tableEdit); Check(corrected["rows"]![1]![0]!.Value<string>() == "C", "Table row insertion");
            Call("edit_drawing_table", JObject.Parse("{name:'empty',changes:{insert_rows:[{index:1,rows:[['Empty','now populated']]}]}}"));
            var full = (JObject)tableEdit.DeepClone(); full["rebuild"] = true; full["changes"] = JObject.Parse("{header_height_mm:12}");
            foreach (var member in new[] { "MaximumRows", "NumberOfSections", "WrapAutomatically", "WrapLeft" }) { try { Console.WriteLine("TABLE_NATIVE " + member + "=" + typeof(CustomTable).GetProperty(member)!.GetValue(native)); } catch (Exception e) { Console.WriteLine("TABLE_NATIVE " + member + " unavailable: " + e.GetBaseException().Message); } }
            var rebuilt = Call("edit_drawing_table", full); Check(Math.Abs(rebuilt.Value<double>("header_height_mm") - 12) < 0.01 && JToken.DeepEquals(rebuilt["rows"], corrected["rows"]) && rebuilt.Value<string>("title") == "Edited fixture table", "Header rebuild snapshot");
            full["changes"]!["header_height_mm"] = 0.01; var rollback = Call("edit_drawing_table", full, true); Check(rollback.Value<bool>("rolled_back"), "Failed table rebuild rollback");
            Check(s.CustomTables.Count == 3, "Rollback leaked measurement/rebuilt table");
            var globalFiles = Directory.GetFiles(app.FileOptions.DesignDataPath, "*.xml", SearchOption.AllDirectories);
            string Hash(string f) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(f)));
            var globalBefore = globalFiles.ToDictionary(f => f, Hash);
            var styleEdit = new JObject { ["changes"] = new JObject {
                ["text_styles"] = new JArray(new JObject { ["name"] = textStyle, ["font"] = "Arial", ["size_mm"] = 3.5, ["bold"] = true, ["italic"] = false }),
                ["dimension_styles"] = new JArray(new JObject { ["name"] = dimStyle, ["units"] = "mm", ["display_format"] = "decimal", ["linear_precision"] = 2, ["text_style"] = textStyle }),
                ["layers"] = new JArray(new JObject { ["name"] = layer, ["color_rgb"] = new JArray(12, 34, 56), ["weight_mm"] = 0.25, ["line_type"] = "dashed" }),
                ["object_defaults"] = new JArray(new JObject { ["kind"] = "general_note", ["style"] = textStyle, ["layer"] = layer }) } };
            var styled = Call("set_drawing_styles", styleEdit); Check(styled.Value<int>("affected_count") > 0 && !styled.Value<bool>("global_style_library_written"), "Style affected count"); Call("set_drawing_styles", styleEdit);
            Check(d.StylesManager.DimensionStyles.Cast<DimensionStyle>().Single(x => x.Name == dimStyle).LinearPrecision == LinearPrecisionEnum.kTwoDecimalPlacesLinearPrecision, "Decimal precision enum mapping");
            Check(globalFiles.Length > 0 && globalFiles.All(f => globalBefore[f] == Hash(f)), "Global style library changed");
            System.IO.File.WriteAllText(Path.Combine(root, "global-styles.json"), JsonConvert.SerializeObject(new { count = globalFiles.Length, unchanged = true, hashes = globalBefore }));
            Call("edit_sheet", JObject.Parse("{changes:{delete:true},contents:[]}"), true);
            Call("edit_sheet", JObject.Parse("{changes:{name:'Edited sheet',code:'S-MAIN'}}"));
            var second = d.Sheets.Add(DrawingSheetSizeEnum.kA3DrawingSheetSize, PageOrientationTypeEnum.kLandscapePageOrientation, "Second"); second.AddDefaultBorder(); second.Activate(); d.Update(); s = second;
            Call("edit_sheet", JObject.Parse("{changes:{name:'Auxiliary',code:'S-AUX'}}"));
            Check(Call("edit_sheet", JObject.Parse("{changes:{index:1}}")).Value<int>("index") == 1, "Sheet reorder");
            s = d.Sheets.Cast<Sheet>().Single(x => x.AttributeSets.NameIsUsed["BimwrightDrawing"] && x.AttributeSets["BimwrightDrawing"].NameIsUsed["code"] && x.AttributeSets["BimwrightDrawing"]["code"].Value.ToString() == "S-MAIN");
            Check(Call("edit_sheet", JObject.Parse("{changes:{active:true}}")).Value<bool>("active"), "Sheet activate");
            var crossBase = s.DrawingViews.AddBaseView((Inventor._Document)(object)part, tg.CreatePoint2d(32, 8), 0.5, ViewOrientationTypeEnum.kFrontViewOrientation, DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); crossBase.Name = "cross-base";
            var crossChild = s.DrawingViews.AddProjectedView(crossBase, tg.CreatePoint2d(32, 14), DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); crossChild.Name = "cross-child"; crossChild = crossChild.MoveTo(second); second.Activate(); d.Update(); s.Activate(); d.Update();
            var crossCounts = new[] { s.DrawingViews.Count, second.DrawingViews.Count };
            // Native cross-sheet projection introduces a sketch outside the supported
            // cascade inventory. Both preview and execution must refuse it safely.
            Call("delete_drawing_items", JObject.Parse("{selector:{kind:'view',names:['cross-base']}}"), true);
            Call("delete_drawing_items", JObject.Parse("{dry_run:false,items:[{kind:'view',name:'cross-base'}]}"), true);
            Check(crossCounts.SequenceEqual(new[] { s.DrawingViews.Count, second.DrawingViews.Count }), "Cross-sheet refusal changed views");
            crossChild.Delete(); crossBase.Delete(); d.Update();
            var resized = Call("edit_sheet", JObject.Parse("{changes:{width_mm:400,height_mm:270,orientation:'landscape'}}")); Check(!resized.Value<bool>("views_rescaled") && ((JArray)resized["bounds_unavailable"]!).Count > 0, "Resize missing bounds coverage");
            d.Save(); var dirty = d.Dirty;
            var preview = Call("delete_drawing_items", JObject.Parse("{selector:{kind:'note',names:['boxed']}}")); Check(preview.Value<int>("count") == 1 && d.Dirty == dirty, "Delete preview dirty state");
            Check(Call("delete_drawing_items", JObject.Parse("{selector:{kind:'note',region_mm:{min:[0,0],max:[1,1]}}}")).Value<int>("count") == 0 && !d.Dirty, "Empty region/preview");
            var exact = (JObject)preview["items"]![0]!.DeepClone(); exact.Remove("box_mm"); exact.Remove("dependents");
            Call("delete_drawing_items", new JObject { ["dry_run"] = false, ["items"] = new JArray(exact) }); Call("delete_drawing_items", new JObject { ["dry_run"] = false, ["items"] = new JArray(exact) }, true);
            s = second; s.Activate(); d.Update();
            Call("edit_sheet", JObject.Parse("{changes:{delete:true},contents:[]}"), true);
            var secondInfo = Call("get_drawing_info", JObject.Parse("{include:'items'}"), false, true);
            Call("edit_sheet", new JObject { ["changes"] = new JObject { ["delete"] = true }, ["contents"] = secondInfo["sheets"]!["items"]![0]!["contents"]!["items"]!.DeepClone() });
            s = d.ActiveSheet;
            Check(d.Sheets.Count == 1, "Explicit sheet deletion");
            var aux = s.DrawingViews.AddBaseView((Inventor._Document)(object)part, tg.CreatePoint2d(32, 8), 0.5, ViewOrientationTypeEnum.kFrontViewOrientation, DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); aux.Name = "delete-base";
            var child = s.DrawingViews.AddProjectedView(aux, tg.CreatePoint2d(32, 14), DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle); child.Name = "delete-child"; d.Update();
            Console.WriteLine("DEPENDENT_NATIVE parent=" + child.ParentView?.Name + " type=" + child.ViewType);
            var cascade = Call("delete_drawing_items", JObject.Parse("{selector:{kind:'view',names:['delete-base']}}"));
            var baseTarget = (JObject)cascade["items"]![0]!; var deps = (JArray)baseTarget["dependents"]!; Check(deps.Count == 1, "Child-view deletion inventory");
            var baseExact = (JObject)baseTarget.DeepClone(); baseExact.Remove("box_mm"); baseExact.Remove("dependents");
            Call("delete_drawing_items", new JObject { ["dry_run"] = false, ["items"] = new JArray(baseExact) }, true);
            Call("delete_drawing_items", new JObject { ["dry_run"] = false, ["items"] = new JArray(new[] { (JToken)baseExact }.Concat(deps.Select(x => x.DeepClone()))) });
            Check(s.DrawingViews.Count == 1 && s.Centermarks.Count == 1, "Cascade changed retained items");
            d.Save(); var finalInfo = Call("get_drawing_info", JObject.Parse("{include:'items'}"), false, true); var finalLocator = finalInfo["sheets"]!["items"]![0]!["notes"]!["items"]![0]!["selection"]!.DeepClone();
            d.Close(true); d = (DrawingDocument)app.Documents.Open(path, true); s = d.ActiveSheet;
            var reopened = Call("get_drawing_info", JObject.Parse("{include:'items'}"), false, true); Check(!d.Dirty && reopened["sheets"]!["items"]![0]!.Value<string>("code") == "S-MAIN", "Reopen sheet code/query dirty state");
            finalLocator["changes"] = new JObject { ["text_override"] = "Persistent locator edit" }; Call("edit_drawing_annotation", new JObject { ["items"] = new JArray(finalLocator) });
            Call("edit_drawing_annotation", Edit("note", "sheet-title", JObject.Parse("{text_position_mm:[30,250]}")));
            Call("edit_drawing_annotation", Edit("note", "leader", JObject.Parse("{text_position_mm:[170,155]}")));
            Call("edit_drawing_table", JObject.Parse("{name:'bottom-style',changes:{position_mm:[290,125]}}"));
            d.Save();
            var pdf = Path.Combine(root, "phase2.pdf"); System.Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", root);
            Call("export_drawing", new JObject { ["format"] = "pdf", ["output_path"] = pdf, ["all_sheets"] = true });
            Console.WriteLine("PASS phase2 all-eight-tool fixture " + root);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); System.Environment.ExitCode = 1; }
        finally { if (app != null) { try { app.Quit(); } catch (Exception ex) { Console.Error.WriteLine("Owned-session cleanup failed: " + ex); System.Environment.ExitCode = 1; } } }
    }
}
