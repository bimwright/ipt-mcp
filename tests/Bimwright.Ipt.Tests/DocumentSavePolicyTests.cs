using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

/// <summary>save_document enforces the get_document_state policy (rvt-mcp DocumentSavePolicy / SaveAsPathPolicy parity).</summary>
public sealed class DocumentSavePolicyTests
{
    [Theory]
    [InlineData(DocumentStatePolicy.Unsaved, true, true, false, false, "DOCUMENT_NEVER_SAVED")]
    [InlineData(DocumentStatePolicy.Template, true, true, false, false, "TEMPLATE_SAVE_AS_REQUIRED")]
    [InlineData(DocumentStatePolicy.ReadOnly, true, true, false, false, "FILE_READ_ONLY")]
    [InlineData(DocumentStatePolicy.LocalFile, false, true, false, false, "FILE_MISSING_ON_DISK")]
    [InlineData(DocumentStatePolicy.LocalFile, true, false, true, false, DocumentSavePolicy.NoChanges)]
    [InlineData(DocumentStatePolicy.LocalFile, true, true, true, false, "FORMAT_UPGRADE_REQUIRES_APPROVAL")]
    [InlineData(DocumentStatePolicy.LocalFile, true, true, true, true, null)]
    [InlineData(DocumentStatePolicy.LocalFile, true, true, false, false, null)]
    public void In_place_save(string mode, bool exists, bool modified, bool migrating, bool allow, string? expected)
        => Assert.Equal(expected, DocumentSavePolicy.CheckSave(mode, exists, modified, migrating, allow));

    [Fact]
    public void Unknown_mode_is_a_bug()
        => Assert.Throws<ArgumentException>(() => DocumentSavePolicy.CheckSave("cloud", true, true, false, false));

    [Fact]
    public void Extensions_follow_the_document_type()
    {
        Assert.Equal(new[] { ".ipt" }, DocumentSavePolicy.ExtensionsFor("Part"));
        Assert.Equal(new[] { ".iam" }, DocumentSavePolicy.ExtensionsFor("Assembly"));
        Assert.Equal(new[] { ".idw", ".dwg" }, DocumentSavePolicy.ExtensionsFor("Drawing"));
        Assert.Equal(new[] { ".ipn" }, DocumentSavePolicy.ExtensionsFor("Presentation"));
        Assert.Empty(DocumentSavePolicy.ExtensionsFor("Unknown"));
    }

    [Fact]
    public void Save_all_checks_the_root_and_everything_it_would_save()
    {
        var local = (DocumentStatePolicy.LocalFile, false);
        Assert.Null(DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.LocalFile, true, new[] { local, local }, false));
        Assert.Equal("TEMPLATE_SAVE_AS_REQUIRED", DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.Template, true, new[] { local }, false));
        Assert.Equal("FILE_READ_ONLY", DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.ReadOnly, true, new[] { local }, false));
        Assert.Equal("DOCUMENT_NEVER_SAVED", DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.Unsaved, false, new[] { local }, false));
        Assert.Equal("FILE_MISSING_ON_DISK", DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.LocalFile, false, new[] { local }, false));
        // A dirty reference that is a template, or would be converted from an older format.
        Assert.Equal("TEMPLATE_SAVE_AS_REQUIRED",
            DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.LocalFile, true, new[] { local, (DocumentStatePolicy.Template, false) }, true));
        Assert.Equal("FORMAT_UPGRADE_REQUIRES_APPROVAL",
            DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.LocalFile, true, new[] { local, (DocumentStatePolicy.LocalFile, true) }, false));
        Assert.Null(DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.LocalFile, true, new[] { (DocumentStatePolicy.LocalFile, true) }, true));
        // Read-only references are reported, not saved.
        Assert.Null(DocumentSavePolicy.CheckSaveAll(DocumentStatePolicy.LocalFile, true, new[] { (DocumentStatePolicy.ReadOnly, true) }, false));
    }

    private static string? SaveAs(string? target, string[]? ext = null, bool exists = false, bool folder = true, bool network = false)
        => DocumentSavePolicy.CheckSaveAsPath(target, ext ?? new[] { ".ipt" }, _ => exists, _ => folder, _ => network);

    [Theory]
    [InlineData(@"D:\Work\New.ipt", null)]
    [InlineData(@"d:/work/new.IPT", null)]
    [InlineData(@"\\server\share\New.ipt", "SAVE_AS_PATH_NOT_LOCAL")]
    [InlineData(@"//server/share/New.ipt", "SAVE_AS_PATH_NOT_LOCAL")]
    [InlineData(@"New.ipt", "SAVE_AS_PATH_NOT_ABSOLUTE")]
    [InlineData(@"\New.ipt", "SAVE_AS_PATH_NOT_ABSOLUTE")]
    [InlineData(@"D:New.ipt", "SAVE_AS_PATH_NOT_ABSOLUTE")]
    [InlineData(@"", "SAVE_AS_PATH_NOT_ABSOLUTE")]
    [InlineData(@"D:\Work\New.iam", "SAVE_AS_WRONG_EXTENSION")]
    [InlineData(@"D:\Work\New.ipt.bak", "SAVE_AS_WRONG_EXTENSION")]
    public void Save_as_path_shape(string target, string? expected) => Assert.Equal(expected, SaveAs(target));

    [Fact]
    public void Save_as_never_overwrites_and_needs_an_existing_local_folder()
    {
        Assert.Equal("SAVE_AS_TARGET_EXISTS", SaveAs(@"D:\Work\Old.ipt", exists: true));
        Assert.Equal("SAVE_AS_FOLDER_MISSING", SaveAs(@"D:\Missing\New.ipt", folder: false));
        Assert.Equal("SAVE_AS_PATH_NOT_LOCAL", SaveAs(@"Z:\Mapped\New.ipt", network: true));
        Assert.Null(SaveAs(@"D:\Work\Sheet.dwg", DocumentSavePolicy.ExtensionsFor("Drawing")));
    }
}
