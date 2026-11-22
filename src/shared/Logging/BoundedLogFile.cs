using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>Generated records only. Append, rotation and retention share a full-path mutex.</summary>
    public static class BoundedLogFile
    {
        public const long RecordFileBytes = 5L * 1024 * 1024;
        public const long RecordGroupBytes = 100L * 1024 * 1024;
        public const int RecordRetentionDays = 30;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Callbacks must be synchronous: mutex ownership is thread-affine. Failure never writes unlocked.</summary>
        public static T WithLock<T>(string path, Func<T> action, int timeoutMilliseconds = 2000)
        {
            var full = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar == '\\') full = full.ToUpperInvariant();
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(full))).Replace("-", "");
            var name = (Path.DirectorySeparatorChar == '\\' ? @"Global\" : "") + "Bimwright.Ipt.Log." + hash;
            using (var mutex = new Mutex(false, name))
            {
                var acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(timeoutMilliseconds); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new TimeoutException("Timed out waiting for the MCP log file lock.");
                    RefuseLink(path);
                    return action();
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }

        public static void Append(string path, string line, long fileBytes = RecordFileBytes,
            long groupBytes = RecordGroupBytes, int retentionDays = RecordRetentionDays, DateTimeOffset? now = null)
        {
            WithLock(path, () => { AppendUnderLock(path, line, fileBytes, groupBytes, retentionDays, now); return true; });
        }

        public static void AppendUnderLock(string path, string line, long fileBytes = RecordFileBytes,
            long groupBytes = RecordGroupBytes, int retentionDays = RecordRetentionDays, DateTimeOffset? now = null)
        {
            path = Path.GetFullPath(path);
            var byteCount = (long)Utf8.GetByteCount(line ?? "") + 1;
            if (fileBytes <= 0 || groupBytes < fileBytes || retentionDays < 0)
                throw new ArgumentOutOfRangeException(nameof(fileBytes));
            if (byteCount > fileBytes) throw new InvalidDataException("Log record exceeds the file budget.");
            var utc = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
            RefuseLink(path);
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            // LastWriteTime avoids NTFS filename tunneling restoring an old creation time
            // when the active filename is recreated immediately after rotation.
            if (File.Exists(path) && (new FileInfo(path).Length + byteCount > fileBytes || File.GetLastWriteTimeUtc(path).Date < utc.Date))
                Rotate(path, utc); // A failed rotation cannot fall through to an oversized append.
            Prune(path, groupBytes - byteCount, utc.AddDays(-retentionDays));
            var bytes = Utf8.GetBytes((line ?? "") + "\n");
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                stream.Write(bytes, 0, bytes.Length);
        }

        private static void Rotate(string path, DateTime utc)
        {
            RefuseLink(path);
            var archive = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path)
                + "-" + utc.ToString("yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            File.Move(path, archive);
        }

        /// <summary>Run for an existing group even when its opt-in writer is disabled; never creates or enables it.</summary>
        public static void Maintain(string path, long fileBytes = RecordFileBytes, long groupBytes = RecordGroupBytes,
            int retentionDays = RecordRetentionDays, DateTimeOffset? now = null)
        {
            path = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(path))) return;
            WithLock(path, () =>
            {
                MaintainUnderLock(path, fileBytes, groupBytes, retentionDays, now);
                return true;
            }, 250);
        }

        public static void MaintainUnderLock(string path, long fileBytes = RecordFileBytes, long groupBytes = RecordGroupBytes,
            int retentionDays = RecordRetentionDays, DateTimeOffset? now = null)
        {
            path = Path.GetFullPath(path);
            RefuseLink(path);
            if (!Directory.Exists(Path.GetDirectoryName(path))) return;
            var utc = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
            if (File.Exists(path) && (new FileInfo(path).Length > fileBytes || File.GetLastWriteTimeUtc(path) < utc.AddDays(-retentionDays)))
                Rotate(path, utc);
            Prune(path, groupBytes, utc.AddDays(-retentionDays));
        }

        public static string[] Files(string directory, string group)
            => FilesForPath(Path.Combine(directory, group + ".jsonl"));

        /// <summary>Only this exact basename/extension and generated timestamp archives belong to the group.</summary>
        public static string[] FilesForPath(string path)
        {
            path = Path.GetFullPath(path);
            RefuseLink(path);
            var directory = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory)) return new string[0];
            var pattern = "^" + Regex.Escape(Path.GetFileNameWithoutExtension(path))
                + @"(?:-\d{8}-\d{6}(?:\d{7})?(?:-[0-9a-f]{32})?)?" + Regex.Escape(Path.GetExtension(path)) + "$";
            return Directory.EnumerateFiles(directory)
                .Where(p => Regex.IsMatch(Path.GetFileName(p), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
                .OrderBy(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal).ToArray();
        }

        private static void Prune(string active, long budget, DateTime cutoff)
        {
            if (LogRetentionPolicy.KeepAll) return; // keep every archive; no budget check, no throw
            var files = FilesForPath(active);
            long total = files.Sum(p => new FileInfo(p).Length);
            foreach (var file in files)
            {
                if (string.Equals(file, active, StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc >= cutoff && total <= budget) continue;
                var length = info.Length;
                RefuseLink(file);
                File.Delete(file); // A locked archive prevents growth, rather than bypassing the budget.
                total -= length;
            }
            if (total > budget) throw new IOException("Log group budget is unavailable.");
        }

        public static void RefuseLink(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Log maintenance refuses linked files.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            for (var directory = Path.GetDirectoryName(Path.GetFullPath(path)); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
                if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Log maintenance refuses linked directories.");
        }

        /// <summary>Skip huge malformed lines without allocating the whole line; allow concurrent append and rotation.</summary>
        public static IEnumerable<string> ReadLines(string path, int maxLineCharacters = 5 * 1024 * 1024)
        {
            if (maxLineCharacters < 0) throw new ArgumentOutOfRangeException(nameof(maxLineCharacters));
            RefuseLink(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Utf8))
            {
                var line = new StringBuilder();
                var overflow = false;
                int value;
                while ((value = reader.Read()) != -1)
                {
                    if (value == '\n')
                    {
                        if (!overflow) yield return line.ToString().TrimEnd('\r');
                        line.Clear();
                        overflow = false;
                    }
                    else if (!overflow)
                    {
                        if (line.Length >= maxLineCharacters) { line.Clear(); overflow = true; }
                        else line.Append((char)value);
                    }
                }
                if (!overflow && line.Length > 0) yield return line.ToString().TrimEnd('\r');
            }
        }

        public static IEnumerable<string> ReadLinesSafe(string path, int maxLineCharacters = 5 * 1024 * 1024)
        {
            using (var reader = ReadLines(path, maxLineCharacters).GetEnumerator())
                while (true)
                {
                    bool next;
                    try { next = reader.MoveNext(); }
                    catch (IOException) { yield break; }
                    catch (UnauthorizedAccessException) { yield break; }
                    if (!next) yield break;
                    yield return reader.Current;
                }
        }
    }
}
