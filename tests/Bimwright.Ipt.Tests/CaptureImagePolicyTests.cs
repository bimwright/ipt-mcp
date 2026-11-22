using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Export;

namespace Bimwright.Ipt.Tests;

// Mutates BIMWRIGHT_INVENTOR_EXPORT_ROOT, which ExportPathPolicy also reads.
[Collection("Process-wide privacy state")]
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

    [Theory]
    [InlineData("exports")]      // plain relative — would resolve against Inventor's CWD
    [InlineData("C:exports")]    // drive-relative — IsPathRooted accepts it, still CWD-dependent
    [InlineData("\\exports")]    // root-relative — lands on the current drive's root
    public void ResolveCaptureRoot_NonAbsoluteEnvFallsBackToLocalAppData(string envValue)
    {
        var prev = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", envValue);
            Assert.EndsWith(@"Bimwright\ipt-mcp", CaptureImagePolicy.ResolveCaptureRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIMWRIGHT_INVENTOR_EXPORT_ROOT", prev);
        }
    }

    [Fact]
    public void TryReserveCapturePath_SkipsNamesAlreadyReservedByAPeer()
    {
        // Atomic reservation (FileMode.CreateNew): a second caller with the same timestamp +
        // start sequence must get the next free name — two Inventor instances share the dir.
        var root = Path.Combine(Path.GetTempPath(), "ipt-cap-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stamp = new DateTime(2026, 9, 16, 1, 2, 3, DateTimeKind.Utc);

            var first = CaptureImagePolicy.TryReserveCapturePath(root, stamp, 1);
            Assert.Equal(Path.Combine(root, "captures", "capture-20260916-010203-001.png"), first);
            Assert.True(File.Exists(first));   // placeholder created = name is ours

            var second = CaptureImagePolicy.TryReserveCapturePath(root, stamp, 1);
            Assert.Equal(Path.Combine(root, "captures", "capture-20260916-010203-002.png"), second);
            Assert.NotEqual(first, second);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
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

    [Fact]
    public void PruneGenerated_drops_old_then_oldest_generated_captures_and_nothing_else()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-capture-prune-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "captures");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        var now = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
        string Make(string name, int ageDays, int bytes)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, new byte[bytes]);
            File.SetLastWriteTimeUtc(path, now.AddDays(-ageDays));
            return path;
        }
        try
        {
            var expired = Make("capture-20261001-101010-001.png", 40, 10);
            var oldest = Make("capture-20261101-101010-002.png", 20, 400);
            var middle = Make("capture-20261110-101010-1003.png", 11, 400);
            var newest = Make("capture-20261121-101010-004.png", 1, 400);
            var named = Make("before-fix.png", 90, 400);
            var lookalike = Make("capture-notes.png", 90, 400);
            var otherType = Make("capture-20261001-101010-005.jpg", 90, 400);
            var nested = Make(Path.Combine("sub", "capture-20261001-101010-006.png"), 90, 400);

            // Budget 900: the expired file goes by age, then the oldest until the rest fits.
            Assert.Equal(2, CaptureImagePolicy.PruneGenerated(root, newest, now, days: 30, budget: 900));
            Assert.False(File.Exists(expired));
            Assert.False(File.Exists(oldest));
            foreach (var kept in new[] { middle, newest, named, lookalike, otherType, nested }) Assert.True(File.Exists(kept), kept);

            // The capture just written survives even when it alone exceeds the budget.
            Assert.Equal(1, CaptureImagePolicy.PruneGenerated(root, newest, now, days: 30, budget: 100));
            Assert.True(File.Exists(newest));
            Assert.False(File.Exists(middle));

            Assert.Equal(0, CaptureImagePolicy.PruneGenerated(Path.Combine(root, "missing"), null, now));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PruneGenerated_keeps_every_capture_when_keep_all_is_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipt-capture-keepall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "captures"));
        var old = Path.Combine(root, "captures", "capture-20200101-101010-001.png");
        File.WriteAllBytes(old, new byte[400]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-400));
        try
        {
            Bimwright.Ipt.Shared.Logging.LogRetentionPolicy.ReadValue = () => "1";
            Assert.Equal(0, CaptureImagePolicy.PruneGenerated(root, null, DateTime.UtcNow, days: 30, budget: 1));
            Assert.True(File.Exists(old));
        }
        finally
        {
            Bimwright.Ipt.Shared.Logging.LogRetentionPolicy.ReadValue = () => null;
            Directory.Delete(root, true);
        }
    }
}
