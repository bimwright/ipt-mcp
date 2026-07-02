using System.IO.Pipes;
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
    public async Task Feature_and_view_tools_emit_expected_wire_commands_and_keys()
    {
        var features = new FeatureTools(_client);
        var exports = new ExportTools(_client);

        var hole = await CaptureAsync(() => features.Hole(
            new HoleFaceDto { Normal = "+Z", Extreme = "max" },
            [[10, 10, 5]],
            6.5,
            tapped: new HoleTappedDto { Designation = "M6x1" }));
        AssertEnvelope(hole, "hole", "face", "points_mm", "diameter_mm", "tapped_designation");

        AssertEnvelope(await CaptureAsync(() => features.CircularPattern(["Hole1"], "Z Axis", 4)),
            "circular_pattern", "feature_names", "axis", "count", "angle_deg");
        AssertEnvelope(await CaptureAsync(() => features.RectangularPattern(
            ["Hole1"], "X Axis", 3, 20, "Y Axis", 2, 10)),
            "rectangular_pattern", "feature_names", "dir1", "count1", "spacing_mm1", "dir2", "count2", "spacing_mm2");
        AssertEnvelope(await CaptureAsync(() => exports.ViewFit()), "view_fit");
        AssertEnvelope(await CaptureAsync(() => exports.SetViewOrientation("iso_top_right", false)),
            "set_view_orientation", "orientation", "fit");
    }

    private async Task<JObject> CaptureAsync(Func<Task<string>> invoke)
    {
        using var server = new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var receive = ReceiveOnceAsync(server);
        var invokeTask = invoke();
        var envelope = await receive;
        await invokeTask;
        return envelope;
    }

    private static async Task<JObject> ReceiveOnceAsync(NamedPipeServerStream server)
    {
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var envelope = JObject.Parse((await reader.ReadLineAsync())!);
        await writer.WriteLineAsync(new JObject
        {
            ["id"] = envelope["id"],
            ["ok"] = true,
            ["data"] = new JObject { ["accepted"] = true },
            ["meta"] = new JObject(),
        }.ToString(Formatting.None));
        return envelope;
    }

    private static void AssertEnvelope(JObject envelope, string command, params string[] keys)
    {
        Assert.Equal(command, (string?)envelope["command"]);
        var parameters = Assert.IsType<JObject>(envelope["params"]);
        foreach (var key in keys) Assert.True(parameters.ContainsKey(key), $"{command} missing wire key '{key}'");
    }
}
