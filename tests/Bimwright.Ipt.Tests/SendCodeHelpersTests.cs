using System.IO.Pipes;
using System.Reflection;
using System.Text.RegularExpressions;
using Bimwright.Ipt.Scripting;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Code;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

// script_helpers_v1: loop guard, time budget, number cleaning and the send_code surface. The Ipt.* helpers
// themselves call the Inventor API and are checked live (internal test notes), not here.
public sealed class SendCodeHelpersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ipt-send-helpers-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    // ---- loop guard: shape ------------------------------------------------------------------------------------

    [Fact]
    public void Source_without_loops_is_returned_unchanged()
    {
        const string code = "var x = 1;\nreturn x + 1;";
        Assert.Same(code, ScriptLoopGuard.Instrument(code));
    }

    [Theory]
    [InlineData("for (int i = 0; i < 3; i++) { n++; }")]
    [InlineData("foreach (var v in new[] { 1, 2 }) { n += v; }")]
    [InlineData("foreach (var (a, b) in new[] { (1, 2) }) { n += a + b; }")]
    [InlineData("while (n < 3) { n++; }")]
    [InlineData("do { n++; } while (n < 3);")]
    [InlineData("while (n < 3) n++;")]
    public void Every_loop_kind_gets_a_guard(string loop)
    {
        var result = ScriptLoopGuard.Instrument("int n = 0;\n" + loop + "\nreturn n;");
        Assert.Single(Regex.Matches(result, Regex.Escape(ScriptLoopGuard.CheckCall)));
        Assert.Contains(ScriptLoopGuard.CheckCall + "(\"script\", 2);", result);
        AssertNoErrors(result);
    }

    [Fact]
    public void Nested_and_single_statement_loops_are_all_guarded_and_lines_do_not_move()
    {
        const string code =
            "int n = 0;\n" +
            "for (int i = 0; i < 2; i++)\n" +
            "    foreach (var j in new[] { 1, 2 })\n" +
            "    {\n" +
            "        while (n < 0) n++;\n" +
            "        n += j;\n" +
            "    }\n" +
            "return n;";
        var result = ScriptLoopGuard.Instrument(code);
        Assert.Equal(3, Regex.Matches(result, Regex.Escape(ScriptLoopGuard.CheckCall)).Count);
        var before = code.Split('\n');
        var after = result.Split('\n');
        Assert.Equal(before.Length, after.Length);
        // Every original line still holds its own code on the same line number (ignoring the guard and added braces).
        for (var i = 0; i < before.Length; i++)
            Assert.Equal(Bare(before[i]), Bare(after[i]));
        // Each guard names the line its loop statement starts on.
        foreach (var line in new[] { 2, 3, 5 })
            Assert.Contains($"(\"script\", {line})", result);
        AssertNoErrors(result);
    }

    [Fact]
    public void Loops_report_the_source_part_and_line_of_the_built_source()
    {
        var module = new SendCodeSource.Part(SendCodeSource.ModulePrefix + "geo", "static int Sum(int[] xs)\n{\n    var s = 0;\n    foreach (var x in xs) s += x;\n    return s;\n}");
        var built = SendCodeSource.Build(new[] { module }, "var t = 0;\nwhile (t < 1) t++;\nreturn Sum(new[] { t });");
        var result = ScriptLoopGuard.Instrument(built);
        Assert.Contains("(\"module:geo\", 4)", result);
        Assert.Contains("(\"script\", 2)", result);
        Assert.Equal(built.Split('\n').Length, result.Split('\n').Length);
    }

    // ---- loop guard: run time ---------------------------------------------------------------------------------

    [Fact]
    public void An_endless_loop_stops_with_its_line_when_the_budget_runs_out()
    {
        const string code =
            "public static class Endless\n" +
            "{\n" +
            "    public static int Run()\n" +
            "    {\n" +
            "        var n = 0;\n" +
            "        while (true)\n" +
            "        {\n" +
            "            n++;\n" +
            "        }\n" +
            "    }\n" +
            "}";
        var run = Load(ScriptLoopGuard.Instrument(code)).GetType("Endless")!.GetMethod("Run")!;
        var started = System.Diagnostics.Stopwatch.StartNew();
        IptGuard.Begin(100);
        try
        {
            var ex = Assert.Throws<TargetInvocationException>(() => run.Invoke(null, null));
            var stop = Assert.IsType<IptTimeoutException>(ex.InnerException);
            Assert.Equal(6, stop.Line);
            Assert.Equal("script", stop.Source);
            Assert.Contains("script line 6", stop.Message);
            Assert.Contains("rolled back", stop.Message);
        }
        finally { IptGuard.End(); }
        Assert.True(started.ElapsedMilliseconds < 5000, $"stopped after {started.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Without_a_budget_the_guard_never_stops_a_loop()
    {
        IptGuard.End();
        for (var i = 0; i < 1000; i++) IptGuard.Check("script", 1);
        IptGuard.Begin(0);
        IptGuard.Check("script", 1);
        IptGuard.End();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(60000, 58000)]   // margin capped at 2 s
    [InlineData(10000, 9500)]    // margin 5 %
    [InlineData(1000, 800)]      // margin at least 200 ms
    [InlineData(100, 1)]         // already past the margin: stop at the first loop iteration
    public void Budget_leaves_a_margin_before_the_deadline(int? leftMs, long expected)
    {
        var now = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, SendCodeHelpers.BudgetMs(leftMs is { } l ? now.AddMilliseconds(l) : null, now));
    }

    // ---- numbers ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2388.400000000001, 2388.4)]
    [InlineData(1.2246467991473532e-16, 0)]
    [InlineData(-3e-12, 0)]
    [InlineData(0.30000000000000004, 0.3)]
    [InlineData(123456789.123456789, 123456789.123)]
    [InlineData(1e-6, 1e-6)]
    [InlineData(-42.5, -42.5)]
    public void Doubles_lose_floating_point_noise(double raw, double expected)
    {
        Assert.Equal(expected, ScriptNumbers.Clean(raw));
    }

    [Fact]
    public void Cleaning_walks_objects_and_arrays_and_leaves_other_values_alone()
    {
        var token = JToken.FromObject(new
        {
            z_mm = 2388.400000000001,
            count = 3,
            big = 9007199254740993L,
            name = "0.30000000000000004",
            box = new[] { 1.2e-16, 10.000000000000002 },
            nested = new { f = 0.1f, nan = double.NaN },
        });
        ScriptNumbers.Clean(token);
        Assert.Equal(2388.4, (double)token["z_mm"]!);
        Assert.Equal(3, (int)token["count"]!);
        Assert.Equal(9007199254740993L, (long)token["big"]!);
        Assert.Equal("0.30000000000000004", (string?)token["name"]);
        Assert.Equal(new[] { 0d, 10d }, token["box"]!.Values<double>().ToArray());
        Assert.Equal(0.1, (double)token["nested"]!["f"]!);
        Assert.True(double.IsNaN((double)token["nested"]!["nan"]!));
    }

    // ---- task contract ----------------------------------------------------------------------------------------

    [Fact]
    public void Legacy_script_returning_McpTaskResult_gets_a_contract_hint()
    {
        var data = new JObject { ["ok"] = true, ["error"] = null, ["result"] = new JObject { ["IsPassed"] = false } };
        SendCodeTask.Apply(data, "legacy", completed: true, McpTaskResult.Fail("X", "y"));
        Assert.True((bool)data["ok"]!);
        Assert.Contains("result_contract=\"task_v1\"", (string?)data["task_contract_hint"]);
    }

    // ---- send_code surface ------------------------------------------------------------------------------------

    [Fact]
    public void Send_code_description_lists_the_helpers_and_raw_numbers()
    {
        var method = typeof(CodeTools).GetMethod(nameof(CodeTools.SendCode))!;
        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
        foreach (var name in new[] { "Ipt.Doc", "Ipt.Occs", "Ipt.BboxMm", "Ipt.Box", "Ipt.Ref", "Ipt.Sheet", "Ipt.Prop", "Ipt.TolMm", "Ipt.Help()", "raw_numbers", "clean_v1", "time-guarded" })
            Assert.Contains(name, description);
        Assert.Contains(method.GetParameters(), p => p.Name == "raw_numbers" && p.ParameterType == typeof(bool));
    }

    [Fact]
    public void Send_code_description_starts_by_pointing_at_the_helpers()
    {
        var description = typeof(CodeTools).GetMethod(nameof(CodeTools.SendCode))!
            .GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
        Assert.StartsWith("Before writing a script, reuse the Ipt.* helpers", description);
        Assert.Contains("helper_hint", description.Substring(0, 400));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raw_numbers_is_only_sent_on_the_wire_when_set(bool raw)
    {
        var (request, _) = await RunSendCode("return 0.1 + 0.2;", raw, DocumentTargetFixture.Capabilities);
        Assert.Equal(raw ? true : null, (bool?)request["params"]?["raw_numbers"]);
    }

    // ---- helper_hint ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("foreach (Document d in app.Documents) if (d.DisplayName == \"A\") return d;", "Ipt.Doc(")]
    [InlineData("var x = app.Documents.Cast<Document>().First(d => d.FullFileName.EndsWith(\"A.ipt\"));", "Ipt.Doc(")]
    [InlineData("foreach (ComponentOccurrence o in asm.ComponentDefinition.Occurrences) n++;", "Ipt.Occs(")]
    [InlineData("foreach (ComponentOccurrence o in def.Occurrences.AllLeafOccurrences) n++;", "Ipt.Occs(")]
    [InlineData("foreach (Sheet sh in dd.Sheets) if (sh.Name == \"FAB-004:4\") s = sh;", "Ipt.Sheet(")]
    [InlineData("foreach (DrawingView v in sh.DrawingViews) if (v.Name == \"E\") view = v;", "Ipt.View(")]
    [InlineData("foreach (CustomTable t in sh.CustomTables) n++;", "Ipt.Table(")]
    [InlineData("foreach (TextBox tb in sk.TextBoxes) n++;", "Ipt.Texts(")]
    [InlineData("var b = occ.RangeBox; return b.MaxPoint.Z * 10;", "Ipt.BboxMm(")]
    [InlineData("return doc.PropertySets[\"Design Tracking Properties\"][\"Part Number\"].Value;", "Ipt.Prop(")]
    [InlineData("return Math.Round(x * 10, 3);", "clean_v1")]
    public void Hand_written_lookups_get_the_matching_helper(string code, string expected)
    {
        var hint = SendCodeHelperHints.Match(code);
        Assert.NotNull(hint);
        Assert.Contains(expected, hint);
        Assert.EndsWith("Return Ipt.Help() for every signature.", hint);
    }

    [Theory]
    [InlineData("return Ipt.Occs(null, new { regex = \"^INT-\" }).Count;")]
    [InlineData("var o = asm.ComponentDefinition.Occurrences.Add(path, m); return o.Name;")]
    [InlineData("return Math.Round(x, 2);")]
    public void Helper_calls_placement_and_raw_rounding_get_no_hint(string code)
    {
        Assert.Null(SendCodeHelperHints.Match(code, rawNumbers: code.Contains("Math.Round")));
    }

    [Fact]
    public void Hint_names_at_most_three_helpers()
    {
        var hint = SendCodeHelperHints.Match(
            "foreach (Document d in app.Documents) {} foreach (Sheet s in d.Sheets) {} foreach (DrawingView v in s.DrawingViews) {} " +
            "foreach (CustomTable t in s.CustomTables) {} return Math.Round(1.0, 2);")!;
        Assert.Contains("(+2 more)", hint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Helper_hint_is_added_only_for_an_add_in_with_the_helpers(bool helpers)
    {
        var capabilities = helpers ? DocumentTargetFixture.Capabilities.Append(SendCodeHelperHints.Capability).ToArray() : DocumentTargetFixture.Capabilities;
        var (_, response) = await RunSendCode("foreach (Document d in app.Documents) if (d.DisplayName == \"A\") return d.FullFileName; return null;", false, capabilities);
        var hint = (string?)JObject.Parse(response)["helper_hint"];
        if (helpers) Assert.Contains("Ipt.Doc(", hint); else Assert.Null(hint);
    }

    // ---- report_task_result / get_drawing_info ------------------------------------------------------------

    [Fact]
    public void Report_summary_error_states_the_length_and_where_detail_goes()
    {
        var error = Bimwright.Ipt.Shared.Contracts.ReportLimits.Validate("t1", "completed", new string('x', 143), null);
        Assert.Equal("summary must be a single-line string of 1-120 characters (got 143); put detail in body.", error);
        Assert.Contains("at most 120 characters", Bimwright.Ipt.Server.Reports.ReportTracker.Text);
    }

    [Fact]
    public void Get_drawing_info_accepts_styles_false()
    {
        DrawingInput.Validate("get_drawing_info", JObject.Parse("{include:'summary',sheet:'FAB-004:4',references:false,styles:false}"));
        Assert.Contains(typeof(DrawingQueryTools).GetMethod(nameof(DrawingQueryTools.GetDrawingInfo))!.GetParameters(), p => p.Name == "styles");
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    /// <summary>One send_code call against a fake add-in on a named pipe: returns the request line and the tool response.</summary>
    private async Task<(JObject Request, string Response)> RunSendCode(string code, bool raw, string[] capabilities)
    {
        var pipe = "ipt-send-helpers-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        File.WriteAllText(Path.Combine(_root, "target.json"), JsonConvert.SerializeObject(new TargetDescriptor
        {
            TargetId = "inventor-2027-" + Environment.ProcessId, ProcessId = Environment.ProcessId,
            InventorYear = 2027, HostApp = "Inventor", Transport = "pipe", PipeName = pipe,
            AuthToken = "test-token", Capabilities = capabilities, LastHeartbeatUtc = DateTimeOffset.UtcNow,
        }));
        var tools = new CodeTools(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _root, TimeoutMs = 5000 }).Confirmed(),
            new CodeModuleStore(Path.Combine(_root, "modules")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        var call = tools.SendCode(code, raw_numbers: raw, ct: timeout.Token);
        await connected;
        using var input = new StreamReader(server, leaveOpen: true);
        using var output = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var request = JObject.Parse((await input.ReadLineAsync(timeout.Token))!);
        await output.WriteLineAsync(new JObject { ["id"] = request["id"], ["ok"] = true, ["data"] = new JObject { ["ok"] = true } }.ToString(Formatting.None));
        return (request, await call);
    }

    private static string Bare(string line) =>
        Regex.Replace(Regex.Replace(line, Regex.Escape(ScriptLoopGuard.CheckCall) + @"\(""[^""]*"", \d+\);", ""), @"[\s{}]", "");

    /// <summary>Instrumented script text must still compile as a script (the guard call resolves to IptGuard).</summary>
    private static void AssertNoErrors(string scriptSource)
    {
        var tree = CSharpSyntaxTree.ParseText(scriptSource, new CSharpParseOptions(kind: SourceCodeKind.Script));
        var compilation = CSharpCompilation.CreateScriptCompilation("guarded", tree, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), returnType: typeof(object));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.ToString())) + "\n" + scriptSource);
    }

    private static Assembly Load(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("guarded_" + Guid.NewGuid().ToString("N"), new[] { tree }, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(ms.ToArray());
    }

    private static IEnumerable<MetadataReference> References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => MetadataReference.CreateFromFile(p))
        .Append(MetadataReference.CreateFromFile(typeof(IptGuard).Assembly.Location));
}
