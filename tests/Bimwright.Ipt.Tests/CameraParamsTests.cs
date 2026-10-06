using Bimwright.Ipt.Shared.Handlers.Export;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class CameraParamsTests
{
    private static JObject P(string json) => JObject.Parse(json);

    [Fact]
    public void Full_spec_parses()
    {
        var ok = CameraParams.TryParse(
            P("""{"eye":{"x":100,"y":80,"z":60},"target":[0,0,0],"up":[0,0,1],"perspective":true,"extents_mm":[200,150],"fit":true}"""),
            out var s, out var err);
        Assert.True(ok, err);
        Assert.Equal(new[] { 100.0, 80.0, 60.0 }, s.Eye);
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, s.Target);
        Assert.Equal(new[] { 0.0, 0.0, 1.0 }, s.Up);
        Assert.True(s.Perspective);
        Assert.Equal(new[] { 200.0, 150.0 }, s.ExtentsMm);
        Assert.True(s.Fit);
    }

    [Theory]
    [InlineData("""{"eye":[10,0,0]}""")]
    [InlineData("""{"target":{"x":0,"y":0,"z":0}}""")]
    [InlineData("""{"up":[0,1,0]}""")]
    [InlineData("""{"perspective":false}""")]
    [InlineData("""{"extents_mm":{"width":100,"height":50}}""")]
    [InlineData("""{"fit":true}""")]
    public void Single_param_suffices(string json)
        => Assert.True(CameraParams.TryParse(P(json), out _, out _));

    [Fact]
    public void Empty_call_rejected()
    {
        Assert.False(CameraParams.TryParse(P("{}"), out _, out var err));
        Assert.Contains("at least one", err);
    }

    [Fact]
    public void Explicit_nulls_count_as_absent()
        => Assert.False(CameraParams.TryParse(P("""{"eye":null,"fit":false}"""), out _, out _));

    [Theory]
    [InlineData("""{"eye":[1,2]}""", "eye")]
    [InlineData("""{"eye":{"x":1,"y":2}}""", "eye")]
    [InlineData("""{"target":"front"}""", "target")]
    [InlineData("""{"up":[0,0,0]}""", "non-zero")]
    [InlineData("""{"perspective":"yes"}""", "boolean")]
    [InlineData("""{"extents_mm":[100]}""", "extents_mm")]
    [InlineData("""{"extents_mm":[100,0]}""", "extents_mm")]
    [InlineData("""{"extents_mm":[-5,10]}""", "extents_mm")]
    public void Malformed_params_rejected(string json, string needle)
    {
        Assert.False(CameraParams.TryParse(P(json), out _, out var err));
        Assert.Contains(needle, err);
    }

    [Fact]
    public void Eye_equal_target_rejected()
    {
        Assert.False(CameraParams.TryParse(
            P("""{"eye":[10,10,10],"target":{"x":10,"y":10,"z":10}}"""), out _, out var err));
        Assert.Contains("eye and target", err);
    }

    [Fact]
    public void Up_parallel_to_view_direction_rejected()
    {
        // eye→target looks straight down +X; up = [2,0,0] is parallel.
        Assert.False(CameraParams.TryParse(
            P("""{"eye":[0,0,0],"target":[50,0,0],"up":[2,0,0]}"""), out _, out var err));
        Assert.Contains("parallel", err);
    }

    [Fact]
    public void Up_parallel_check_needs_both_endpoints()
    {
        // up parallel to *a possible* view dir is fine when we can't know the final direction.
        Assert.True(CameraParams.TryParse(P("""{"up":[1,0,0],"eye":[0,0,0]}"""), out _, out _));
    }

    [Fact]
    public void Non_finite_components_rejected()
    {
        Assert.False(CameraParams.TryParse(P("""{"eye":[1e400,0,0]}"""), out _, out _));
        Assert.False(CameraParams.TryParse(P("""{"extents_mm":[1e400,10]}"""), out _, out _));
    }
}
