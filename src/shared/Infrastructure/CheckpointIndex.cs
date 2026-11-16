using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Shared.Infrastructure;

// Checkpoints (rvt-mcp parity: src/shared/Documents/Checkpoint*.cs). A checkpoint is a verified copy of
// one Inventor file in "<name>.checkpoints\" next to it, listed in index.json. Host-free and unit-tested.

public sealed class CheckpointSource
{
    /// <summary>Document.FileSaveCounter of the saved file the copy was taken from.</summary>
    [JsonProperty("file_save_counter", Required = Required.Always)] public int FileSaveCounter { get; set; }
    /// <summary>SHA-256 of the source file; the copy was verified against it.</summary>
    [JsonProperty("sha256", Required = Required.Always)] public string Sha256 { get; set; } = "";
}

public sealed class CheckpointEntry
{
    [JsonProperty("id", Required = Required.Always)] public string Id { get; set; } = "";
    [JsonProperty("file", Required = Required.Always)] public string File { get; set; } = "";
    [JsonProperty("label", Required = Required.Always)] public string Label { get; set; } = "";
    [JsonProperty("reason")] public string? Reason { get; set; }
    [JsonProperty("milestone", Required = Required.Always)] public bool Milestone { get; set; }
    [JsonProperty("created_utc", Required = Required.Always)] public string CreatedUtc { get; set; } = "";
    [JsonProperty("bytes", Required = Required.Always)] public long Bytes { get; set; }
    [JsonProperty("source", Required = Required.Always)] public CheckpointSource Source { get; set; } = new();
    [JsonProperty("inventor_year", Required = Required.Always)] public int InventorYear { get; set; }
    [JsonProperty("user", Required = Required.Always)] public string User { get; set; } = "";
    /// <summary>Referenced documents NOT copied (assembly/drawing checkpoints hold only the file itself).</summary>
    [JsonProperty("references_not_captured")] public int ReferencesNotCaptured { get; set; }
}

public sealed class CheckpointIndexData
{
    [JsonIgnore] public bool Exists { get; internal set; }
    [JsonProperty("schema", Required = Required.Always)] public int Schema { get; set; } = 1;
    [JsonProperty("model_file", Required = Required.Always)] public string ModelFile { get; set; } = "";
    [JsonProperty("checkpoints", Required = Required.Always)] public List<CheckpointEntry> Checkpoints { get; set; } = new();
}

public static class CheckpointIndex
{
    public static CheckpointIndexData Load(string path, string modelFile)
    {
        var fullPath = Path.GetFullPath(path);
        return WithMutex(fullPath, () => LoadUnderMutex(fullPath, modelFile));
    }

    public static CheckpointIndexData Update(string path, string modelFile, Action<CheckpointIndexData> mutate)
    {
        if (mutate == null) throw new ArgumentNullException(nameof(mutate));
        return Update(path, modelFile, data => { mutate(data); return true; });
    }

    public static CheckpointIndexData Update(string path, string modelFile, Func<CheckpointIndexData, bool> mutate)
    {
        if (mutate == null) throw new ArgumentNullException(nameof(mutate));
        var fullPath = Path.GetFullPath(path);
        return WithMutex(fullPath, () =>
        {
            var data = LoadUnderMutex(fullPath, modelFile);
            if (!mutate(data)) return data;
            Validate(data);
            if (!string.Equals(data.ModelFile, modelFile, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checkpoint index belongs to a different file.");
            WriteUnderMutex(fullPath, data);
            data.Exists = true;
            return data;
        });
    }

    private static T WithMutex<T>(string path, Func<T> action)
    {
        string name;
        using (var sha = SHA256.Create())
            name = "Local\\IptMcp.CheckpointIndex." + BitConverter.ToString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-", "");
        using var mutex = new Mutex(false, name);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("CHECKPOINT_INDEX_BUSY");
            return action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private static CheckpointIndexData LoadUnderMutex(string path, string modelFile)
    {
        try
        {
            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) json = reader.ReadToEnd();
            var data = JsonConvert.DeserializeObject<CheckpointIndexData>(json);
            Validate(data);
            if (!string.Equals(data!.ModelFile, modelFile, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checkpoint index belongs to a different file.");
            data.Exists = true;
            return data;
        }
        catch (FileNotFoundException) { return new CheckpointIndexData { ModelFile = modelFile }; }
        catch (DirectoryNotFoundException) { return new CheckpointIndexData { ModelFile = modelFile }; }
        catch (JsonException ex) { throw new InvalidDataException("Checkpoint index is unreadable.", ex); }
    }

    private static void WriteUnderMutex(string path, CheckpointIndexData data)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(data, Formatting.Indented));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (System.IO.File.Exists(path)) System.IO.File.Replace(temporary, path, null);
            else System.IO.File.Move(temporary, path);
        }
        finally
        {
            if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary);
        }
    }

    private static void Validate(CheckpointIndexData? data)
    {
        if (data == null || data.Schema != 1 || !IsFileName(data.ModelFile) || data.Checkpoints == null)
            throw new InvalidDataException("Checkpoint index is unreadable.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in data.Checkpoints)
        {
            if (entry == null || !IsFileName(entry.Id) || !IsFileName(entry.File)
                || string.IsNullOrWhiteSpace(entry.Label) || entry.Label.Length > 80
                || (entry.Reason != null && entry.Reason.Length > 500) || entry.Bytes < 0
                || !DateTimeOffset.TryParse(entry.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || entry.Source == null || entry.Source.FileSaveCounter < 0 || entry.Source.Sha256.Length != 64
                || entry.InventorYear <= 0 || entry.User == null || entry.ReferencesNotCaptured < 0
                || !ids.Add(entry.Id) || !files.Add(entry.File))
                throw new InvalidDataException("Checkpoint index contains an invalid entry.");
        }
    }

    internal static bool IsFileName(string? name)
        => !string.IsNullOrWhiteSpace(name) && name != "." && name != ".."
           && name!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal);
}
