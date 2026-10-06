using Bimwright.Ipt.Shared.Handlers;

namespace Bimwright.Ipt.Tests;

/// <summary><see cref="EnumText"/> — Inventor enum-name cleanup for the list_* DTOs (F4-P0-3).</summary>
public sealed class EnumTextTests
{
    [Theory]
    [InlineData("kExtrudeFeatureObject", "Object", "ExtrudeFeature")]
    [InlineData("kFilletFeatureObject", "Object", "FilletFeature")]
    [InlineData("kPartFeatureObject", "Object", "PartFeature")]
    [InlineData("kUpToDateHealth", "Health", "UpToDate")]
    [InlineData("kInErrorHealth", "Health", "InError")]
    [InlineData("kCannotComputeHealth", "Health", "CannotCompute")]
    public void Friendly_StripsPrefixAndSuffix(string raw, string suffix, string expected)
        => Assert.Equal(expected, EnumText.Friendly(raw, suffix));

    [Theory]
    [InlineData("kSolidObject", "Object", "Solid")]          // strips both
    [InlineData("kBox", "Object", "Box")]                    // no suffix present
    [InlineData("Extrude", "Object", "Extrude")]             // no k prefix, no suffix
    [InlineData("k", "Object", "k")]                         // bare "k" survives (length guard)
    [InlineData("kObject", "Object", "Object")]              // k stripped; suffix kept (length guard)
    [InlineData("", "Object", "")]                           // empty
    public void Friendly_EdgeCases_DoNotOverstrip(string raw, string suffix, string expected)
        => Assert.Equal(expected, EnumText.Friendly(raw, suffix));

    [Fact]
    public void Friendly_Null_ReturnsEmpty()
        => Assert.Equal("", EnumText.Friendly(null!));
}
