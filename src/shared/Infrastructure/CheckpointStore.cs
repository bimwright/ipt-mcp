using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Bimwright.Ipt.Shared.Infrastructure;

public sealed class CheckpointName
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
}

/// <summary>Where a file's checkpoints live: "&lt;dir&gt;\&lt;name&gt;.checkpoints\" with index.json.</summary>
public static class CheckpointPaths
{
    public static readonly string[] Extensions = { ".ipt", ".iam", ".idw", ".dwg", ".ipn" };
    public static string Directory(string filePath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(filePath))!, Path.GetFileNameWithoutExtension(filePath) + ".checkpoints");
    public static string Index(string filePath) => Path.Combine(Directory(filePath), "index.json");
    public static bool Supported(string? filePath)
        => !string.IsNullOrEmpty(filePath) && Extensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
}

public static class CheckpointNaming
{
    // The caller must still copy with overwrite: false to reject a concurrent collision.
    public static CheckpointName Next(string directory, string extension, DateTimeOffset localTime, IEnumerable<CheckpointEntry>? entries)
    {
        if (!CheckpointPaths.Extensions.Contains((extension ?? "").ToLowerInvariant()))
            throw new ArgumentException("Checkpoint extension must be .ipt, .iam, .idw, .dwg or .ipn.", nameof(extension));
        var list = (entries ?? Enumerable.Empty<CheckpointEntry>()).ToList();
        var existingIds = new HashSet<string>(list.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
        var existingFiles = new HashSet<string>(list.Select(e => e.File), StringComparer.OrdinalIgnoreCase);
        var stem = localTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        extension = extension!.ToLowerInvariant();
        for (var suffix = 1; ; suffix++)
        {
            var id = suffix == 1 ? stem : stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            var file = id + extension;
            if (!existingIds.Contains(id) && !existingFiles.Contains(file) && !System.IO.File.Exists(Path.Combine(directory, file)))
                return new CheckpointName { Id = id, File = file };
        }
    }
}

public static class CheckpointFiles
{
    public static void CopyNew(string source, string target)
    {
        // Only remove a target after File.Copy has successfully created it.
        System.IO.File.Copy(source, target, false);
        try
        {
            var attributes = System.IO.File.GetAttributes(target);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                System.IO.File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
        }
        catch
        {
            try { System.IO.File.Delete(target); } catch { }
            throw;
        }
    }

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>A collision on copy (ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS).</summary>
    public static bool IsExists(IOException ex) => (ex.HResult & 0xffff) == 80 || (ex.HResult & 0xffff) == 183;
}

public static class CheckpointSpace
{
    /// <summary>Keep 10% headroom beyond the copy itself.</summary>
    public static bool HasEnough(long sourceBytes, long freeBytes)
    {
        if (sourceBytes < 0) throw new ArgumentOutOfRangeException(nameof(sourceBytes));
        if (freeBytes < 0) throw new ArgumentOutOfRangeException(nameof(freeBytes));
        return (decimal)freeBytes * 10 >= (decimal)sourceBytes * 11;
    }

