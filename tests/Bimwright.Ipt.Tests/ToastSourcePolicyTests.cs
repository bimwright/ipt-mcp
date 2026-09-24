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
        var window = ReadToast("ToastWindow.cs");
        Assert.Contains("ShowActivated = false", window);
        Assert.Contains("ToastNative.MakeNoActivate(", window);
        Assert.DoesNotContain(".Owner =", window);
        Assert.Contains("WS_EX_NOACTIVATE", ReadToast("ToastNative.cs"));
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
        var server = text.IndexOf("_server?.Dispose()", start, StringComparison.Ordinal);
        var toasts = text.IndexOf("_toasts?.Dispose()", start, StringComparison.Ordinal);
        var ribbon = text.IndexOf("_ribbon?.Remove()", start, StringComparison.Ordinal);
        Assert.True(server >= 0 && toasts > server && ribbon > toasts);
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
