using System;
using System.IO;
using System.Linq;
using System.Text;
using Bimwright.Ipt.Shared.Security;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Writes oversized command output to local, agent-readable spill files under
/// <c>%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\</c> (spec F3-b — the ipt-mcp cut of rvt-mcp's
/// spill machinery: text/JSON artifacts only, no sqlite/ndjson autoformat). Files expire
/// after 36 h by default. A cleanup deletes at most 50 expired files and never evicts
/// a fresh file because of a count limit. API-agnostic so the test suite
/// exercises it without Inventor.
/// </summary>
public sealed class ResponseSpillWriter
{
    /// <summary>Output above this many UTF-8 bytes is spilled (same as the 64 KiB warn tier).</summary>
    public const int SpillThresholdBytes = 64 * 1024;

    /// <summary>How much of the spilled payload stays inline in the response.</summary>
    public const int InlineKeepBytes = 8 * 1024;

    public const int MaxRetainedFiles = 50;
    public static readonly TimeSpan MaxFileAge = TimeSpan.FromHours(36);

    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly string _directory;
    private readonly TimeSpan _retention;

    public ResponseSpillWriter()
        : this(DefaultDirectory)
    {
    }

    public ResponseSpillWriter(string directory,int retentionHours=36)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Spill directory is required.", nameof(directory));
        _directory = Path.GetFullPath(directory);
        if(retentionHours<=0)throw new ArgumentOutOfRangeException(nameof(retentionHours));
        _retention=TimeSpan.FromHours(retentionHours);
    }

    public static ResponseSpillWriter ForContext(InventorCommandContext ctx) => new(DefaultDirectory,ctx.SpillRetentionHours);

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bimwright", "ipt-mcp", "spill");

    /// <summary>True when the payload is large enough to spill.</summary>
    public static bool ShouldSpill(string? text)
        => text is not null && Encoding.UTF8.GetByteCount(text) > SpillThresholdBytes;

    /// <summary>
    /// First <paramref name="maxBytes"/> UTF-8 bytes of <paramref name="text"/> without
    /// splitting a multi-byte character at the cut.
    /// </summary>
    public static string Utf8Prefix(string text, int maxBytes)
    {
        if (text is null) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes) return text;

        // A UTF-8 continuation byte is 10xxxxxx — walk back over it so the prefix
        // ends on a complete character.
        var cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;
        return Encoding.UTF8.GetString(bytes, 0, cut);
    }

    /// <summary>
    /// Persist <paramref name="content"/> as <c>&lt;command&gt;-&lt;yyyyMMdd-HHmmss&gt;-&lt;id&gt;&lt;ext&gt;</c>
    /// in the spill directory, then enforce TTL + retention cap. Returns the full path.
    /// </summary>
    public string Write(string commandName, string extension, string content)
    {
        Directory.CreateDirectory(_directory);
        var name = SanitizeName(commandName) + "-"
            + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension;
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, Utf8NoBom);
        Cleanup(DateTime.UtcNow);
        return path;
    }

    /// <summary>Deletes at most <see cref="MaxRetainedFiles"/> expired files, oldest first.
    /// Fresh files are retained even when their count exceeds this cleanup batch size.</summary>
    public int Cleanup(DateTime utcNow)
    {
        if (!Directory.Exists(_directory)) return 0;
        var deleted = 0;
        var dir = new DirectoryInfo(_directory);

        foreach (var f in dir.GetFiles().Where(f => utcNow - f.LastWriteTimeUtc > _retention).OrderBy(f=>f.LastWriteTimeUtc).Take(MaxRetainedFiles))
        {
            try { f.Delete(); deleted++; } catch { /* locked file — skip */ }
        }

        return deleted;
    }

    /// <summary>
    /// Sets <c>data["stdout"]</c>; when the text is oversized it is written to a spill file and
    /// the field is replaced by an 8 KiB prefix plus <c>stdout_truncated</c>/<c>stdout_file</c>
    /// (spec F3-b). A failed spill keeps the full stdout inline — the size guard still applies.
    /// </summary>
    public static void AttachStdout(string commandName, Newtonsoft.Json.Linq.JObject data, string stdout,
        ResponseSpillWriter? writer = null)
    {
        data["stdout"] = stdout;
        if (!ShouldSpill(stdout)) return;
        try
        {
            var file = (writer ?? new ResponseSpillWriter()).Write(commandName, ".txt", stdout);
            data["stdout"] = Utf8Prefix(stdout, InlineKeepBytes);
            data["stdout_truncated"] = true;
            data["stdout_file"] = file;
        }
        catch { /* keep full stdout inline */ }
    }

    /// <summary>
    /// Same policy for <c>run_baked_tool</c>'s / <c>batch_execute</c>'s <c>results</c> array,
    /// spilled as .json. Sanitization happens HERE, before serialization — the dispatcher's
    /// error-field pass only runs after this method returns, so un-sanitized results would
    /// persist raw paths/secrets to the spill file and the inline preview (review fix):
    /// error/message fields get the dispatcher's sanitizer, and the serialized payload that
    /// reaches disk or <c>results_preview</c> additionally passes through the secret masker.
    /// </summary>
    public static void AttachResults(string commandName, Newtonsoft.Json.Linq.JObject data, Newtonsoft.Json.Linq.JArray results,
        ResponseSpillWriter? writer = null)
    {
        foreach (var item in results.OfType<Newtonsoft.Json.Linq.JObject>())
            ErrorSanitizer.SanitizeErrorFields(item);
        var serialized = SecretMasker.Mask(results.ToString(Newtonsoft.Json.Formatting.None));
        data["results"] = results;
        if (!ShouldSpill(serialized)) return;
        try
        {
            var file = (writer ?? new ResponseSpillWriter()).Write(commandName, ".json", serialized);
            data.Remove("results");
            data["results_truncated"] = true;
            data["results_file"] = file;
            data["results_count"] = results.Count;
            data["results_preview"] = Utf8Prefix(serialized, InlineKeepBytes);
        }
        catch { /* keep full results inline */ }
    }

    /// <summary>
    /// Same policy for a script's single <c>result</c> value (send_code): above the threshold the
    /// JSON goes to a spill file and the response carries <c>result: null</c> plus
    /// <c>result_truncated</c>/<c>result_file</c>/<c>result_preview</c>, instead of tripping
    /// RESPONSE_TOO_LARGE after the script already ran. A failed spill keeps the result inline.
    /// </summary>
    public static void AttachResult(string commandName, Newtonsoft.Json.Linq.JObject data, Newtonsoft.Json.Linq.JToken? result,
        ResponseSpillWriter? writer = null)
    {
        data["result"] = result ?? Newtonsoft.Json.Linq.JValue.CreateNull();
        if (result is null) return;
        // Size on the raw payload (what would travel inline); the masked form is what reaches disk.
        var raw = result.ToString(Newtonsoft.Json.Formatting.None);
        if (!ShouldSpill(raw)) return;
        var serialized = SecretMasker.Mask(raw);
        try
        {
            var file = (writer ?? new ResponseSpillWriter()).Write(commandName + "-result", ".json", serialized);
            data["result"] = Newtonsoft.Json.Linq.JValue.CreateNull();
            data["result_truncated"] = true;
            data["result_file"] = file;
            data["result_bytes"] = Encoding.UTF8.GetByteCount(raw);
            data["result_preview"] = Utf8Prefix(serialized, InlineKeepBytes);
        }
        catch { /* keep full result inline */ }
    }

    private static string SanitizeName(string commandName)
    {
        var chars = commandName
            .Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_')
            .ToArray();
        return new string(chars);
    }
}
