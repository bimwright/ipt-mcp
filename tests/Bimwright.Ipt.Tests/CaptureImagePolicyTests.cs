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
}
