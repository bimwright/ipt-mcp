using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

/// <summary>Host-free checkpoint store (rvt-mcp parity): index, naming, verified restore and plan-hash pruning.</summary>
public sealed class CheckpointStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ipt-checkpoints-" + Guid.NewGuid().ToString("N"));
    private readonly string _part;

    public CheckpointStoreTests()
    {
        Directory.CreateDirectory(_root);
        _part = Path.Combine(_root, "Bracket.ipt");
        File.WriteAllText(_part, "part v1");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    /// <summary>What save_checkpoint does after saving: name, copy, verify, index.</summary>
    private CheckpointEntry Take(string label, DateTimeOffset at, bool milestone = false)
    {
        var folder = CheckpointPaths.Directory(_part);
        Directory.CreateDirectory(folder);
        var index = CheckpointIndex.Load(CheckpointPaths.Index(_part), "Bracket.ipt");
        var name = CheckpointNaming.Next(folder, ".ipt", at, index.Checkpoints);
        var target = Path.Combine(folder, name.File);
        CheckpointFiles.CopyNew(_part, target);
        var entry = new CheckpointEntry
        {
            Id = name.Id, File = name.File, Label = label, Milestone = milestone, CreatedUtc = at.ToUniversalTime().ToString("o"),
            Bytes = new FileInfo(target).Length, InventorYear = 2027, User = "tester",
            Source = new CheckpointSource { FileSaveCounter = 3, Sha256 = CheckpointFiles.Sha256(_part) },
        };
        CheckpointIndex.Update(CheckpointPaths.Index(_part), "Bracket.ipt", d => d.Checkpoints.Add(entry));
        return entry;
    }

    [Fact]
    public void Paths_sit_next_to_the_file_and_only_inventor_files_are_supported()
    {
        Assert.Equal(Path.Combine(_root, "Bracket.checkpoints"), CheckpointPaths.Directory(_part));
        Assert.Equal(Path.Combine(_root, "Bracket.checkpoints", "index.json"), CheckpointPaths.Index(_part));
        foreach (var ext in new[] { ".ipt", ".IAM", ".idw", ".dwg", ".ipn" }) Assert.True(CheckpointPaths.Supported("x" + ext));
        Assert.False(CheckpointPaths.Supported("x.rvt"));
        Assert.False(CheckpointPaths.Supported(""));
    }

    [Fact]
    public void Names_are_timestamps_and_never_collide()
    {
        var at = new DateTimeOffset(2026, 11, 16, 10, 15, 0, TimeSpan.FromHours(7));
        var first = Take("one", at);
        var second = Take("two", at);
        Assert.Equal("20261116-101500", first.Id);
        Assert.Equal("20261116-101500-2", second.Id);
        Assert.Equal("20261116-101500-2.ipt", second.File);
        Assert.Throws<ArgumentException>(() => CheckpointNaming.Next(_root, ".rvt", at, null));
    }

    [Fact]
    public void Copy_never_overwrites_and_clears_read_only()
    {
        var target = Path.Combine(_root, "copy.ipt");
        File.SetAttributes(_part, FileAttributes.ReadOnly);
        try
        {
            CheckpointFiles.CopyNew(_part, target);
            Assert.False(new FileInfo(target).IsReadOnly);
            var error = Assert.ThrowsAny<IOException>(() => CheckpointFiles.CopyNew(_part, target));
            Assert.True(CheckpointFiles.IsExists(error));
        }
        finally { File.SetAttributes(_part, FileAttributes.Normal); }
    }

    [Fact]
    public void List_reports_entries_newest_first_and_untracked_files()
    {
        var older = Take("older", DateTimeOffset.Now.AddMinutes(-5));
        var newer = Take("newer", DateTimeOffset.Now);
        File.WriteAllText(Path.Combine(CheckpointPaths.Directory(_part), "stray.ipt"), "x");
        var list = new CheckpointStore(_part).List();
        Assert.Equal("ok", list.IndexStatus);
        Assert.Equal(new[] { newer.Id, older.Id }, list.Items.Select(i => i.Entry.Id));
        Assert.All(list.Items, i => Assert.True(i.Exists));
        Assert.Equal(new[] { "stray.ipt" }, list.UntrackedFiles);
    }

    [Fact]
    public void List_without_checkpoints_is_not_created()
        => Assert.Equal("not_created", new CheckpointStore(_part).List().IndexStatus);

    [Fact]
    public void Restore_creates_a_verified_new_file_and_never_overwrites()
    {
        var entry = Take("v1", DateTimeOffset.Now);
        File.WriteAllText(_part, "part v2 (later work)");
        var store = new CheckpointStore(_part);

        var restored = store.Restore(entry.Id);
        Assert.Null(restored.Error);
        Assert.Equal(Path.Combine(_root, "Bracket-restored-" + entry.Id + ".ipt"), restored.Path);
        Assert.Equal("part v1", File.ReadAllText(restored.Path!));
        Assert.Equal("part v2 (later work)", File.ReadAllText(_part));   // working file untouched

        Assert.Equal("RESTORE_TARGET_EXISTS", store.Restore(entry.Id).Error);
        Assert.Equal("CHECKPOINT_NOT_FOUND", store.Restore("nope").Error);
        Assert.Equal("CHECKPOINT_ID_REQUIRED", store.Restore(" ").Error);
    }

    [Fact]
    public void Restore_refuses_a_tampered_checkpoint()
    {
        var entry = Take("v1", DateTimeOffset.Now);
        File.WriteAllText(Path.Combine(CheckpointPaths.Directory(_part), entry.File), "tampered");
        Assert.Equal("RESTORE_VERIFY_FAILED", new CheckpointStore(_part).Restore(entry.Id).Error);
        Assert.False(File.Exists(Path.Combine(_root, "Bracket-restored-" + entry.Id + ".ipt")));
    }

    [Fact]
    public void Prune_needs_the_previewed_plan_hash_and_keeps_milestones()
    {
        var now = DateTimeOffset.Now;
        var milestone = Take("milestone", now.AddMinutes(-10), milestone: true);
        var oldest = Take("a", now.AddMinutes(-3));
        var middle = Take("b", now.AddMinutes(-2));
        var newest = Take("c", now.AddMinutes(-1));
        var store = new CheckpointStore(_part);

        var plan = store.Plan(keepLast: 2);
        Assert.Equal(new[] { oldest.Id }, plan.DeleteFiles.Select(c => c.Id));
        Assert.Equal("PLAN_HASH_REQUIRED", store.Prune(2, null).Error);
        Assert.Equal("PLAN_CHANGED", store.Prune(2, new string('0', 64)).Error);
        Assert.True(File.Exists(Path.Combine(CheckpointPaths.Directory(_part), oldest.File)));

        var result = store.Prune(2, plan.PlanHash);
        Assert.Null(result.Error);
        Assert.Equal(new[] { oldest.Id }, result.DeletedIds);
        Assert.Equal(new[] { newest.Id, middle.Id, milestone.Id }, store.List().Items.Select(i => i.Entry.Id));
    }

    [Fact]
    public void A_plan_goes_stale_when_a_checkpoint_changes()
    {
        var now = DateTimeOffset.Now;
        var oldest = Take("a", now.AddMinutes(-2));
        Take("b", now.AddMinutes(-1));
        var store = new CheckpointStore(_part);
        var plan = store.Plan(1);
        File.WriteAllText(Path.Combine(CheckpointPaths.Directory(_part), oldest.File), "changed after the user approved");
        Assert.Equal("PLAN_CHANGED", store.Prune(1, plan.PlanHash).Error);
    }

    [Fact]
    public void Index_rejects_entries_that_escape_the_folder_or_belong_to_another_file()
    {
        var entry = Take("v1", DateTimeOffset.Now);
        var indexPath = CheckpointPaths.Index(_part);
        Assert.Throws<InvalidDataException>(() => CheckpointIndex.Load(indexPath, "Other.ipt"));
        File.WriteAllText(indexPath, File.ReadAllText(indexPath).Replace(entry.File, @"..\\escape.ipt"));
        Assert.Throws<InvalidDataException>(() => CheckpointIndex.Load(indexPath, "Bracket.ipt"));
    }

    [Theory]
    [InlineData(100, 110, true)]
    [InlineData(100, 109, false)]
    [InlineData(0, 0, true)]
    public void Space_keeps_ten_percent_headroom(long source, long free, bool enough)
        => Assert.Equal(enough, CheckpointSpace.HasEnough(source, free));
}
