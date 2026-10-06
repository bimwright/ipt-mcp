using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>Static pins for the Phase 1a platform decisions the toast code must keep (API-free).</summary>
public sealed class ToastSourcePolicyTests
{
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) &&
                Directory.Exists(Path.Combine(d.FullName, "tests")) &&
                File.Exists(Path.Combine(d.FullName, "README.md")))
                return d.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    private static string ToastDir => Path.Combine(RepoRoot(), "src", "shared", "Views", "Toast");
    private static string ReadToast(string name) => File.ReadAllText(Path.Combine(ToastDir, name));
    private static string ReadBase() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "shared", "Plugin", "InventorAddInServerBase.cs"));

    [Theory]
    [InlineData(2022)]
    [InlineData(2023)]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void Every_plugin_shell_enables_WPF(int year)
    {
        var csproj = Path.Combine(RepoRoot(), "src", $"plugin-inv{year - 2000}", $"Bimwright.Ipt.Plugin.Inv{year - 2000}.csproj");
        Assert.Equal("true", XDocument.Load(csproj).Descendants("UseWPF").FirstOrDefault()?.Value.Trim());
    }

    [Fact]
    public void Src_has_no_rvt_mcp_dependency()
    {
        var sep = Path.DirectorySeparatorChar;
        var hits = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains(sep + "obj" + sep) && !p.Contains(sep + "bin" + sep))
            .Where(p => File.ReadAllText(p).Contains("RvtMcp"))
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Toast_ui_is_code_only()
        => Assert.Empty(Directory.EnumerateFiles(ToastDir, "*.xaml", SearchOption.AllDirectories));

    [Fact]
    public void Toast_window_never_takes_activation_and_is_never_owned()
    {
        // The card is rvt-mcp's McpToastWindow, which blocks activation itself. Owning it by an Inventor HWND
        // would tie the toast thread's input queue to Inventor's, so neither file may set an owner.
        var window = ReadToast("McpToastWindow.cs");
        Assert.Contains("ShowActivated = false", window);
        Assert.Contains("WS_EX_NOACTIVATE", window);
        Assert.DoesNotContain(".Owner =", window);
        Assert.DoesNotContain(".Owner =", ReadToast("McpToastManager.cs"));
        Assert.DoesNotContain("WindowInteropHelper", ReadToast("McpToastManager.cs"));
    }

    [Fact]
    public void Toast_has_no_theme_or_backdrop_sampling()
    {
        Assert.False(File.Exists(Path.Combine(ToastDir, "ScreenSampler.cs")));
        Assert.False(File.Exists(Path.Combine(ToastDir, "Model", "ToastPalette.cs")));
        foreach (var file in Directory.EnumerateFiles(ToastDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(@"ToastTheme", text);
            Assert.DoesNotContain("BackdropHint", text);
            Assert.DoesNotContain("CopyFromScreen", text);
        }
    }

    [Fact]
    public void Toast_thread_swallows_its_own_exceptions()
    {
        // An unhandled exception on the toast dispatcher would terminate Inventor.exe.
        var host = ReadToast("ToastHost.cs");
        Assert.Contains("UnhandledException += (_, e) => e.Handled = true", host);
        Assert.Contains("SetApartmentState(ApartmentState.STA)", host);
        Assert.Contains("IsBackground = true", host);
    }

    [Fact]
    public void Toast_is_notified_only_after_the_response_is_handed_back()
    {
        var text = ReadBase();
        var set = text.IndexOf("tcs.TrySetResult(JsonConvert.SerializeObject(result));", StringComparison.Ordinal);
        var notify = text.IndexOf("RecordOutcome(env, dispatcher, result.Ok", StringComparison.Ordinal);
        Assert.True(set >= 0, "success path must serialize the result it hands back");
        Assert.True(notify > set, "the outcome record (toast + history) must happen after tcs.TrySetResult");
    }

    [Fact]
    public void Deactivate_stops_transport_then_toasts_then_ribbon()
    {
        var text = ReadBase();
        var start = text.IndexOf("public void Deactivate()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var snapshotTimer = text.IndexOf("StopToastSnapshotTimer()", start, StringComparison.Ordinal);
        var server = text.IndexOf("_server?.Dispose()", start, StringComparison.Ordinal);
        var toasts = text.IndexOf("_toasts?.Dispose()", start, StringComparison.Ordinal);
        var ribbon = text.IndexOf("_ribbon?.Remove()", start, StringComparison.Ordinal);
        Assert.True(server >= 0 && toasts > server && ribbon > toasts);
        Assert.True(snapshotTimer >= 0 && snapshotTimer < server);
    }

    [Fact]
    public void Startup_snapshot_retries_back_off_on_the_Inventor_STA_until_visible_or_disabled()
    {
        var text = ReadBase();
        var start = text.IndexOf("private void RefreshToastWhenReady()", StringComparison.Ordinal);
        var end = text.IndexOf("private void StopToastSnapshotTimer()", start, StringComparison.Ordinal);
        var retry = text.Substring(start, end - start);
        Assert.Contains("new System.Windows.Forms.Timer { Interval = 250 }", retry);
        Assert.Matches(@"Tick \+= .*\s*\{\s*RefreshToastSnapshot\(\);", retry);
        Assert.Matches(@"if \(_toasts == null \|\| !_toasts.Enabled \|\| _toasts.HasVisibleSnapshot\)\s*\{\s*StopToastSnapshotTimer\(\);\s*return;\s*\}", retry);
        Assert.Matches(@"if \(attempts < 20 && \+\+attempts == 20\)\s*_toastSnapshotTimer!\.Interval = 2000;", retry);
        Assert.Equal(1, retry.Split(new[] { "StopToastSnapshotTimer();" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("else StopToastSnapshotTimer();", text); // ribbon disable
        Assert.Contains("_toastSnapshotTimer?.Stop();", text);
        Assert.Contains("_toastSnapshotTimer?.Dispose();", text);
        Assert.Contains("_toastSnapshotTimer = null;", text);
    }

    [Fact]
    public void Ribbon_icons_use_OleCreatePictureIndirect_not_AxHost()
    {
        var ribbon = File.ReadAllText(Path.Combine(RepoRoot(), "src", "shared", "Plugin", "BimwrightRibbon.cs"));
        Assert.Contains("OleCreatePictureIndirect", ribbon);
        Assert.DoesNotContain("AxHost", ribbon);
        Assert.Contains("OnResetRibbonInterface", ribbon);
    }

    [Fact]
    public void Toast_code_never_touches_Inventor_or_messages_its_windows()
    {
        foreach (var file in Directory.EnumerateFiles(ToastDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("global::Inventor", text);
            Assert.DoesNotContain("InvApi", text);
            Assert.DoesNotContain("SendMessage", text);
            Assert.DoesNotContain("PostMessage", text);
        }
    }
}
