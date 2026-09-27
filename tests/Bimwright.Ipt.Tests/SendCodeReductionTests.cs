using System;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.ToolBaker;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// send_code reliability + reuse (cycle 2, S0.1/S1.3/S1.4/S1.5/S2/S3.1): journal v3 keys, the
/// pure System.IO.Path allowlist, repair-hint rules, module source assembly, the module store,
/// result spill and document matching. All host-free.
/// </summary>
public sealed class SendCodeReductionTests
{
    // ---- S1.3 policy: pure System.IO.Path calls ------------------------------------------

    [Theory]
    [InlineData("return System.IO.Path.GetFileName(doc.FullFileName);")]
    [InlineData("var n = System.IO.Path.GetFileNameWithoutExtension(p);")]
    [InlineData("var e = System.IO.Path.GetExtension(p).ToLowerInvariant();")]
    [InlineData("var d = System.IO.Path.GetDirectoryName(p);")]
    [InlineData("var c = System.IO.Path.Combine(root, \"parts\", name + \".ipt\");")]
    [InlineData("var x = System.IO.Path.ChangeExtension(p, \".stp\");")]
    [InlineData("var g = global::System.IO.Path.GetFileName(p);")]
    [InlineData("var s = System . IO . Path . GetFileName ( p );")]
    [InlineData("var s = System\n  .IO\n  .Path.GetFileName(p);")]
    [InlineData("var s = $\"{System.IO.Path.GetFileName(p)} ok\";")]
    [InlineData("var a = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(p), System.IO.Path.GetFileName(q));")]
    public void Pure_path_calls_pass_the_policy(string code)
        => Assert.True(BakeCompilerPolicy.ValidateSource(code, "send_code").Ok, code);

    [Theory]
    [InlineData("using System.IO;\nvar n = Path.GetFileName(p);")]
    [InlineData("using P = System.IO.Path;\nvar n = P.GetFileName(p);")]
    [InlineData("using static System.IO.Path;\nvar n = GetFileName(p);")]
    [InlineData("var t = System.IO.Path.GetTempFileName();")]
    [InlineData("var t = System.IO.Path.GetTempPath();")]
    [InlineData("var t = System.IO.Path.GetFullPath(p);")]
    [InlineData("Func<string,string> f = System.IO.Path.GetFileName;")]
    [InlineData("var x = Foo.System.IO.Path.GetFileName(p);")]
    [InlineData("var n = System.IO.Path.GetFileName(System.IO.File.ReadAllText(p));")]
    [InlineData("var n = System.IO.Path.Combine(System.IO.Path.GetTempPath(), \"x\");")]
    [InlineData("System.IO.File.Delete(p);")]
    [InlineData("global::System.IO.File.Delete(p);")]
    [InlineData("var n = nameof(System.IO.File);")]
    [InlineData("var s = $\"{System.IO.File.Exists(p)}\";")]
    [InlineData("var s = System\n.IO.Directory.GetFiles(p);")]
    [InlineData("var r = doc.File.ReferencedFileDescriptors;")]
    public void File_system_access_and_path_aliases_stay_blocked(string code)
        => Assert.False(BakeCompilerPolicy.ValidateSource(code, "send_code").Ok, code);

    [Fact]
    public void File_rejection_points_to_get_document_info_references()
    {
        var r = BakeCompilerPolicy.ValidateSource("var r = doc.File.ReferencedFileDescriptors;", "send_code");
        Assert.StartsWith("send_code source uses forbidden token: File.", r.Error);
        Assert.Contains("inventor_get_document_info(references=true)", r.Error);
    }

    [Fact]
    public void Path_hint_survives_secret_masking()
    {
        var r = BakeCompilerPolicy.ValidateSource("using System.IO;", "send_code");
        Assert.Equal(r.Error, Bimwright.Ipt.Shared.Security.ErrorSanitizer.Sanitize(r.Error));
    }

    // ---- S1.4 repair hints ---------------------------------------------------------------

