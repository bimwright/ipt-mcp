using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Regression guard for the DTO parameter-naming finding: the MCP SDK generates its input schema with
/// System.Text.Json (Web/camelCase policy) and ignores Newtonsoft <c>[JsonProperty]</c>. Nested DTO
/// properties must therefore carry <c>[JsonPropertyName]</c> so they surface as snake_case, matching the
/// flat method parameters and the wire keys the handlers read. Without it they leak as camelCase
/// (radiusMm, nearMm, ...), which no test previously caught.
/// </summary>
public sealed class AssemblyToolSchemaTests
{
    // Static probes with the same DTO parameter types the real tools use; schema comes from the type.
    private static string ImateProbe(FaceSelectorDto selector, double offset_mm = 0) => "";
    private static string HoleProbe(HoleFaceDto face, HoleTappedDto? tapped = null) => "";
    private static string ConstraintProbe(ConstraintSideDto a, ConstraintSideDto b) => "";

    private static string SchemaOf(System.Delegate probe)
        => McpServerTool.Create(probe).ProtocolTool.InputSchema.GetRawText();

    [Fact]
    public void FaceSelector_dto_props_are_snake_case()
    {
        var s = SchemaOf(ImateProbe);
        Assert.Contains("radius_mm", s);
        Assert.Contains("near_mm", s);
        Assert.Contains("tolerance_deg", s);
        Assert.Contains("radius_tol_mm", s);
        Assert.Contains("offset_mm", s);           // flat param stays snake_case too

        Assert.DoesNotContain("radiusMm", s);
        Assert.DoesNotContain("nearMm", s);
        Assert.DoesNotContain("toleranceDeg", s);
        Assert.DoesNotContain("radiusTolMm", s);
    }

    [Fact]
    public void HoleTapped_dto_props_are_snake_case()
    {
        var s = SchemaOf(HoleProbe);
        Assert.Contains("thread_depth_mm", s);
        Assert.Contains("right_handed", s);
        Assert.Contains("full_depth", s);
        Assert.Contains("near_mm", s);

        Assert.DoesNotContain("threadDepthMm", s);
        Assert.DoesNotContain("rightHanded", s);
        Assert.DoesNotContain("fullDepth", s);
        Assert.DoesNotContain("nearMm", s);
    }

    [Fact]
    public void ConstraintSide_dto_props_are_snake_case()
    {
        var s = SchemaOf(ConstraintProbe);
        Assert.Contains("occurrence", s);
        Assert.Contains("ref", s);
    }
}
