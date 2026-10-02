using System;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Shared.ToolBaker;

namespace Bimwright.Ipt.Tests;

public sealed class SourcePolicyTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) &&
                Directory.Exists(Path.Combine(d.FullName, "tests")) &&
                File.Exists(Path.Combine(d.FullName, "README.md")))
                return d.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repo root from " + dir);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    [Fact]
    public void SendCodeDoesNotMoveInventorApplicationOntoWorkerThread()
    {
        var text = Read(@"src\shared\Handlers\Code\SendCodeHandler.cs");

        Assert.DoesNotContain("new Thread", text);
        Assert.DoesNotContain("Thread.Abort(", text);
    }

    [Fact]
    public void AddInExportHandlersEnforceSharedOutputPathPolicy()
    {
        foreach (var path in new[]
                 {
                     @"src\shared\Handlers\Export\ExportStepHandler.cs",
                     @"src\shared\Handlers\Export\ExportStlHandler.cs",
                     @"src\shared\Handlers\Export\ExportDxfHandler.cs",
                 })
        {
            var text = Read(path);
            Assert.Contains("ExportPathPolicy.TryRejectPath", text);
        }
    }

    [Fact]
    public void SketchAndFeatureHandlersUseSharedActivePartResolver()
    {
        var paths = Directory.EnumerateFiles(Path.Combine(RepoRoot(), @"src\shared\Handlers"), "*.cs", SearchOption.AllDirectories)
            .Where(p => p.Contains(@"\Sketch\") || p.Contains(@"\Feature\"));

        foreach (var path in paths)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("ActiveDocument is not PartDocument", text);
        }
    }

    [Fact]
    public void ParameterHandlersDoNotExposeRawValueField()
    {
        var paths = Directory.EnumerateFiles(Path.Combine(RepoRoot(), @"src\shared\Handlers\Parameters"), "*.cs");

        foreach (var path in paths)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("[\"value\"] =", text);
            Assert.Contains("ParameterValueDto", text);
        }
    }

    [Fact]
    public void ServerClientUsesBoundedResponseRead()
    {
        var text = Read(@"src\server\PluginClient.cs");

        Assert.DoesNotContain("ReadLineAsync", text);
        Assert.Contains("NdjsonLineReader.ReadLineBoundedAsync", text);
    }

    [Fact]
    public void AddInReadOnlyContextIsNotHardCodedFalse()
    {
        var text = Read(@"src\shared\Plugin\InventorAddInServerBase.cs");

        Assert.DoesNotContain("ReadOnly = false", text);
        Assert.Contains("ReadOnly = o.ReadOnly || env.ReadOnly", text);
    }

    [Fact]
    public void MassPropertiesUsesAreaUnitHelper()
    {
        var text = Read(@"src\shared\Handlers\Properties\GetMassPropertiesHandler.cs");

        Assert.Contains("UnitConvert.Cm2ToMm2", text);
        Assert.DoesNotContain("areaCm2 * 100.0", text);
    }

    // ---- F2-c: token-aware source policy --------------------------------------------
    //
    // The scan strips comments and string/char literals, then matches tokens on word
    // boundaries, case-sensitively. Type-metadata reads (typeof/GetType) are allowed;
    // invoke/load-style reflection stays blocked.

    [Theory]
    // False positives observed in the 2026-09-15 WS2 run — all must now pass.
    [InlineData("var name = \"Drain_1p5NPT_VisibleSocket\";")]                 // #13: banned token inside a string literal
    [InlineData("try { } catch (Exception ex) { var n = ex.GetType().Name; }")] // #140: GetType in a catch block
    [InlineData("var t = typeof(int);")]                                        // #130: typeof is metadata-only
    [InlineData("var t = app.ActiveDocument.GetType();")]                       // #131: GetType is metadata-only
    // Boundary / case-sensitivity checks.
    [InlineData("var visibleSocket = 1; Console.WriteLine(visibleSocket);")]
    [InlineData("var socket = new SocketAdapter(); socket.Close();")]           // lowercase identifier containing the token
    [InlineData("var file = OpenData(); file.Write(\"x\");")]                   // lowercase var.member is not File.
    [InlineData("var process = GetProcessLike(); process.Refresh();")]          // lowercase process var
    // Literals and comments are stripped before matching.
    [InlineData("// File.Delete(\"x\");\nvar c = 1;")]
    [InlineData("/* System.IO.File.Delete(\"x\") */ var c = 1;")]
    [InlineData("var path = @\"C:\\out\\File.txt\";")]
    [InlineData("var note = \"Process.Start is banned\";")]
    [InlineData("var s = $\"count={app.Documents.Count}\";")]                   // interpolated hole is scanned: safe content passes
    public void CompilerPolicy_allows_previously_false_positive_or_safe_source(string source)
    {
        var result = BakeCompilerPolicy.ValidateSource(source);
        Assert.True(result.Ok, "expected pass, got: " + result.Error);
    }

    [Theory]
    [InlineData("System.IO.File.Delete(\"x\");")]
    [InlineData("var d = System.IO.Directory.CreateDirectory(\"x\");")]
    [InlineData("System.Diagnostics.Process.Start(\"calc\");")]
    [InlineData("var v = System.Environment.GetEnvironmentVariable(\"PATH\");")]
    [InlineData("var c = new System.Net.Http.HttpClient();")]
    [InlineData("var s = new System.Net.Sockets.Socket(default, default, default);")]
    [InlineData("File.WriteAllText(\"x\", \"y\");")]                            // bare File. still blocked
    [InlineData("Process.Start(\"calc\");")]                                    // bare Process still blocked
    [InlineData("var p = Environment.GetFolderPath(default);")]                 // bare Environment. still blocked
    // Invoke/load-style reflection stays blocked now that typeof/GetType are allowed.
    [InlineData("typeof(string).GetMethod(\"X\").Invoke(null, null);")]
    [InlineData("typeof(string).GetMethods();")]
    [InlineData("typeof(string).GetProperty(\"P\").GetValue(null);")]           // GetValue itself unreachable without getters
    [InlineData("typeof(string).GetField(\"f\");")]
    [InlineData("var m = typeof(string).GetMember(\"X\");")]
    [InlineData("var mi = typeof(string).GetMethods()[0]; mi.DynamicInvoke(null);")]
    [InlineData("System.Reflection.Assembly.Load(\"x\");")]
    [InlineData("System.Reflection.Assembly.LoadFrom(\"x.dll\");")]
    [InlineData("var a = Activator.CreateInstance(t);")]
    [InlineData("var d = Delegate.CreateDelegate(typeof(Action), null, \"M\");")]
    // Evasions the naive substring scan missed or mishandled.
    [InlineData("System . IO . File.Delete(\"x\");")]                           // whitespace around dots
    [InlineData("var s = $\"{System.IO.File.ReadAllText(\"x\")}\";")]           // banned call inside an interpolation hole
    [InlineData("var s = $\"{System.IO.File.ReadAllText(@\"x\")}\";")]
    [InlineData("using Bimwright.Ipt.Shared.ToolBaker;")]
    public void CompilerPolicy_still_rejects_dangerous_source(string source)
    {
        var result = BakeCompilerPolicy.ValidateSource(source);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CompilerPolicy_message_carries_the_calling_context_label()
    {
        var sendCode = BakeCompilerPolicy.ValidateSource("System.IO.File.Delete(\"x\");", "send_code");
        Assert.StartsWith("send_code source uses forbidden token: System.IO", sendCode.Error);

        var baked = BakeCompilerPolicy.ValidateSource("System.IO.File.Delete(\"x\");");
        Assert.StartsWith("Baked tool source uses forbidden token: System.IO", baked.Error);
    }

    [Fact]
    public void CompilerPolicy_strips_all_literal_forms()
    {
        var stripped = BakeCompilerPolicy.StripLiteralsAndComments(
            "var a = \"File.x\";\n" +
            "var b = @\"File.x\";\n" +
            "var c = $\"File.{1}x\";\n" +
            "var d = 'x';\n" +
            "// File.x\n" +
            "/* File.x */\n" +
            "var e = \"\"\"File.x\"\"\";");

        Assert.DoesNotContain("File", stripped);
        Assert.Contains("var a = ;", stripped);
        Assert.Contains("var e = ;", stripped);
    }
}