    public static long AvailableBytes(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        // UNC directories need a trailing separator for GetDiskFreeSpaceEx.
        if (!directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            directory += Path.DirectorySeparatorChar;
        if (!GetDiskFreeSpaceEx(directory, out var available, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return available > long.MaxValue ? long.MaxValue : (long)available;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong totalBytes, out ulong totalFreeBytes);
}

public sealed class PruneCandidate
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool Exists { get; set; }
    public long Bytes { get; set; }
    public DateTime? LastWriteUtc { get; set; }
}

public sealed class CheckpointPrunePlan
{
    public List<PruneCandidate> DeleteFiles { get; set; } = new();
    public List<PruneCandidate> MissingEntries { get; set; } = new();
    public long BytesToFree { get; set; }
    public string PlanHash { get; set; } = "";
}

public static class CheckpointPlanner
{
    public static CheckpointPrunePlan Plan(string directory, IEnumerable<CheckpointEntry> entries, int keepLast)
    {
        if (keepLast < 1 || keepLast > 100) throw new ArgumentOutOfRangeException(nameof(keepLast));
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        var root = Path.GetFullPath(directory);
        if (!Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .EndsWith(".checkpoints", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The directory is not a checkpoints directory.");
        try
        {
            var rootAttributes = System.IO.File.GetAttributes(root);
            if ((rootAttributes & FileAttributes.Directory) == 0 || (rootAttributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Checkpoint directory is not a regular directory.");
        }
        catch (DirectoryNotFoundException) { }
        catch (FileNotFoundException) { }

        var states = new List<(CheckpointEntry Entry, DateTimeOffset Created, PruneCandidate Candidate)>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id)
                || string.IsNullOrWhiteSpace(entry.File) || !files.Add(entry.File)
                || !DateTimeOffset.TryParse(entry.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
                throw new InvalidDataException("Invalid checkpoint entry.");
            var path = ResolvePath(root, entry.File);
            var candidate = new PruneCandidate { Id = entry.Id, File = entry.File, FullPath = path };
            try
            {
                var attributes = System.IO.File.GetAttributes(path);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new InvalidDataException("Checkpoint is not a regular file.");
                var info = new FileInfo(path);
                candidate.Exists = true;
                candidate.Bytes = info.Length;
                candidate.LastWriteUtc = info.LastWriteTimeUtc;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            states.Add((entry, created, candidate));
        }

        var missing = states.Where(s => !s.Candidate.Exists).Select(s => s.Candidate).ToList();
        var deletions = states.Where(s => s.Candidate.Exists && !s.Entry.Milestone)
            .OrderByDescending(s => s.Created).ThenBy(s => s.Entry.Id, StringComparer.Ordinal)
            .Skip(keepLast).Select(s => s.Candidate).ToList();
        return new CheckpointPrunePlan
        {
            DeleteFiles = deletions,
            MissingEntries = missing,
            BytesToFree = deletions.Sum(c => c.Bytes),
            PlanHash = Hash(keepLast, deletions.Concat(missing)),
        };
    }

    private static string ResolvePath(string root, string file)
    {
        if (Path.IsPathRooted(file) || file == "." || file == ".."
            || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || file.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new InvalidDataException("Checkpoint file must be directly inside the checkpoints directory.");
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, file));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint file is outside the checkpoints directory.");
        return full;
    }

    private static string Hash(int keepLast, IEnumerable<PruneCandidate> candidates)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(keepLast);
            foreach (var item in candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ThenBy(c => c.File, StringComparer.Ordinal))
            {
                writer.Write(item.Id);
                writer.Write(item.File);
                writer.Write(item.Exists);
                writer.Write(item.Bytes);
                writer.Write(item.LastWriteUtc?.Ticks ?? 0);
            }
        }
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
    }
}

public sealed class CheckpointListItem
{
    public CheckpointEntry Entry { get; set; } = new();
    public bool Exists { get; set; }
    public long? Bytes { get; set; }
}

public sealed class CheckpointListResult
{
    public List<CheckpointListItem> Items { get; set; } = new();
    public List<string> UntrackedFiles { get; set; } = new();
    public string IndexStatus { get; set; } = "";
}

public sealed class CheckpointPruneResult
{
    public string? Error { get; set; }
    public List<string> DeletedIds { get; set; } = new();
    public List<string> MissingIds { get; set; } = new();
    public List<string> FailedIds { get; set; } = new();
}

public sealed class CheckpointRestoreResult
{
    public string? Error { get; set; }
    public string? Path { get; set; }
}

/// <summary>List, prune (with an approved plan hash) and restore (as a new file) one file's checkpoints.</summary>
public sealed class CheckpointStore
{
    private readonly string _filePath;
    private readonly string _fileName;
    private readonly string _folder;
    private readonly string _index;

    public CheckpointStore(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        _fileName = Path.GetFileName(_filePath);
        _folder = CheckpointPaths.Directory(_filePath);
        _index = CheckpointPaths.Index(_filePath);
    }

    public CheckpointListResult List()
    {
        var index = CheckpointIndex.Load(_index, _fileName);
        // Validate every indexed file and the checkpoint directory before exposing paths.
        CheckpointPlanner.Plan(_folder, index.Checkpoints, 100);
        var items = index.Checkpoints.OrderByDescending(c => DateTimeOffset.Parse(c.CreatedUtc, CultureInfo.InvariantCulture))
            .Select(entry =>
            {
                var file = Path.Combine(_folder, entry.File);
                var exists = System.IO.File.Exists(file);
                return new CheckpointListItem { Entry = entry, Exists = exists, Bytes = exists ? new FileInfo(file).Length : null };
            }).ToList();
        var tracked = new HashSet<string>(index.Checkpoints.Select(c => c.File), StringComparer.OrdinalIgnoreCase) { "index.json" };
        var untracked = System.IO.Directory.Exists(_folder)
            ? System.IO.Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)).Where(name => !tracked.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        return new CheckpointListResult { Items = items, UntrackedFiles = untracked, IndexStatus = index.Exists ? "ok" : "not_created" };
    }

    public CheckpointPrunePlan Plan(int keepLast)
    {
        var index = CheckpointIndex.Load(_index, _fileName);
        return CheckpointPlanner.Plan(_folder, index.Checkpoints, keepLast);
    }

    public CheckpointPruneResult Prune(int keepLast, string? approvedHash)
    {
        var result = new CheckpointPruneResult();
        if (string.IsNullOrWhiteSpace(approvedHash)) { result.Error = "PLAN_HASH_REQUIRED"; return result; }
        var pendingMissing = new List<string>();
        try
        {
            CheckpointIndex.Update(_index, _fileName, data =>
            {
                var plan = CheckpointPlanner.Plan(_folder, data.Checkpoints, keepLast);
                if (!string.Equals(plan.PlanHash, approvedHash, StringComparison.OrdinalIgnoreCase)) throw new PlanChangedException();
                if (!data.Exists) return false;
                foreach (var missing in plan.MissingEntries)
                {
                    data.Checkpoints.RemoveAll(c => c.Id == missing.Id);
                    pendingMissing.Add(missing.Id);
                }
                foreach (var candidate in plan.DeleteFiles)
                {
                    var entry = data.Checkpoints.Single(c => c.Id == candidate.Id);
                    try
                    {
                        // Revalidate containment, reparse status and approved file metadata immediately before deletion.
                        CheckpointPlanner.Plan(_folder, new[] { entry }, 1);
                        var info = new FileInfo(candidate.FullPath);
                        if (!info.Exists || info.Length != candidate.Bytes || info.LastWriteTimeUtc != candidate.LastWriteUtc)
                        {
                            result.FailedIds.Add(candidate.Id);
                            continue;
                        }
                        System.IO.File.Delete(candidate.FullPath);
                        if (System.IO.File.Exists(candidate.FullPath)) { result.FailedIds.Add(candidate.Id); continue; }
                        data.Checkpoints.RemoveAll(c => c.Id == candidate.Id);
                        result.DeletedIds.Add(candidate.Id);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        result.FailedIds.Add(candidate.Id);
                    }
                }
                return result.DeletedIds.Count != 0 || pendingMissing.Count != 0;
            });
            result.MissingIds.AddRange(pendingMissing);
        }
        catch (PlanChangedException) { result.Error = "PLAN_CHANGED"; }
        catch (TimeoutException) { result.Error = "CHECKPOINT_INDEX_BUSY"; }
        catch (InvalidDataException) { result.Error = result.DeletedIds.Count != 0 || pendingMissing.Count != 0 ? "CHECKPOINT_INDEX_WRITE_FAILED" : "CHECKPOINT_INDEX_UNREADABLE"; }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            result.Error = result.DeletedIds.Count != 0 || pendingMissing.Count != 0 ? "CHECKPOINT_INDEX_WRITE_FAILED" : "CHECKPOINT_INDEX_UNREADABLE";
        }
        return result;
    }

    /// <summary>Copies a checkpoint to "&lt;name&gt;-restored-&lt;id&gt;.ext" next to the file; the working file is never overwritten.</summary>
    public CheckpointRestoreResult Restore(string? checkpointId)
    {
        if (string.IsNullOrWhiteSpace(checkpointId)) return new CheckpointRestoreResult { Error = "CHECKPOINT_ID_REQUIRED" };
        var index = CheckpointIndex.Load(_index, _fileName);
        var entry = index.Checkpoints.SingleOrDefault(c => c.Id == checkpointId);
        if (entry == null) return new CheckpointRestoreResult { Error = "CHECKPOINT_NOT_FOUND" };
        CheckpointPlanner.Plan(_folder, new[] { entry }, 1);
        var source = Path.Combine(_folder, entry.File);
        if (!System.IO.File.Exists(source)) return new CheckpointRestoreResult { Error = "CHECKPOINT_FILE_MISSING" };
        var target = Path.Combine(Path.GetDirectoryName(_filePath)!,
            Path.GetFileNameWithoutExtension(_filePath) + "-restored-" + entry.Id + Path.GetExtension(_filePath));
        if (System.IO.File.Exists(target) || System.IO.Directory.Exists(target)) return new CheckpointRestoreResult { Error = "RESTORE_TARGET_EXISTS" };
        try { CheckpointFiles.CopyNew(source, target); }
        catch (IOException ex) when (CheckpointFiles.IsExists(ex) || System.IO.Directory.Exists(target))
        {
            return new CheckpointRestoreResult { Error = "RESTORE_TARGET_EXISTS" };
        }
        catch (UnauthorizedAccessException) when (System.IO.Directory.Exists(target))
        {
            return new CheckpointRestoreResult { Error = "RESTORE_TARGET_EXISTS" };
        }
        if (!string.Equals(CheckpointFiles.Sha256(target), entry.Source.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { System.IO.File.Delete(target); } catch { }
            return new CheckpointRestoreResult { Error = "RESTORE_VERIFY_FAILED" };
        }
        return new CheckpointRestoreResult { Path = target };
    }

    private sealed class PlanChangedException : Exception { }
}
