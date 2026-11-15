using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using ModelContextProtocol.Server;
using Bimwright.Ipt.Shared.Contracts;
using Xunit;

namespace Bimwright.Ipt.Tests;

public class ClosedConnectionTests
{
    private static readonly TargetDescriptor Target = new() { TargetId = "inventor-2027-4242", ProcessId = 4242 };

    [Fact]
    public async Task ExitedProcessIsHostCrashedAndNamesTheTarget()
    {
        var ex = await PluginClient.ClassifyClosedConnectionAsync(Target, _ => new(false, null), waitMs: 0);

        Assert.Equal(InventorErrorCodes.HOST_CRASHED, ex.Code);
        Assert.Contains("inventor-2027-4242", ex.Message);
        Assert.Contains("Do not resend", ex.Message);
    }

    [Fact]
    public async Task ProcessThatExitsWhileWaitingIsHostCrashed()
    {
        var probes = 0;
        var ex = await PluginClient.ClassifyClosedConnectionAsync(Target, _ => new(++probes < 3, null), waitMs: 5000);

        Assert.Equal(InventorErrorCodes.HOST_CRASHED, ex.Code);
        Assert.Equal(3, probes);
    }

    [Fact]
    public void SendCodeDescriptionWarnsAboutKnownCrashingCalls()
    {
        var description = typeof(CodeTools).GetMethods()
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name == "inventor_send_code")
            .Select(m => m.GetCustomAttribute<DescriptionAttribute>()!.Description).Single();

        Assert.Contains("SecondFormattedText on a detail view", description);
        Assert.Contains("GetMinimumDistance on TransientBRep", description);
        Assert.Contains("HOST_CRASHED", description);
    }

    [Fact]
    public async Task RunningProcessStaysTargetUnavailableAndNamesTheTarget()
    {
        var ex = await PluginClient.ClassifyClosedConnectionAsync(Target, _ => new(true, DateTime.UtcNow), waitMs: 0);

        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE, ex.Code);
        Assert.Contains("inventor-2027-4242", ex.Message);
    }
}
