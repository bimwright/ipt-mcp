using Bimwright.Ipt.Shared.Handlers;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// <see cref="Vec3Params"/> — {x,y,z} / [x,y,z] vector parsing and the degenerate-axes check
/// used by the fixed work-plane kind (F4-P0-2) and later camera/probe params.
/// </summary>
public sealed class Vec3ParamsTests
{
    [Fact]
    public void TryParse_ObjectForm_ReadsComponents()
    {
        var ok = Vec3Params.TryParse(JObject.Parse(@"{""x"":1.5,""y"":-2,""z"":0}"), "origin",
            out var x, out var y, out var z, out var err);
        Assert.True(ok);
        Assert.Equal(1.5, x);
        Assert.Equal(-2, y);
        Assert.Equal(0, z);
        Assert.Equal("", err);
    }

    [Fact]
    public void TryParse_ArrayForm_ReadsComponents()
    {
        var ok = Vec3Params.TryParse(new JArray(1, 2, 3), "x_axis", out var x, out var y, out var z, out _);
        Assert.True(ok);
        Assert.Equal(new[] { 1.0, 2, 3 }, new[] { x, y, z });
    }

    [Fact]
    public void TryParse_MissingOrWrongShape_Rejected()
    {
        Assert.False(Vec3Params.TryParse(null, "origin", out _, out _, out _, out var e1));
        Assert.Contains("origin", e1);
        Assert.False(Vec3Params.TryParse(new JArray(1, 2), "v", out _, out _, out _, out _));           // too few
        Assert.False(Vec3Params.TryParse(new JArray(1, 2, 3, 4), "v", out _, out _, out _, out _));      // too many
        Assert.False(Vec3Params.TryParse(JObject.Parse(@"{""x"":1,""y"":2}"), "v", out _, out _, out _, out _)); // missing z
        Assert.False(Vec3Params.TryParse(new JValue("1,2,3"), "v", out _, out _, out _, out _));          // string
        Assert.False(Vec3Params.TryParse(JObject.Parse(@"{""x"":1,""y"":""a"",""z"":3}"), "v", out _, out _, out _, out _)); // non-numeric
        Assert.False(Vec3Params.TryParse(JValue.CreateNull(), "v", out _, out _, out _, out _));            // JSON null token
        Assert.False(Vec3Params.TryParse(JValue.CreateNull(), "v", out _, out _, out _, out var e2));
        Assert.Contains("v", e2);
    }

    [Fact]
    public void TryParse_ExtraKeysAndNonFinite()
    {
        // Extra keys on {x,y,z} are tolerated — only the three components matter.
        Assert.True(Vec3Params.TryParse(JObject.Parse(@"{""x"":1,""y"":2,""z"":3,""w"":9}"), "v", out _, out _, out _, out _));
        // Non-finite components are rejected before they can reach the Inventor API.
        Assert.False(Vec3Params.TryParse(new JArray(double.NaN, 0, 0), "v", out _, out _, out _, out _));
        Assert.False(Vec3Params.TryParse(new JArray(1, 2, double.PositiveInfinity), "v", out _, out _, out _, out _));
        Assert.False(Vec3Params.TryParse(JObject.Parse(@"{""x"":1,""y"":2,""z"":1e400}"), "v", out _, out _, out _, out _)); // overflow → Infinity
    }

    [Fact]
    public void AxesDegenerate_OrthogonalOrSkewed_NotDegenerate()
    {
        Assert.False(Vec3Params.AxesDegenerate(new[] { 1.0, 0, 0 }, new[] { 0.0, 1, 0 }));
        Assert.False(Vec3Params.AxesDegenerate(new[] { 1.0, 0, 0 }, new[] { 0.0, 1, 0.5 }));
        // Unnormalized magnitudes are fine — the check is about the angle, not the length.
        Assert.False(Vec3Params.AxesDegenerate(new[] { 100.0, 0, 0 }, new[] { 0.0, 0.001, 0 }));
    }

    [Fact]
    public void AxesDegenerate_ZeroOrParallel_Degenerate()
    {
        Assert.True(Vec3Params.AxesDegenerate(new[] { 0.0, 0, 0 }, new[] { 0.0, 1, 0 }));   // zero x
        Assert.True(Vec3Params.AxesDegenerate(new[] { 1.0, 0, 0 }, new[] { 0.0, 0, 0 }));   // zero y
        Assert.True(Vec3Params.AxesDegenerate(new[] { 1.0, 0, 0 }, new[] { -2.0, 0, 0 }));  // anti-parallel
        Assert.True(Vec3Params.AxesDegenerate(new[] { 1.0, 1, 1 }, new[] { 2.0, 2, 2 }));   // parallel scaled
    }
}
