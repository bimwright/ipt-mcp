using System.IO;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Transport;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// <see cref="TransportFactory.Create"/> picks the transport by Inventor year: TCP for 2022-2024,
/// Named Pipe for 2025-2027, and stamps the matching <c>transport</c>/<c>port</c>/<c>pipe_name</c> on
/// the descriptor. API-free, so it runs in the net8 test project without Inventor.
/// </summary>
public sealed class TransportSelectionTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "inv-tf-" + System.Guid.NewGuid().ToString("N"));

    [Fact]
    public void Year2024_uses_tcp()
    {
        var server = TransportFactory.Create(2024, TempDir(), out var descriptor);
        try
        {
            Assert.IsType<TcpTransportServer>(server);
            Assert.Equal("tcp", descriptor.Transport);
            Assert.Equal("Inventor", descriptor.HostApp);
            Assert.Equal(2024, descriptor.InventorYear);
            Assert.StartsWith("inventor-2024-", descriptor.TargetId);
            Assert.False(string.IsNullOrEmpty(descriptor.AuthToken));

            // TCP binds an OS-assigned port at Start; verify a real port is read back.
            server.Start((_, tcs) => tcs.TrySetResult("{}"));
            Assert.True(((TcpTransportServer)server).Port > 0);
        }
        finally
        {
            server.Dispose();
        }
    }

    [Fact]
    public void Year2025_uses_pipe()
    {
        var server = TransportFactory.Create(2025, TempDir(), out var descriptor);
        try
        {
            Assert.IsType<PipeTransportServer>(server);
            Assert.Equal("pipe", descriptor.Transport);
            Assert.Equal("Inventor", descriptor.HostApp);
            Assert.False(string.IsNullOrEmpty(descriptor.PipeName));
            Assert.StartsWith("BimwrightInventor-", descriptor.PipeName);
        }
        finally
        {
            server.Dispose();
        }
    }

    [Fact]
    public void Year2027_uses_pipe()
    {
        var server = TransportFactory.Create(2027, TempDir(), out var descriptor);
        try
        {
            Assert.IsType<PipeTransportServer>(server);
            Assert.Equal("pipe", descriptor.Transport);
            Assert.Equal("Inventor", descriptor.HostApp);
            Assert.Equal(2027, descriptor.InventorYear);
            Assert.False(string.IsNullOrEmpty(descriptor.PipeName));
        }
        finally
        {
            server.Dispose();
        }
    }

    [Fact]
    public void CreateStarted_tcp_populates_port_on_descriptor()
    {
        var server = TransportFactory.CreateStarted(2022, TempDir(), (_, tcs) => tcs.TrySetResult("{}"), out var descriptor);
        try
        {
            Assert.IsType<TcpTransportServer>(server);
            Assert.Equal("tcp", descriptor.Transport);
            Assert.True(descriptor.Port > 0);
            Assert.Equal(((TcpTransportServer)server).Port, descriptor.Port);
        }
        finally
        {
            server.Dispose();
        }
    }
}
