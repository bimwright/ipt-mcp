using Bimwright.Ipt.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Behavior of <see cref="ToolsetFilter.Resolve"/> per the Frozen Integration Contracts:
///   DefaultOn = all except `code`;
///   WriteCapable (removed by --read-only) = document, parameters, properties, sketch,
///     feature, export, code, toolbaker_write;
///   `code` requires EnableSendCode; `toolbaker*` requires EnableToolBaker;
///   "all" expands to every known toolset; unknown names are silently dropped.
/// </summary>
public sealed class ToolsetFilterTests
{
    [Fact]
    public void DefaultSurfaceIncludesEverythingExceptCode()
    {
        var set = ToolsetFilter.Resolve(new InventorMcpConfig());
        foreach (var t in new[]
                 {
                     "meta", "query", "document", "parameters", "properties",
                     "sketch", "feature", "export", "toolbaker", "toolbaker_write",
                     "assembly", "assembly_query"
                 })
            Assert.Contains(t, set);
        Assert.DoesNotContain("code", set);
    }

    [Fact]
    public void AllPlusSendCodeIncludesCode()
    {
        var set = ToolsetFilter.Resolve(new InventorMcpConfig { Toolsets = { "all" }, EnableSendCode = true });
        Assert.Contains("code", set);
    }

    [Fact]
    public void AllWithoutSendCodeStillExcludesCode()
    {
        var set = ToolsetFilter.Resolve(new InventorMcpConfig { Toolsets = { "all" } });
        Assert.DoesNotContain("code", set);
    }

    [Fact]
    public void UnknownToolsetNameIsSilentlyDropped()
    {
        var set = ToolsetFilter.Resolve(new InventorMcpConfig { Toolsets = { "sketch", "bogus_toolset" } });
        Assert.Contains("sketch", set);
        Assert.DoesNotContain("bogus_toolset", set);
        Assert.Single(set);   // only the recognized one survives
    }

    [Fact]
    public void ReadOnlyRemovesWriteCapableToolsetsButKeepsReadOnlyOnes()
    {
        var set = ToolsetFilter.Resolve(new InventorMcpConfig
        {
            Toolsets = { "all" },
            ReadOnly = true,
            EnableSendCode = true,
        });

        foreach (var keep in new[] { "meta", "query", "assembly_query", "toolbaker" })
            Assert.Contains(keep, set);

        foreach (var gone in new[]
                 {
                     "document", "parameters", "properties", "sketch",
                     "feature", "export", "code", "toolbaker_write", "assembly"
                 })
            Assert.DoesNotContain(gone, set);
    }

    [Fact]
    public void DisableToolBakerRemovesBothBakerToolsets()
    {
        var set = ToolsetFilter.Resolve(new InventorMcpConfig { Toolsets = { "all" }, EnableToolBaker = false });
        Assert.DoesNotContain("toolbaker", set);
        Assert.DoesNotContain("toolbaker_write", set);
    }

    [Fact]
    public void ExplicitSingleToolsetDoesNotAutoAddMeta()
    {
        var set = ToolsetFilter.Resolve(new InventorMcpConfig { Toolsets = { "sketch" } });
        Assert.Contains("sketch", set);
        Assert.DoesNotContain("meta", set);
        Assert.Single(set);
    }

    [Fact]
    public void Assembly_is_writecapable_assembly_query_is_not()
    {
        var ro = new InventorMcpConfig { Toolsets = { "all" }, ReadOnly = true };
        var set = ToolsetFilter.Resolve(ro);
        Assert.DoesNotContain("assembly", set);
        Assert.Contains("assembly_query", set);
    }

    [Fact]
    public void Assembly_toolsets_are_default_on()
    {
        var def = ToolsetFilter.Resolve(new InventorMcpConfig());
        Assert.Contains("assembly", def);
        Assert.Contains("assembly_query", def);
    }
}
