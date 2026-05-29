using System;
using System.IO;
using System.Linq;

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
        Assert.DoesNotContain(".Abort(", text);
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
}
