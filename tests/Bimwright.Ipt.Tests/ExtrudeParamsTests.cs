using Bimwright.Ipt.Shared.Handlers.Feature;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// <see cref="ExtrudeParams"/> — the API-agnostic half of the extrude extension (F4-P0):
/// distance as number-or-expression, and the affected_bodies/new_body incompatibility check.
/// </summary>
public sealed class ExtrudeParamsTests
{
    [Fact]
    public void TryParseDistance_Number_TreatedAsMm()
    {
        var ok = ExtrudeParams.TryParseDistance(new JValue(40.0), out var mm, out var expr, out var err);
        Assert.True(ok);
        Assert.Equal(40.0, mm);
        Assert.Null(expr);
        Assert.Equal("", err);
    }

    [Fact]
    public void TryParseDistance_NumericString_TreatedAsMm()
    {
        var ok = ExtrudeParams.TryParseDistance(new JValue("40"), out var mm, out var expr, out _);
        Assert.True(ok);
        Assert.Equal(40.0, mm);
        Assert.Null(expr);
    }

    [Theory]
    [InlineData("40 mm")]
    [InlineData("plate_thk")]
    [InlineData("plate_thk * 2 + 1 mm")]
    public void TryParseDistance_ExpressionString_PassedVerbatim(string s)
    {
        var ok = ExtrudeParams.TryParseDistance(new JValue(s), out var mm, out var expr, out _);
        Assert.True(ok);
        Assert.Null(mm);
        Assert.Equal(s, expr);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void TryParseDistance_NonPositiveNumber_Rejected(string s)
    {
        Assert.False(ExtrudeParams.TryParseDistance(new JValue(double.Parse(s)), out _, out _, out var err));
        Assert.Contains("greater than 0", err);
    }

    [Fact]
    public void TryParseDistance_NonFinite_Rejected()
    {
        // NaN/Infinity must not slip through: NaN <= 0 is false, so the d<=0 check alone is a hole.
        Assert.False(ExtrudeParams.TryParseDistance(new JValue(double.NaN), out _, out _, out _));
        Assert.False(ExtrudeParams.TryParseDistance(new JValue(double.PositiveInfinity), out _, out _, out _));
        Assert.False(ExtrudeParams.TryParseDistance(new JValue("NaN"), out _, out _, out _));
        Assert.False(ExtrudeParams.TryParseDistance(new JValue("1e400"), out _, out _, out _));
    }

    [Fact]
    public void TryParseDistance_NonPositiveNumericString_Rejected()
    {
        Assert.False(ExtrudeParams.TryParseDistance(new JValue("-5"), out _, out _, out var err));
        Assert.Contains("greater than 0", err);
    }

    [Fact]
    public void TryParseDistance_MissingOrEmpty_Rejected()
    {
        Assert.False(ExtrudeParams.TryParseDistance(null, out _, out _, out var err1));
        Assert.Contains("required", err1);
        Assert.False(ExtrudeParams.TryParseDistance(new JValue(""), out _, out _, out var err2));
        Assert.Contains("empty", err2);
        Assert.False(ExtrudeParams.TryParseDistance(JValue.CreateNull(), out _, out _, out _));
    }

    [Fact]
    public void TryParseDistance_WrongTokenType_Rejected()
    {
        Assert.False(ExtrudeParams.TryParseDistance(new JArray(1, 2), out _, out _, out var err));
        Assert.Contains("number (mm) or an expression", err);
        Assert.False(ExtrudeParams.TryParseDistance(new JValue(true), out _, out _, out _));
        Assert.False(ExtrudeParams.TryParseDistance(new JObject(), out _, out _, out _));
    }

    [Fact]
    public void TryParseDistance_WhitespaceStrings()
    {
        var ok = ExtrudeParams.TryParseDistance(new JValue("  40  "), out var mm, out var expr, out _);
        Assert.True(ok);
        Assert.Equal(40.0, mm);
        Assert.Null(expr);
        Assert.False(ExtrudeParams.TryParseDistance(new JValue("   "), out _, out _, out var err));
        Assert.Contains("empty", err);
    }

    [Fact]
    public void TryRejectIncompatible_NewBodyWithAffectedBodies_Rejected()
    {
        var rejected = ExtrudeParams.TryRejectIncompatible("new_body",
            new JArray("1", "body:2"), out var err);
        Assert.True(rejected);
        Assert.Contains("affected_bodies", err);
        Assert.Contains("new_body", err);
    }

    [Theory]
    [InlineData("join")]
    [InlineData("cut")]
    [InlineData("intersect")]
    public void TryRejectIncompatible_ScopedOpsWithAffectedBodies_Allowed(string op)
    {
        Assert.False(ExtrudeParams.TryRejectIncompatible(op, new JArray("1"), out _));
    }

    [Fact]
    public void TryRejectIncompatible_NewBodyWithoutAffectedBodies_Allowed()
    {
        Assert.False(ExtrudeParams.TryRejectIncompatible("new_body", null, out _));
        Assert.False(ExtrudeParams.TryRejectIncompatible("new_body", new JArray(), out _));
    }
}
