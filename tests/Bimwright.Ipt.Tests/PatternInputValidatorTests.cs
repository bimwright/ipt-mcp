using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

public sealed class PatternInputValidatorTests
{
    [Theory]
    [InlineData(1, 10.0, null, null, null, "count1")]
    [InlineData(2, 0.0, null, null, null, "spacing_mm1")]
    [InlineData(2, 10.0, "Y Axis", 1, 20.0, "count2")]
    [InlineData(2, 10.0, "Y Axis", 3, 0.0, "spacing_mm2")]
    public void Rectangular_pattern_rejects_invalid_counts_and_spacing(
        int count1,
        double spacingMm1,
        string? dir2,
        int? count2,
        double? spacingMm2,
        string expectedField)
    {
        Assert.False(PatternInputValidator.TryValidateRectangular(
            count1, spacingMm1, dir2, count2, spacingMm2, out var error));
        Assert.Contains(expectedField, error);
    }

    [Theory]
    [InlineData(2, 10.0, null, null, null)]
    [InlineData(2, 10.0, "Y Axis", 3, 20.0)]
    public void Rectangular_pattern_accepts_valid_one_and_two_direction_inputs(
        int count1,
        double spacingMm1,
        string? dir2,
        int? count2,
        double? spacingMm2)
    {
        Assert.True(PatternInputValidator.TryValidateRectangular(
            count1, spacingMm1, dir2, count2, spacingMm2, out var error), error);
    }
}
