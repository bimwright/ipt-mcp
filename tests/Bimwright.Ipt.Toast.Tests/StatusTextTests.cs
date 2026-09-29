using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class StatusTextTests
{
    private static StatusInfo Info(
        string transport = "pipe", ToastSettings? toast = null, bool on = true, string? decision = null)
        => new("inventor-2027-4242", 2027, transport, "BimwrightInventor-4242", 50123,
            SendCodeEnabled: false, ReadOnly: true,
            toast ?? new ToastSettings(true, ToastTheme.Auto, "default", "default"),
            on, decision, @"C:\Users\u\AppData\Local\Bimwright\ipt-mcp\iptmcp.config.json");

    [Fact]
    public void Pipe_target_and_gates()
    {
        var t = StatusText.Build(Info());
        Assert.Contains("Target: inventor-2027-4242 (Inventor 2027)", t);
        Assert.Contains("Transport: Named Pipe BimwrightInventor-4242", t);
        Assert.Contains("send_code (add-in opt-in): OFF", t);
        Assert.Contains("Read-only lock: ON", t);
    }

    [Fact]
    public void Tcp_transport_shows_port()
        => Assert.Contains("Transport: TCP port 50123 (loopback)", StatusText.Build(Info(transport: "tcp")));

    [Fact]
    public void Toast_state_sources_and_palette()
    {
        var t = StatusText.Build(Info(toast: new ToastSettings(false, ToastTheme.Dark, "config", "config"), on: true,
            decision: "screen sample #2A313D → light-elevated (13.1:1) (sample 4 ms)"));
        Assert.Contains("Toasts: ON (startup value from config)", t);
        Assert.Contains("Toast theme: dark (from config)", t);
        Assert.Contains("Last palette: screen sample #2A313D → light-elevated (13.1:1) (sample 4 ms)", t);
        Assert.Contains(@"Config file: C:\Users\u\AppData\Local\Bimwright\ipt-mcp\iptmcp.config.json", t);
    }

    [Fact]
    public void No_toast_yet()
        => Assert.Contains("Last palette: no toast shown yet", StatusText.Build(Info()));

    [Fact]
    public void Env_override_is_explained()
    {
        var t = StatusText.Build(Info(toast: new ToastSettings(true, ToastTheme.Auto, "env " + ToastConfigStore.EnableEnv, "default")));
        Assert.Contains("BIMWRIGHT_INVENTOR_ENABLE_TOAST is set: it wins over the ribbon choice at the next start.", t);
    }

    [Fact]
    public void Privacy_block_is_present()
    {
        var t = StatusText.Build(Info());
        Assert.Contains("Privacy", t);
        Assert.Contains("Nothing is sent anywhere.", t);
        Assert.Contains("Pixels are never saved or logged.", t);
        Assert.Contains("BIMWRIGHT_INVENTOR_CALL_LOG", t);
    }
}
