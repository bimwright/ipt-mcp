using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Export;

namespace Bimwright.Ipt.Tests;

public class CaptureImagePolicyTests
{
    [Theory]
    [InlineData(@"C:\Users\me\pic.png")]
    [InlineData(@"C:\Users\me\pic.PNG")]
    [InlineData(@"C:\Users\me\pic.jpg")]
    [InlineData(@"C:\Users\me\pic.jpeg")]
    [InlineData(@"C:\Users\me\pic.bmp")]
    public void TryRejectImageExtension_AcceptsSupportedExtensions(string path)
    {
        var rejected = CaptureImagePolicy.TryRejectImageExtension(path, out var rejection);
        Assert.False(rejected);
        Assert.Equal("", rejection);
    }

    [Theory]
    [InlineData(@"C:\Users\me\pic.gif")]
    [InlineData(@"C:\Users\me\pic.txt")]
    [InlineData(@"C:\Users\me\pic")]
    public void TryRejectImageExtension_RejectsUnsupportedOrMissingExtension(string path)
    {
        var rejected = CaptureImagePolicy.TryRejectImageExtension(path, out var rejection);
        Assert.True(rejected);
        Assert.Contains(".png", rejection);
    }

    [Fact]
    public void TryRejectImageExtension_RejectsNullOrEmpty()
    {
        Assert.True(CaptureImagePolicy.TryRejectImageExtension(null, out _));
        Assert.True(CaptureImagePolicy.TryRejectImageExtension("   ", out _));
    }

    [Theory]
    [InlineData(@"C:\x\a.png", "png")]
    [InlineData(@"C:\x\a.PNG", "png")]
    [InlineData(@"C:\x\a.jpg", "jpg")]
    [InlineData(@"C:\x\a.jpeg", "jpeg")]
    [InlineData(@"C:\x\a.bmp", "bmp")]
    public void ResolveFormat_ReturnsLowercasedExtensionWithoutDot(string path, string expected)
    {
        Assert.Equal(expected, CaptureImagePolicy.ResolveFormat(path));
    }

    // ---- F3-c: file-mode default --------------------------------------------------------

    [Fact]
    public void DefaultCapturePath_UnderCapturesDir_WithTimestampAndSequence()
    {
        var path = CaptureImagePolicy.DefaultCapturePath(
            @"C:\root", new DateTime(2026, 9, 15, 3, 4, 5, DateTimeKind.Utc), 7);

        Assert.Equal(@"C:\root\captures\capture-20260915-030405-007.png", path);
    }

    [Fact]
    public void ResolveCaptureRoot_UsesEnvRootWhenSet()
    {
        var prev = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", @"D:\exports");
            Assert.Equal(@"D:\exports", CaptureImagePolicy.ResolveCaptureRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", prev);
        }
    }

    [Fact]
    public void ResolveCaptureRoot_FallsBackToLocalAppData()
    {
        var prev = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", null);
            var root = CaptureImagePolicy.ResolveCaptureRoot();
            Assert.EndsWith(@"Bimwright\ipt-mcp", root);
            Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", prev);
        }
    }

    [Fact]
    public void ResolveCaptureRoot_RelativeEnvFallsBackToLocalAppData()
    {
        // A non-rooted env root would resolve against Inventor.exe's unpredictable CWD — refuse it.
        var prev = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", "exports");
            Assert.EndsWith(@"Bimwright\ipt-mcp", CaptureImagePolicy.ResolveCaptureRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", prev);
        }
    }

    [Fact]
    public void DefaultCapturePath_SatisfiesExportPathPolicy()
    {
        // Locks the "allowed root by construction" claim: a generated path under the resolved
        // root must pass the same policy explicit output_path goes through.
        var path = CaptureImagePolicy.DefaultCapturePath(
            CaptureImagePolicy.ResolveCaptureRoot(), DateTime.UtcNow, 1);
        Assert.False(ExportPathPolicy.TryRejectPath(path, out var rejection), rejection);
    }

    [Fact]
    public void TryRejectInline_AtOrUnderBudget_Passes()
    {
        Assert.False(CaptureImagePolicy.TryRejectInline(CaptureImagePolicy.MaxInlineBase64Bytes, out var r));
        Assert.Equal("", r);
        Assert.False(CaptureImagePolicy.TryRejectInline(1, out _));
    }

    [Fact]
    public void TryRejectInline_OverBudget_RejectsWithFileModeHint()
    {
        var rejected = CaptureImagePolicy.TryRejectInline(CaptureImagePolicy.MaxInlineBase64Bytes + 1, out var r);
        Assert.True(rejected);
        Assert.Contains("file mode", r);
        Assert.Contains("width/height", r);
    }
}
