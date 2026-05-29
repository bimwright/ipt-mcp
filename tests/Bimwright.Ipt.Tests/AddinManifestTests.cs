using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// XML-parses all six per-version <c>.addin</c> manifests and asserts the invariants that the
/// Phase-2 reviewer flagged as previously untestable (the version-bracket bug):
/// <list type="bullet">
///   <item><c>ClassId == ClientId</c> (Inventor requires both, and they must match the COM GUID).</item>
///   <item>The ClassId GUID equals the <c>[Guid(...)]</c> on that version's
///         <c>InventorAddInServer</c> (the COM registration the manifest points at).</item>
///   <item>The <c>Assembly</c> element names the version-specific DLL
///         (<c>Bimwright.Ipt.Plugin.InvNN.dll</c>).</item>
///   <item>The <c>SupportedSoftwareVersionGreaterThan</c>/<c>...LessThan</c> brackets isolate
///         <b>exactly one</b> internal major version, where internal = calendar year - 1996
///         (2022=26 … 2027=31). This is the assertion that would have caught the bracket bug.</item>
/// </list>
/// API-free: reads files on disk, no Inventor needed.
/// </summary>
public sealed class AddinManifestTests
{
    /// <summary>Inventor's internal major version is the calendar year minus 1996.</summary>
    private const int YearToInternalOffset = 1996;

    private static string RepoSrc()
    {
        // Walk up from the test assembly until we find the src/ directory (same pattern as TfmSplitTests).
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "src");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "plugin-inv25")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate the repo src/ directory from " + dir);
    }

    private static string PluginDir(int year) => Path.Combine(RepoSrc(), $"plugin-inv{year - 2000}");

    private static XElement LoadAddin(int year)
    {
        var nn = year - 2000;
        var path = Path.Combine(PluginDir(year), $"Bimwright.Ipt.Inv{nn:00}.addin");
        Assert.True(File.Exists(path), $"missing .addin manifest: {path}");
        var root = XDocument.Load(path).Root;
        Assert.NotNull(root);
        Assert.Equal("Addin", root!.Name.LocalName);
        return root;
    }

    private static string Element(XElement addin, string name)
    {
        var el = addin.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        Assert.True(el != null, $"<{name}> element missing from manifest");
        return el!.Value.Trim();
    }

    /// <summary>Parse a brace-wrapped GUID; fail the test if it is not a well-formed GUID.</summary>
    private static Guid ParseGuid(string raw, string what)
    {
        Assert.True(Guid.TryParse(raw, out var g), $"{what} is not a well-formed GUID: '{raw}'");
        return g;
    }

    /// <summary>
    /// Read the <c>[Guid("...")]</c> attribute off the per-version <c>InventorAddInServer.cs</c>.
    /// We parse the source text rather than reflect (the plugin assemblies are not built / loadable
    /// in this server-only test process).
    /// </summary>
    private static Guid ServerClassGuid(int year)
    {
        var src = Path.Combine(PluginDir(year), "InventorAddInServer.cs");
        Assert.True(File.Exists(src), $"missing entrypoint source: {src}");
        var text = File.ReadAllText(src);
        var m = System.Text.RegularExpressions.Regex.Match(
            text, @"\[\s*Guid\(\s*""([^""]+)""\s*\)\s*\]");
        Assert.True(m.Success, $"no [Guid(...)] attribute found in {src}");
        return ParseGuid(m.Groups[1].Value, "InventorAddInServer [Guid]");
    }

    [Theory]
    [InlineData(2022)]
    [InlineData(2023)]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void ClassId_equals_ClientId(int year)
    {
        var addin = LoadAddin(year);
        var classId = ParseGuid(Element(addin, "ClassId"), "ClassId");
        var clientId = ParseGuid(Element(addin, "ClientId"), "ClientId");
        Assert.Equal(classId, clientId);
    }

    [Theory]
    [InlineData(2022)]
    [InlineData(2023)]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void ClassId_matches_entrypoint_Guid_attribute(int year)
    {
        var addin = LoadAddin(year);
        var classId = ParseGuid(Element(addin, "ClassId"), "ClassId");
        Assert.Equal(ServerClassGuid(year), classId);
    }

    [Theory]
    [InlineData(2022)]
    [InlineData(2023)]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void Assembly_name_matches_the_version(int year)
    {
        var addin = LoadAddin(year);
        Assert.Equal($"Bimwright.Ipt.Plugin.Inv{year - 2000:00}.dll", Element(addin, "Assembly"));
    }

    /// <summary>
    /// Every ClassId GUID across the six manifests must be distinct — otherwise two versions would
    /// register the same COM class and collide.
    /// </summary>
    [Fact]
    public void All_six_ClassIds_are_distinct()
    {
        var ids = new[] { 2022, 2023, 2024, 2025, 2026, 2027 }
            .Select(y => ParseGuid(Element(LoadAddin(y), "ClassId"), "ClassId"))
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>
    /// The core regression guard for the bracket bug: the GreaterThan/LessThan pair must bracket
    /// exactly one internal major version, and that version must be (year - 1996).
    /// The manifest encodes bounds as "M.." where ".." is the minor/service-pack wildcard.
    /// </summary>
    [Theory]
    [InlineData(2022)]
    [InlineData(2023)]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void Version_brackets_isolate_exactly_one_internal_version(int year)
    {
        var addin = LoadAddin(year);
        var expected = year - YearToInternalOffset;

        var lower = ParseBracketMajor(Element(addin, "SupportedSoftwareVersionGreaterThan"), "GreaterThan");
        var upper = ParseBracketMajor(Element(addin, "SupportedSoftwareVersionLessThan"), "LessThan");

        // Bounds are strict (GreaterThan / LessThan). The set of internal majors v with
        // lower < v < upper must be exactly { expected }.
        Assert.True(lower < upper, $"{year}: lower bound {lower} not below upper bound {upper}");

        var isolated = Enumerable.Range(lower + 1, Math.Max(0, upper - lower - 1)).ToList();
        Assert.Single(isolated);
        Assert.Equal(expected, isolated[0]);
    }

    /// <summary>Parse the integer major from a "M.." bracket bound (the ".." is the SP wildcard).</summary>
    private static int ParseBracketMajor(string raw, string which)
    {
        var head = raw.Split('.')[0];
        Assert.True(int.TryParse(head, out var major), $"{which} bound '{raw}' has no integer major");
        return major;
    }
}
