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
