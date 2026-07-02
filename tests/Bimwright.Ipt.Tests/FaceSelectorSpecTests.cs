using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class FaceSelectorSpecTests
{
    [Fact]
    public void Planar_parses_with_defaults()
    {
        var ok = FaceSelectorSpec.TryParse(JObject.Parse("{kind:'planar',normal:'+Z',extreme:'max'}"), out var s, out var err);
        Assert.True(ok, err);
        Assert.Equal("planar", s.Kind);
        Assert.Equal("+Z", s.Direction);
        Assert.Equal("max", s.Extreme);
        Assert.Equal(5.0, s.ToleranceDeg);
        Assert.Null(s.NearMm);
    }

    [Fact]
    public void Cylindrical_parses_radius_and_near()
    {
        var ok = FaceSelectorSpec.TryParse(JObject.Parse("{kind:'cylindrical',radius_mm:9,axis:'+Z',near_mm:[90,0,8]}"), out var s, out var err);
        Assert.True(ok, err);
        Assert.Equal("cylindrical", s.Kind);
        Assert.Equal(9.0, s.RadiusMm);
        Assert.Equal(0.01, s.RadiusTolMm);
        Assert.Equal(new[] { 90.0, 0.0, 8.0 }, s.NearMm);
    }

    [Theory]
    [InlineData("{}", "kind")]
    [InlineData("{kind:'weird'}", "kind")]
    [InlineData("{kind:'planar'}", "normal")]                       // planar requires normal
    [InlineData("{kind:'planar',normal:'up'}", "normal")]           // bad direction token
    [InlineData("{kind:'planar',normal:'+Z',extreme:'mid'}", "extreme")]
    [InlineData("{kind:'cylindrical'}", "radius_mm")]               // cylindrical requires radius
    [InlineData("{kind:'cylindrical',radius_mm:-1}", "radius_mm")]
    [InlineData("{kind:'planar',normal:'+Z',near_mm:[1,2]}", "near_mm")] // must be 3 numbers
    public void Invalid_specs_fail_and_name_the_field(string json, string expectedInError)
    {
        var ok = FaceSelectorSpec.TryParse(JObject.Parse(json), out _, out var err);
        Assert.False(ok);
        Assert.Contains(expectedInError, err);
    }

    [Fact]
    public void Null_object_fails()
    {
        Assert.False(FaceSelectorSpec.TryParse(null, out _, out var err));
        Assert.Contains("selector", err);
    }
}
