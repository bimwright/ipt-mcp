using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastThumbnailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-toast-thumb-" + Guid.NewGuid().ToString("N"));
    public ToastThumbnailTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.JPG")]
    [InlineData("a.jpeg")]
    [InlineData("a.bmp")]
    public void Existing_images_are_accepted(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, new byte[] { 1 });
        Assert.Equal(Path.GetFullPath(p), ToastThumbnail.PathIfImage(p));
    }

    [Fact]
    public void Non_image_extension_is_rejected()
    {
        var p = Path.Combine(_dir, "a.step");
        File.WriteAllBytes(p, new byte[] { 1 });
        Assert.Null(ToastThumbnail.PathIfImage(p));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\server\share\capture.png")]   // never touch the network from the toast thread
    [InlineData("bad\0name.png")]
    public void Unusable_paths_are_rejected(string? path)
        => Assert.Null(ToastThumbnail.PathIfImage(path));

    [Fact]
    public void Missing_file_is_rejected()
        => Assert.Null(ToastThumbnail.PathIfImage(Path.Combine(_dir, "none.png")));

    [Fact]
    public void Load_respects_the_size_cap()
    {
        var p = Path.Combine(_dir, "big.png");
        File.WriteAllBytes(p, new byte[100]);
        Assert.Null(ToastThumbnail.TryLoadBytes(p, maxBytes: 99));
        Assert.Equal(100, ToastThumbnail.TryLoadBytes(p, maxBytes: 100)!.Length);
    }

    [Fact]
    public void Load_of_missing_file_is_null()
        => Assert.Null(ToastThumbnail.TryLoadBytes(Path.Combine(_dir, "none.png")));
}
