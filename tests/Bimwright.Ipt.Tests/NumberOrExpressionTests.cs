using Bimwright.Ipt.Shared.Handlers;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// <see cref="NumberOrExpression"/> — the shared number-or-Inventor-expression parse behind
/// sketch dimension value and revolve angle. Sign/range rules belong to callers.
/// </summary>
public sealed class NumberOrExpressionTests
{
    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("+1.0e9999")]
    public void Overflowing_numeric_literals_never_become_expressions(string value)
    {
        Assert.False(Parse(new JValue(value), out var number, out var expression, out var error));
        Assert.Null(number);
        Assert.Null(expression);
        Assert.Contains("finite", error);
    }

    private static bool Parse(JToken? token, out double? number, out string? expression, out string error)
        => NumberOrExpression.TryParse(token, "value_mm", "mm", out number, out expression, out error);

    [Fact]
    public void Number_returned_as_number()
    {
        Assert.True(Parse(new JValue(12.5), out var n, out var e, out var err));
        Assert.Equal(12.5, n);
        Assert.Null(e);
        Assert.Equal("", err);
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("  7 ", 7.0)]
    [InlineData("1e1", 10.0)]
    public void Numeric_string_returned_as_number_using_invariant_culture(string s, double expected)
    {
        Assert.True(Parse(new JValue(s), out var n, out var e, out _));
        Assert.Equal(expected, n);
        Assert.Null(e);
    }

    [Theory]
    [InlineData("plate_w / 2", "plate_w / 2")]
    [InlineData("  Shaft_D + Fit_Clr ", "Shaft_D + Fit_Clr")]
    [InlineData("40 mm", "40 mm")]
    public void Other_strings_returned_trimmed_as_expression(string s, string expected)
    {
        Assert.True(Parse(new JValue(s), out var n, out var e, out _));
        Assert.Null(n);
        Assert.Equal(expected, e);
    }

    [Fact]
    public void Sign_is_not_judged_here()
    {
        // Revolve treats 0 as "full" and negative as "reverse", so range rules are the caller's.
        Assert.True(Parse(new JValue(0.0), out var zero, out _, out _));
        Assert.Equal(0.0, zero);
        Assert.True(Parse(new JValue(-5), out var neg, out _, out _));
        Assert.Equal(-5.0, neg);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_string_rejected(string s)
    {
        Assert.False(Parse(new JValue(s), out _, out _, out var err));
        Assert.Equal("value_mm must not be empty", err);
    }

    [Fact]
    public void Non_finite_rejected_on_both_paths()
    {
        Assert.False(Parse(new JValue(double.NaN), out _, out _, out var err));
        Assert.Equal("value_mm must be a finite number", err);
        Assert.False(Parse(new JValue(double.PositiveInfinity), out _, out _, out _));
        Assert.False(Parse(new JValue("NaN"), out _, out _, out _));
        Assert.False(Parse(new JValue("1e400"), out _, out _, out _));
    }

    [Fact]
    public void Missing_rejected()
    {
        Assert.False(Parse(null, out _, out _, out var err));
        Assert.Equal("value_mm is required", err);
        Assert.False(Parse(JValue.CreateNull(), out _, out _, out _));
    }

    [Fact]
    public void Wrong_json_type_rejected_with_unit_in_message()
    {
        Assert.False(Parse(new JValue(true), out _, out _, out var err));
        Assert.Equal("value_mm must be a number (mm) or an expression string", err);
        Assert.False(Parse(new JArray(1, 2), out _, out _, out _));
    }
}
