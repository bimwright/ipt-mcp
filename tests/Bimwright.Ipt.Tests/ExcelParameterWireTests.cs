using System.IO.Pipes;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ExcelParameterWireTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ipt-excel-wire-" + Guid.NewGuid().ToString("N"));
    private readonly string _pipe = "ipt-excel-" + Guid.NewGuid().ToString("N");
    private readonly ParameterTools _tools;
    public ExcelParameterWireTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId, InventorYear = 2027,
            HostApp = "Inventor", Transport = "pipe", PipeName = _pipe, AuthToken = "test-token", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));
        _tools = new ParameterTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _directory, TimeoutMs = 5000 }));
    }
    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData("list_excel_tables", false)]
    [InlineData("link_excel_table", false)]
    [InlineData("reload_excel_table", false)]
    [InlineData("link_excel_table", true)]
    [InlineData("reload_excel_table", true)]
    public async Task Excel_tools_forward_target_and_preserve_verified_or_failed_receipts(string command, bool fail)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var data = fail
            ? new JObject { ["rolled_back"] = false, ["rollback_failed"] = true, ["mutation_applied"] = "unknown", ["parameters_before"] = new JObject { ["Diameter"] = 210 }, ["recovery_required"] = "restore known-good Excel and reload" }
            : new JObject { ["verified_saved_values"] = true, ["changed_parameters"] = new JArray("Diameter"), ["source_sha256"] = "saved-hash" };
        async Task<JObject> Receive()
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var envelope = JObject.Parse((await reader.ReadLineAsync(timeout.Token))!);
            await writer.WriteLineAsync(new JObject { ["id"] = envelope["id"], ["ok"] = !fail, ["data"] = data,
                ["error"] = fail ? new JObject { ["code"] = "API_ERROR", ["message"] = "native update failed" } : null, ["meta"] = new JObject() }.ToString(Formatting.None));
            return envelope;
        }
        var receive = Receive();
        var response = JObject.Parse(await (command switch
        {
            "list_excel_tables" => _tools.ListExcelTables("copy.ipt", timeout.Token),
            "link_excel_table" => _tools.LinkExcelTable(@"C:\fixture.xlsx", "A6", "copy.ipt", timeout.Token),
            _ => _tools.ReloadExcelTable("excel:table-id", "copy.ipt", timeout.Token),
        }));
        var request = await receive;
        Assert.Equal(command, (string?)request["command"]);
        Assert.Equal("copy.ipt", (string?)request["params"]?["document"]);
        if (command == "link_excel_table") { Assert.Equal(@"C:\fixture.xlsx", (string?)request["params"]?["path"]); Assert.Equal("A6", (string?)request["params"]?["start_cell"]); }
        if (command == "reload_excel_table") Assert.Equal("excel:table-id", (string?)request["params"]?["table_id"]);
        if (fail) Assert.False((bool)response["ok"]!);
        Assert.True(JToken.DeepEquals(data, fail ? response["data"] : response), response.ToString());
    }
}