    [Fact]
    public void Hint_rules_load_from_the_embedded_json()
    {
        Assert.True(SendCodeHints.Rules.Count >= 30);
        Assert.Equal(SendCodeHints.Rules.Count, SendCodeHints.Rules.Select(r => r.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("CS1061", "'object' does not contain a definition for 'FullFileName' and no accessible extension method", "object-member")]
    [InlineData("CS1503", "Argument 1: cannot convert from 'string' to 'int'", "string-index")]
    [InlineData("CS1503", "Argument 2: cannot convert from 'Inventor.UnitVector' to 'Inventor.Vector'", "unitvector-to-vector")]
    [InlineData("CS1503", "Argument 1: cannot convert from 'Inventor.PartDocument' to 'Inventor._Document'", "doc-interop")]
    [InlineData("CS1503", "Argument 1: cannot convert from 'int' to 'byte'", "int-to-byte")]
    [InlineData("CS0019", "Operator '*' cannot be applied to operands of type 'object' and 'int'", "object-operator")]
    [InlineData("CS0856", "Indexed property 'SurfaceBody.Volume' has non-optional arguments which must be provided", "indexed-property-needs-args")]
    [InlineData("CS1955", "Non-invocable member 'ComponentOccurrences.ItemByName[string]' cannot be used like a method.", "indexed-property-call")]
    [InlineData("CS1545", "Property, indexer, or event 'Parameter.Units' is not supported by the language; try directly calling accessor method", "accessor-method")]
    [InlineData("CS0117", "'HealthStatusEnum' does not contain a definition for 'kUpToDateHealthStatus'", "health-enum")]
    [InlineData("CS1061", "'Asset' does not contain a definition for 'CopyToDocument'", "asset-copyto")]
    [InlineData("runtime", "ArgumentException: The parameter is incorrect. (0x80070057 (E_INVALIDARG))", "rt-invalidarg")]
    [InlineData("runtime", "COMException: Unspecified error (0x80004005 (E_FAIL))", "rt-efail")]
    [InlineData("runtime", "InvalidCastException: Unable to cast COM object of type 'Inventor._DocumentClass' to interface type 'Inventor.AssemblyDocument'.", "rt-cast-document")]
    public void Known_error_families_get_a_specific_hint(string code, string message, string expectedRule)
        => Assert.Equal(expectedRule, SendCodeHints.Match(code, message)?.Id);

    [Fact]
    public void Unknown_errors_get_no_hint()
        => Assert.Null(SendCodeHints.Match("CS1002", "; expected"));

    // ---- S2 module source assembly -------------------------------------------------------

    [Fact]
    public void Build_tags_every_part_with_a_line_directive_and_hoists_usings()
    {
        var modules = new[] { new SendCodeSource.Part("module:kit", "using System.Text;\n\nstatic int F() => 1;") };
        var src = SendCodeSource.Build(modules, "using System.Globalization;\nreturn F();");

        var lines = src.Split('\n');
        // hoisted usings come first, each with its own #line mapping
        Assert.Equal("#line 1 \"module:kit\"", lines[0]);
        Assert.Equal("using System.Text;", lines[1]);
        Assert.Equal("#line 1 \"script\"", lines[2]);
        Assert.Equal("using System.Globalization;", lines[3]);
        // bodies keep their line numbers (usings blanked in place)
        var kit = Array.IndexOf(lines, "#line 1 \"module:kit\"", 1);
        Assert.Equal("", lines[kit + 1]);
        Assert.Equal("static int F() => 1;", lines[kit + 3]);
        var script = Array.IndexOf(lines, "#line 1 \"script\"", kit);
        Assert.True(script > kit);
        Assert.Equal("return F();", lines[script + 2]);
    }

    [Fact]
    public void Using_statements_are_not_hoisted()
    {
        var src = SendCodeSource.Build(Array.Empty<SendCodeSource.Part>(), "using (var t = x) { }\nusing var y = z;");
        Assert.StartsWith("#line 1 \"script\"\nusing (var t = x)", src);
    }

    [Theory]
    [InlineData("   at Submission#0.<<Initialize>>d__0.MoveNext() in script:line 7", "script", 7)]
    [InlineData("   at Inventor.X()\n   at Submission#0.Boom() in module:kit_2:line 3\n   at Submission#0.<<Initialize>>d__0.MoveNext() in script:line 2", "module:kit_2", 3)]
    public void Failing_location_is_the_innermost_script_or_module_frame(string trace, string source, int line)
    {
        Assert.True(SendCodeSource.TryGetFailingLocation(trace, out var s, out var l));
        Assert.Equal(source, s);
        Assert.Equal(line, l);
    }

    [Fact]
    public void No_line_info_means_no_location()
        => Assert.False(SendCodeSource.TryGetFailingLocation("   at Submission#0.<<Initialize>>d__0.MoveNext()", out _, out _));

    [Fact]
    public void Hash_is_stable_16_hex()
    {
        Assert.Equal(SendCodeSource.Hash("abc"), SendCodeSource.Hash("abc"));
        Assert.Matches("^[0-9a-f]{16}$", SendCodeSource.Hash("abc"));
        Assert.NotEqual(SendCodeSource.Hash("abc"), SendCodeSource.Hash("abd"));
    }

    // ---- S2 module store -----------------------------------------------------------------

    private static CodeModuleStore NewStore()
        => new(Path.Combine(Path.GetTempPath(), "ipt-modules-" + Guid.NewGuid().ToString("N")));

    [Theory]
    [InlineData("kit")]
    [InlineData("geom_2")]
    public void Valid_module_names(string n) => Assert.Null(CodeModuleStore.ValidateName(n));

    [Theory]
    [InlineData("Kit")]
    [InlineData("2kit")]
    [InlineData("kit-x")]
    [InlineData("../x")]
    [InlineData("")]
    public void Invalid_module_names(string n) => Assert.NotNull(CodeModuleStore.ValidateName(n));

    [Fact]
    public void Resolve_orders_dependencies_first_once()
    {
        var store = NewStore();
        store.Save("geom", "static double Mm(double cm) => cm * 10;", "g", Array.Empty<string>(), null);
        store.Save("kit", "static string F() => Mm(1).ToString();", null, new[] { "geom" }, null);
        store.Save("report", "static string R() => F();", null, new[] { "kit", "geom" }, null);

        Assert.True(store.Resolve(new[] { "report", "kit" }, out var ordered, out var error), error);
        Assert.Equal(new[] { "geom", "kit", "report" }, ordered.Select(o => o.Name).ToArray());
        Assert.Equal(3, store.List().Count);
    }

    [Fact]
    public void Resolve_reports_unknown_modules_and_cycles()
    {
        var store = NewStore();
        Assert.False(store.Resolve(new[] { "nope" }, out _, out var error));
        Assert.Contains("unknown code module 'nope'", error);

        store.Save("a", "static int A() => 1;", null, new[] { "b" }, null);
        store.Save("b", "static int B() => 1;", null, new[] { "a" }, null);
        Assert.False(store.Resolve(new[] { "a" }, out _, out var cycle));
        Assert.Contains("cycle", cycle);
    }

    [Fact]
    public void Resolve_with_an_unsaved_module_uses_its_draft()
    {
        var store = NewStore();
        store.Save("geom", "static double Mm(double cm) => cm * 10;", null, Array.Empty<string>(), null);
        Assert.True(store.Resolve(new[] { "kit" }, out var ordered, out _, ("kit", "static int K() => 1;", new[] { "geom" })));
        Assert.Equal(new[] { "geom", "kit" }, ordered.Select(o => o.Name).ToArray());
        Assert.Equal("static int K() => 1;", ordered[1].Code);
    }

    [Fact]
    public void Delete_is_refused_while_another_module_requires_it()
    {
        var store = NewStore();
        store.Save("geom", "static double Mm(double cm) => cm * 10;", null, Array.Empty<string>(), null);
        store.Save("kit", "static double K() => Mm(1);", null, new[] { "geom" }, null);

        Assert.False(store.Delete("geom", out var blockedBy));
        Assert.Equal("kit", blockedBy);
        Assert.True(store.Delete("kit", out _));
        Assert.True(store.Delete("geom", out _));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Saved_entry_carries_hash_bytes_and_signatures()
    {
        var store = NewStore();
        var e = store.Save("geom", "static double Mm(double cm) => cm * 10;", "  mm helpers  ", Array.Empty<string>(),
            new JArray("double Mm(double cm)"));
        Assert.Equal(SendCodeSource.Hash("static double Mm(double cm) => cm * 10;"), e.Hash);
        Assert.Equal("mm helpers", e.Description);
        Assert.True(store.TryGet("geom", out var back, out var code));
        Assert.Equal("static double Mm(double cm) => cm * 10;", code);
        Assert.Equal("double Mm(double cm)", (string?)back.Signatures![0]);
    }

    // ---- S1.5 result spill ---------------------------------------------------------------

    [Fact]
    public void Large_result_spills_to_a_file_with_a_preview()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ipt-spill-" + Guid.NewGuid().ToString("N"));
        var big = new JArray(Enumerable.Range(0, 40_000).Select(i => new JObject { ["i"] = i, ["v"] = "row" }));
        var data = new JObject();
        ResponseSpillWriter.AttachResult("send_code", data, big, new ResponseSpillWriter(dir));

        Assert.Equal(JTokenType.Null, data["result"]!.Type);
        Assert.True((bool)data["result_truncated"]!);
        var file = (string)data["result_file"]!;
        Assert.True(File.Exists(file));
        Assert.Equal(40_000, JArray.Parse(File.ReadAllText(file)).Count);
        Assert.True(((string)data["result_preview"]!).Length <= ResponseSpillWriter.InlineKeepBytes);
    }

    [Fact]
    public void Small_result_stays_inline()
    {
        var data = new JObject();
        ResponseSpillWriter.AttachResult("send_code", data, new JObject { ["a"] = 1 });
        Assert.Equal(1, (int)data["result"]!["a"]!);
        Assert.Null(data["result_file"]);
    }

    // ---- S3.1 document matching ----------------------------------------------------------

    private static readonly DocumentMatcher.Candidate[] Docs =
    {
        new(@"D:\p\asm\TOP.iam", "TOP.iam"),
        new(@"D:\p\parts\PLATE-10.ipt", "PLATE-10.ipt"),
        new(@"D:\p\old\PLATE-10.ipt", "PLATE-10.ipt:2"),
        new(@"D:\p\parts\BOLT.ipt", "BOLT"),
        new("", "Part1"),
    };

    [Theory]
    [InlineData(@"D:\p\parts\PLATE-10.ipt", 1)]
    [InlineData("d:/p/old/plate-10.ipt", 2)]
    [InlineData("TOP.iam", 0)]
    [InlineData("top", 0)]
    [InlineData("BOLT.ipt", 3)]
    [InlineData("bolt", 3)]
    [InlineData("Part1", 4)]
    [InlineData("PLATE-10.ipt:2", 2)]
    public void Document_matches_by_path_then_name(string query, int expected)
    {
        Assert.True(DocumentMatcher.TryMatch(query, Docs, out var i, out var error), error);
        Assert.Equal(expected, i);
    }

    [Fact]
    public void Ambiguous_file_name_lists_the_candidates()
    {
        Assert.False(DocumentMatcher.TryMatch("PLATE-10", Docs, out _, out var error));
        Assert.Contains("ambiguous", error);
        Assert.Contains(@"D:\p\parts\PLATE-10.ipt", error);
        Assert.Contains(@"D:\p\old\PLATE-10.ipt", error);
    }

    [Fact]
    public void Unknown_document_is_never_opened_and_lists_open_ones()
    {
        Assert.False(DocumentMatcher.TryMatch("NOPE.ipt", Docs, out _, out var error));
        Assert.Contains("never opened automatically", error);
        Assert.Contains("TOP.iam", error);
    }

    // ---- S0.1 journal v3 -----------------------------------------------------------------

    [Fact]
    public void Finish_entry_carries_client_identity_keys()
    {
        var entry = ServerLogger.BuildFinishEntry("s", "r", "inventor_health", true, 1, null, null, null, null, null, null);
        Assert.True(entry.ContainsKey("client_name"));
        Assert.True(entry.ContainsKey("client_version"));
    }
}
