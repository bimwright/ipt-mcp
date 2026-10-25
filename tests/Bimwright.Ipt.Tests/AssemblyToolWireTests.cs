using System.IO.Pipes;
using System.Text.Json;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class AssemblyToolWireTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-wire-" + Guid.NewGuid().ToString("N"));
    private readonly string _pipeName = "BimwrightIptTests-" + Guid.NewGuid().ToString("N");
    private readonly PluginClient _client;

    public AssemblyToolWireTests()
    {
        Directory.CreateDirectory(_dir);
        var descriptor = new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId,
            InventorYear = 2027,
            ProcessId = Environment.ProcessId,
            HostApp = "Inventor",
            Transport = "pipe",
            PipeName = _pipeName,
            AuthToken = "test-token",
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(Path.Combine(_dir, "target.json"), JsonConvert.SerializeObject(descriptor));
        _client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir, TimeoutMs = 5000 });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public async Task Export_bodies_sat_emits_wire_keys_and_rejects_bad_arguments_locally()
    {
        var exports = new ExportTools(_client);
        var items = JsonDocument.Parse("[{\"document\":\"TK02-VESSEL-R23.ipt\",\"bodies\":[\"body:1\",\"SHELL\"],\"key\":\"p0001\"}]").RootElement;
        var outDir = Path.Combine(Path.GetTempPath(), "ipt-sat-out");
        var sent = await CaptureAsync(() => exports.ExportBodiesSat(items, outDir, overwrite: true));
        AssertEnvelope(sent, "export_bodies_sat", "items", "out_dir", "verify", "overwrite", "detail");
        Assert.False(sent["params"]!.Value<bool>("detail"));
        Assert.True(sent["params"]!.Value<bool>("verify"));
        Assert.Equal("p0001", (string?)sent["params"]?["items"]?[0]?["key"]);

        var empty = JObject.Parse(await exports.ExportBodiesSat(JsonDocument.Parse("[]").RootElement, outDir));
        Assert.Equal("INVALID_ARGUMENT", (string?)empty["error"]?["code"]);
        var outside = JObject.Parse(await exports.ExportBodiesSat(items, @"Q:\not-allowed\sat"));
        Assert.Equal("INVALID_ARGUMENT", (string?)outside["error"]?["code"]);
        Assert.Contains("out_dir", (string?)outside["error"]?["message"]);
    }

    [Fact]
    public async Task Build_solids_emits_wire_keys_and_returns_every_local_validation_error()
    {
        var features = new FeatureTools(_client);
        var items = JsonDocument.Parse("[{\"name\":\"plate\",\"box\":{\"min\":[0,0,0],\"max\":[100,50,10]}}]").RootElement;
        var sent = await CaptureAsync(() => features.BuildSolids(items, document: "P1.ipt"));
        AssertEnvelope(sent, "build_solids", "items", "document", "stop_on_error", "dry_run", "detail");
        Assert.True(sent["params"]!.Value<bool>("stop_on_error"));

        var bad = JObject.Parse(await features.BuildSolids(JsonDocument.Parse(
            "[{\"name\":\"a\",\"box\":{\"min\":[0,0,0],\"max\":[0,5,5]}},{\"name\":\"b\",\"op\":\"cut\",\"tube\":{\"a\":[0,0,0],\"b\":[0,0,10],\"od\":5}}]").RootElement));
        Assert.Equal("INVALID_ARGUMENT", (string?)bad["error"]?["code"]);
        Assert.Equal(2, ((JArray)bad["data"]!["errors"]!).Count);
    }

    [Fact]
    public async Task Assembly_write_tools_emit_expected_wire_commands_and_keys()
    {
        var tools = new AssemblyTools(_client);

        var place = await CaptureAsync(() => tools.PlaceOccurrence("C:\\parts\\A.ipt", true, [1, 2, 3], [4, 5, 6]));
        AssertEnvelope(place, "place_occurrence", "path", "grounded", "position_mm", "rotation_deg_xyz");

        var constrain = await CaptureAsync(() => tools.AddConstraint(
            "mate",
            new ConstraintSideDto { Occurrence = "A:1", Ref = "IF_TOP" },
            new ConstraintSideDto { Occurrence = "B:1", Ref = "IF_BOTTOM" },
            offset_mm: 2));
        AssertEnvelope(constrain, "add_constraint", "type", "a_occurrence", "a_ref", "b_occurrence", "b_ref", "offset_mm");

        var imate = await CaptureAsync(() => tools.CreateIMate(
            "IF_TOP", "mate", new FaceSelectorDto { Kind = "planar", Normal = "+Z", Extreme = "max" }));
        AssertEnvelope(imate, "create_imate", "name", "type", "selector");
    }

    [Fact]
    public async Task Assembly_query_tools_emit_expected_wire_commands_and_keys()
    {
        var tools = new AssemblyQueryTools(_client);

        AssertEnvelope(await CaptureAsync(() => tools.ListInterfaces("A:1")), "list_interfaces", "occurrence");
        AssertEnvelope(await CaptureAsync(() => tools.CheckInterference(["A:1", "B:1"])), "check_interference", "occurrences");
        AssertEnvelope(await CaptureAsync(() => tools.MeasureMinDistance(
            new MeasureSideDto { Occurrence = "A:1", Ref = "IF_TOP" },
            new MeasureSideDto { Occurrence = "B:1" })),
            "measure_min_distance", "a_occurrence", "a_ref", "b_occurrence", "b_ref");
        AssertEnvelope(await CaptureAsync(() => tools.GetAssemblyBom(25)), "get_assembly_bom", "max_rows");
        AssertEnvelope(await CaptureAsync(() => tools.ListConstraints()), "list_constraints");
    }

    [Fact]
    public async Task Query_tools_emit_expected_wire_commands_and_keys()
    {
        var tools = new QueryTools(_client);

        AssertEnvelope(await CaptureAsync(() => tools.ListBodies(50)), "list_bodies", "max_items");
        AssertEnvelope(await CaptureAsync(() => tools.ListFeatures(10, include_health: false)),
            "list_features", "max_items", "include_health");
        AssertEnvelope(await CaptureAsync(() => tools.ProbeBrep("body:2", min_diameter_mm: 5, max_diameter_mm: 40, max_items: 50)),
            "probe_brep", "body", "min_diameter_mm", "max_diameter_mm", "max_items");
        AssertEnvelope(await CaptureAsync(() => tools.ListFaces("body:1", "plane", 20, "P1.ipt")),
            "list_faces", "body", "surface_type", "max_items", "document");
        AssertEnvelope(await CaptureAsync(() => tools.ListEdges(curve_type: "circle")),
            "list_edges", "curve_type", "max_items");
    }

    [Fact]
    public async Task Sheet_fabrication_tools_emit_wire_keys_and_defaults()
    {
        var sheet = new SheetFabricationTools(_client);
        var query = new SheetFabricationQueryTools(_client);

        AssertEnvelope(await CaptureAsync(() => sheet.NewSheetFabricationPart(@"C:\templates\Sheet Metal (mm).ipt")),
            "new_sheet_fabrication_part", "template");
        AssertEnvelope(await CaptureAsync(() => query.SheetFabricationInfo("SM1.ipt")), "sheet_fabrication_info", "document");
        AssertEnvelope(await CaptureAsync(() => sheet.SheetFabricationFace("Base", name: "plate", document: "SM1.ipt")),
            "sheet_fabrication_face", "sketch", "name", "document");

        var flange = await CaptureAsync(() => sheet.SheetFabricationFlange(["body:1/edge:4"], JsonDocument.Parse("\"flange_h\"").RootElement));
        AssertEnvelope(flange, "sheet_fabrication_flange", "edges", "height", "angle_deg");
        Assert.Equal(90, flange["params"]!.Value<double>("angle_deg"));
        Assert.Equal("flange_h", (string?)flange["params"]!["height"]);
        Assert.False(((JObject)flange["params"]!).ContainsKey("bend_radius_mm"));
        var numeric = await CaptureAsync(() => sheet.SheetFabricationFlange(["edge:2"], JsonDocument.Parse("25").RootElement, 45, bend_radius_mm: 1.5));
        Assert.Equal(25, numeric["params"]!.Value<double>("height"));
        Assert.Equal(1.5, numeric["params"]!.Value<double>("bend_radius_mm"));

        var cut = await CaptureAsync(() => sheet.SheetFabricationCut("Slot"));
        AssertEnvelope(cut, "sheet_fabrication_cut", "sketch", "cut_across_bends", "cut_normal_to_flat", "direction");
        Assert.False(cut["params"]!.Value<bool>("cut_across_bends"));
        Assert.False(cut["params"]!.Value<bool>("cut_normal_to_flat"));
        Assert.Equal("positive", (string?)cut["params"]!["direction"]);

        AssertEnvelope(await CaptureAsync(() => sheet.FlatPattern("face:3")), "sheet_fabrication_flat_pattern", "face");
        var thickness = await CaptureAsync(() => sheet.SetSheetFabricationThickness(JsonDocument.Parse("\"2.5 mm\"").RootElement));
        AssertEnvelope(thickness, "sheet_fabrication_set_thickness", "thickness");
        Assert.Equal("2.5 mm", (string?)thickness["params"]!["thickness"]);
    }

    [Fact]
    public async Task Unwrap_try_rolls_back_by_default_and_empty_ids_fail_before_the_wire()
    {
        var sheet = new SheetFabricationTools(_client);
        var trial = await CaptureAsync(() => sheet.UnwrapTry(["face:1", "body:1/face:2"]));
        AssertEnvelope(trial, "unwrap_try", "faces", "alignment", "auto_face_chain", "merge_result_body", "keep");
        Assert.False(trial["params"]!.Value<bool>("keep"));
        Assert.Equal("origin", (string?)trial["params"]!["alignment"]);
        Assert.True((await CaptureAsync(() => sheet.UnwrapTry(["face:1"], keep: true)))["params"]!.Value<bool>("keep"));

        // No pipe server is listening here, so these must be rejected locally.
        Assert.Equal("INVALID_ARGUMENT", ErrorCode(await sheet.UnwrapTry([])));
        Assert.Equal("INVALID_ARGUMENT", ErrorCode(await sheet.SheetFabricationFlange([], JsonDocument.Parse("10").RootElement)));
    }

    [Fact]
    public async Task ILogic_read_and_command_tools_emit_wire_keys()
    {
        var tools = new ILogicTools(_client);

        AssertEnvelope(await CaptureAsync(() => tools.ListRules("Bracket.ipt")), "ilogic_list_rules", "document");
        AssertEnvelope(await CaptureAsync(() => tools.GetRule("Resize")), "ilogic_get_rule", "name");
        AssertEnvelope(await CaptureAsync(() => tools.ReadLog(4096)), "ilogic_read_log", "max_chars");
        AssertEnvelope(await CaptureAsync(() => tools.ListCommands("undo", 20)), "list_commands", "filter", "max_items");

        var command = await CaptureAsync(() => tools.RunCommand("AppZoomallCmd"));
        AssertEnvelope(command, "run_command", "name");
        Assert.False(((JObject)command["params"]!).ContainsKey("silent"));
        Assert.True((await CaptureAsync(() => tools.RunCommand("AppZoomallCmd", silent: true)))["params"]!.Value<bool>("silent"));

        var rule = await CaptureAsync(() => tools.RunRule("Resize", arguments: JsonDocument.Parse("{\"L\":120,\"mode\":\"fast\"}").RootElement));
        AssertEnvelope(rule, "ilogic_run_rule", "name", "arguments");
        Assert.Equal(120, rule["params"]!["arguments"]!.Value<int>("L"));
        Assert.Equal("INVALID_ARGUMENT", ErrorCode(await tools.RunRule("Resize",
            arguments: JsonDocument.Parse("{\"nested\":{\"a\":1}}").RootElement)));
    }

    [Fact]
    public async Task Code_modules_are_stored_only_after_a_dry_compile_and_deleted_in_dependency_order()
    {
        var store = new CodeModuleStore(Path.Combine(_dir, "modules"));
        var tools = new CodeTools(_client, store);
        const string geo = "static double Sq(double x) => x * x;";

        var (sent, failed) = await CaptureWithResultAsync(() => tools.SaveCodeModule("geo", geo),
            new JObject { ["ok"] = false, ["diagnostics"] = new JArray() });
        AssertEnvelope(sent, "send_code", "code", "compile_only", "modules");
        Assert.True(sent["params"]!.Value<bool>("compile_only"));
        Assert.Equal("", (string?)sent["params"]!["code"]);
        Assert.False(failed.Value<bool>("saved"));
        Assert.Empty(store.List());

        static JObject Compiled(string module) => new()
        {
            ["ok"] = true,
            ["signatures"] = new JArray(new JObject { ["module"] = module, ["signatures"] = new JArray("sig") }),
        };
        Assert.True((await CaptureWithResultAsync(() => tools.SaveCodeModule("geo", geo), Compiled("geo"))).Result.Value<bool>("saved"));
        var (dependent, saved) = await CaptureWithResultAsync(() => tools.SaveCodeModule(
            "shapes", "static double Area(double r) => 3.14159 * Sq(r);", requires: ["geo"]), Compiled("shapes"));
        Assert.True(saved.Value<bool>("saved"));
        Assert.Equal(new[] { "geo", "shapes" }, dependent["params"]!["modules"]!.Select(m => (string)m["name"]!));

        var listed = JObject.Parse(await tools.ListCodeModules());
        Assert.Equal(2, listed.Value<int>("count"));
        Assert.All(listed["modules"]!, m => Assert.Null(m["code"]));
        Assert.Contains(JObject.Parse(await tools.ListCodeModules(include_code: true))["modules"]!,
            m => (string?)m["name"] == "geo" && (string?)m["code"] == geo);

        var blocked = JObject.Parse(await tools.DeleteCodeModule("geo"));
        Assert.Equal("INVALID_ARGUMENT", (string?)blocked["error"]?["code"]);
        Assert.Contains("shapes", (string?)blocked["error"]?["message"]);
        Assert.True(JObject.Parse(await tools.DeleteCodeModule("shapes")).Value<bool>("ok"));
        Assert.True(JObject.Parse(await tools.DeleteCodeModule("geo")).Value<bool>("ok"));
        Assert.Equal("INVALID_ARGUMENT", ErrorCode(await tools.DeleteCodeModule("geo")));
    }

    [Fact]
    public async Task Feature_and_view_tools_emit_expected_wire_commands_and_keys()
    {
        var features = new FeatureTools(_client);
        var exports = new ExportTools(_client);
        var props = new PropertyTools(_client);
        var sketches = new SketchTools(_client);

        var hole = await CaptureAsync(() => features.Hole(
            new HoleFaceDto { Normal = "+Z", Extreme = "max" },
            [[10, 10, 5]],
            6.5,
            tapped: new HoleTappedDto { Designation = "M6x1" }));
        AssertEnvelope(hole, "hole", "face", "points_mm", "diameter_mm", "tapped_designation");

        AssertEnvelope(await CaptureAsync(() => features.Combine("body:1", ["body:2"], "cut", keep_tool_bodies: true)),
            "combine", "base_body", "tool_bodies", "operation", "keep_tool_bodies");
        AssertEnvelope(await CaptureAsync(() => features.BatchExecute(JsonDocument.Parse("[{\"command\":\"list_bodies\"}]").RootElement)),
            "batch_execute", "commands", "continue_on_error");
        AssertEnvelope(await CaptureAsync(() => features.CircularPattern(["Hole1"], "Z Axis", 4)),
            "circular_pattern", "feature_names", "axis", "count", "angle_deg");
        AssertEnvelope(await CaptureAsync(() => features.RectangularPattern(
            ["Hole1"], "X Axis", 3, 20, "Y Axis", 2, 10)),
            "rectangular_pattern", "feature_names", "dir1", "count1", "spacing_mm1", "dir2", "count2", "spacing_mm2");
        AssertEnvelope(await CaptureAsync(() => features.Loft(["S1", "S2:1"], "new_body", closed: true, name: "hull")),
            "loft", "profiles", "operation", "merge_tangent_faces", "closed", "name");
        AssertEnvelope(await CaptureAsync(() => features.Sweep("Prof", "Path1", orientation: "parallel")),
            "sweep", "profile", "path", "operation", "orientation");
        AssertEnvelope(await CaptureAsync(() => features.CreateWorkPoint(
                JsonDocument.Parse("[10,20,30]").RootElement, construction: true, name: "wp_pt", visible: false)),
            "create_work_point", "position", "construction", "name", "visible");
        AssertEnvelope(await CaptureAsync(() => features.CreateBimConnector(
                "body:1/edge:3", nominal_diameter_mm: 50, system_type: "domestic_cold",
                flow_direction: "in", connection_type: "threaded", description: "inlet")),
            "create_bim_connector", "geometry", "kind", "nominal_diameter_mm", "system_type",
            "flow_direction", "connection_type", "description");
        AssertEnvelope(await CaptureAsync(() => sketches.DrawText(
                "PORT A", [5.0, 10.0], sketch_name: "S1", font_size_mm: 3.5, rotation_deg: 90)),
            "draw_text", "text", "position", "sketch_name", "font_size_mm", "rotation_deg");
        AssertEnvelope(await CaptureAsync(() => exports.ViewFit()), "view_fit");
        AssertEnvelope(await CaptureAsync(() => exports.SetViewState("Clean", design_view_create: true,
                object_visibility: JsonDocument.Parse("{\"sketches\":false}").RootElement)),
            "set_view_state", "design_view", "design_view_create", "object_visibility");
        AssertEnvelope(await CaptureAsync(() => exports.SetViewOrientation("iso_top_right", false)),
            "set_view_orientation", "orientation", "fit");
        AssertEnvelope(await CaptureAsync(() => exports.SetCamera(
                eye: JsonDocument.Parse("[10,20,30]").RootElement,
                perspective: true, extents_mm: [100, 80], fit: true)),
            "set_camera", "eye", "perspective", "extents_mm", "fit");
        // output_path is validated against ExportPathPolicy before the wire call, so use a
        // path under %TEMP% (an allowed root) or the request never reaches the plugin.
        AssertEnvelope(await CaptureAsync(() => exports.ExportSat(Path.Combine(Path.GetTempPath(), "out.sat"))),
            "export_sat", "output_path", "acis_version");
        AssertEnvelope(await CaptureAsync(() => exports.DeriveEnvelope(
                Path.Combine(Path.GetTempPath(), "env.ipt"), deriveStyle: "single_no_seams",
                includeBodies: ["valve_body", "body:2"], activate: false)),
            "derive_envelope", "output_path", "derive_style", "include_bodies", "bounding_box", "activate");
        AssertEnvelope(await CaptureAsync(() => props.ListIPropertySets(true)),
            "list_iproperty_sets", "include_values");
    }

    private async Task<JObject> CaptureAsync(Func<Task<string>> invoke)
        => (await CaptureWithResultAsync(invoke, new JObject { ["accepted"] = true })).Envelope;

    private async Task<(JObject Envelope, JObject Result)> CaptureWithResultAsync(Func<Task<string>> invoke, JObject data)
    {
        using var server = new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var receive = ReceiveOnceAsync(server, data);
        var invokeTask = invoke();
        var envelope = await receive;
        return (envelope, JObject.Parse(await invokeTask));
    }

    private static async Task<JObject> ReceiveOnceAsync(NamedPipeServerStream server, JObject data)
    {
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var envelope = JObject.Parse((await reader.ReadLineAsync())!);
        await writer.WriteLineAsync(new JObject
        {
            ["id"] = envelope["id"],
            ["ok"] = true,
            ["data"] = data,
            ["meta"] = new JObject(),
        }.ToString(Formatting.None));
        return envelope;
    }

    private static string? ErrorCode(string json) => (string?)JObject.Parse(json)["error"]?["code"];

    private static void AssertEnvelope(JObject envelope, string command, params string[] keys)
    {
        Assert.Equal(command, (string?)envelope["command"]);
        var parameters = Assert.IsType<JObject>(envelope["params"]);
        foreach (var key in keys) Assert.True(parameters.ContainsKey(key), $"{command} missing wire key '{key}'");
    }
}
