using Bimwright.Ipt.Shared.ToolBaker;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class BakedToolAuthorizerAssemblyTests
{
    [Theory]
    [InlineData("list_interfaces")]
    [InlineData("check_interference")]
    [InlineData("measure_min_distance")]
    [InlineData("get_assembly_bom")]
    [InlineData("list_constraints")]
    public void New_readonly_assembly_commands_are_bakeable(string cmd)
        => Assert.True(BakedToolDispatchAuthorizer.IsAllowed(cmd));

    [Theory]
    [InlineData("place_occurrence")]
    [InlineData("add_constraint")]
    [InlineData("create_imate")]
    [InlineData("hole")]
    [InlineData("circular_pattern")]
    [InlineData("rectangular_pattern")]
    public void Write_commands_stay_denied(string cmd)
        => Assert.False(BakedToolDispatchAuthorizer.IsAllowed(cmd));
}
