using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>EdgeSelectorSpec.TryParse — API-agnostic validation of the fillet edge selector.</summary>
public class EdgeSelectorSpecTests
{
    private static JObject Sel(object o) => JObject.FromObject(o);

    [Fact]
    public void Minimal_circular_selector_parses_with_defaults()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind = "circular" }), out var spec, out var err);

        Assert.True(ok, err);
        Assert.Equal("circular", spec.Kind);
        Assert.Null(spec.RadiusMm);
        Assert.Equal(0.01, spec.RadiusTolMm);
        Assert.Null(spec.CenterMm);
        Assert.Equal(0.5, spec.CenterTolMm);
        Assert.Null(spec.OnBody);
        Assert.Null(spec.AdjacentSurfaceTypes);
    }

    [Fact]
    public void Full_selector_parses_all_fields()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new
            {
                kind = "circular",
                radius_mm = 5.0,
                radius_tol_mm = 0.05,
                center_mm = new[] { 10.0, 20.0, 0.0 },
                center_tol_mm = 1.0,
                on_body = "body:2",
                adjacent_surface_types = new[] { "plane", "cylinder" },
            }), out var spec, out var err);

        Assert.True(ok, err);
        Assert.Equal(5.0, spec.RadiusMm);
        Assert.Equal(0.05, spec.RadiusTolMm);
        Assert.Equal(new[] { 10.0, 20.0, 0.0 }, spec.CenterMm);
        Assert.Equal(1.0, spec.CenterTolMm);
        Assert.Equal("body:2", spec.OnBody);
        Assert.Equal(new[] { "plane", "cylinder" }, spec.AdjacentSurfaceTypes);
    }

    [Theory]
    [InlineData("planar")]
    [InlineData("line")]
    [InlineData("")]
    public void Unknown_kind_rejected(string kind)
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind }), out _, out var err);
        Assert.False(ok);
        Assert.Contains("kind", err);
    }

    [Fact]
    public void Non_object_selector_rejected()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            new JArray(1, 2), out _, out var err);
        Assert.False(ok);
        Assert.Contains("selector", err);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.5)]
    public void Non_positive_radius_rejected(double r)
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind = "circular", radius_mm = r }), out _, out var err);
        Assert.False(ok);
        Assert.Contains("radius_mm", err);
    }

    [Theory]
    [InlineData(new[] { 1.0, 2.0 })]           // too short
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0 })] // too long
    public void Malformed_center_rejected(double[] center)
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind = "circular", center_mm = center }), out _, out var err);
        Assert.False(ok);
        Assert.Contains("center_mm", err);
    }

    [Fact]
    public void Unknown_adjacent_surface_type_rejected()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind = "circular", adjacent_surface_types = new[] { "plane", "marble" } }),
            out _, out var err);
        Assert.False(ok);
        Assert.Contains("marble", err);
    }

    [Fact]
    public void Empty_adjacent_array_rejected()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind = "circular", adjacent_surface_types = new string[0] }),
            out _, out var err);
        Assert.False(ok);
        Assert.Contains("adjacent_surface_types", err);
    }

    [Theory]
    [InlineData("radius_mm", double.PositiveInfinity)]   // JSON 1e400 parses to +Infinity
    [InlineData("radius_mm", double.NaN)]
    [InlineData("radius_tol_mm", double.PositiveInfinity)] // infinite tolerance would disable the filter
    [InlineData("center_tol_mm", double.PositiveInfinity)]
    [InlineData("center_tol_mm", double.NegativeInfinity)]
    public void Non_finite_numbers_rejected(string key, double v)
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            new JObject { ["kind"] = "circular", [key] = v }, out _, out var err);
        Assert.False(ok);
        Assert.Contains("finite", err);
    }

    [Fact]
    public void Non_finite_center_element_rejected()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            new JObject { ["kind"] = "circular", ["center_mm"] = new JArray(0.0, double.PositiveInfinity, 0.0) },
            out _, out var err);
        Assert.False(ok);
        Assert.Contains("center_mm", err);
    }

    [Fact]
    public void Non_numeric_radius_rejected_as_invalid_argument()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind = "circular", radius_mm = "abc" }), out _, out var err);
        Assert.False(ok);
        Assert.Contains("radius_mm", err);
    }

    [Theory]
    [InlineData("radius_tol_mm", 0.0)]
    [InlineData("radius_tol_mm", -1.0)]
    [InlineData("center_tol_mm", 0.0)]
    [InlineData("center_tol_mm", -0.5)]
    public void Non_positive_tolerances_rejected(string key, double v)
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            new JObject { ["kind"] = "circular", [key] = v }, out _, out var err);
        Assert.False(ok);
        Assert.Contains(key, err);
    }

    [Fact]
    public void Empty_on_body_rejected()
    {
        var ok = Bimwright.Ipt.Shared.Contracts.EdgeSelectorSpec.TryParse(
            Sel(new { kind = "circular", on_body = "  " }), out _, out var err);
        Assert.False(ok);
        Assert.Contains("on_body", err);
    }
}
