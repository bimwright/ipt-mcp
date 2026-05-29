using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Asserts the per-version add-in TargetFramework matrix: net48 for 2022-2024, net8.0-windows7.0 for
/// 2025-2026, net10.0-windows7.0 for 2027. Reads each <c>plugin-invNN</c> csproj as XML. API-free.
/// </summary>
public sealed class TfmSplitTests
{
    private static string RepoSrc()
    {
        // Walk up from the test assembly until we find the src/ directory.
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "src");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "plugin-inv25")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate the repo src/ directory from " + dir);
    }

    private static string TargetFrameworkOf(int year)
    {
        var csproj = Path.Combine(RepoSrc(), $"plugin-inv{year - 2000}", $"Bimwright.Ipt.Plugin.Inv{year - 2000}.csproj");
        Assert.True(File.Exists(csproj), $"missing csproj: {csproj}");

        var doc = XDocument.Load(csproj);
        var tfm = doc.Descendants("TargetFramework").FirstOrDefault()?.Value
               ?? doc.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
        Assert.False(string.IsNullOrWhiteSpace(tfm), $"no TargetFramework in {csproj}");
        return tfm!.Trim();
    }

    [Theory]
    [InlineData(2022)]
    [InlineData(2023)]
    [InlineData(2024)]
    public void Years_2022_to_2024_are_net48(int year)
        => Assert.Equal("net48", TargetFrameworkOf(year));

    [Theory]
    [InlineData(2025)]
    [InlineData(2026)]
    public void Years_2025_2026_are_net8_windows(int year)
        => Assert.Equal("net8.0-windows7.0", TargetFrameworkOf(year));

    [Fact]
    public void Year_2027_is_net10_windows()
        => Assert.Equal("net10.0-windows7.0", TargetFrameworkOf(2027));
}
