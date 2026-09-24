using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Security;

/// <summary>
/// Redacts secrets out of baked-tool source before it is persisted to the server registry.
/// Masks assignment-style credentials and then runs <see cref="SecretMasker"/>. Ported from nwd-mcp.
/// <see cref="RedactForBake"/> is the broader variant used by the command-history log and
/// send-code journal (rvt-mcp parity): it additionally strips URLs, Windows paths, and
/// Inventor file names so stored rows stay free of local paths and project names.
/// </summary>
public static class BakeRedactor
{
    private static readonly Regex AssignmentSecret = new Regex(
        @"(?i)\b(api[_-]?key|auth[_-]?token|password|secret|token)\b\s*=\s*[""'][^""']+[""']",
        RegexOptions.Compiled);

    private static readonly Regex Url = new Regex(
        @"https?://[^\s""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WindowsPath = new Regex(
        @"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*",
        RegexOptions.Compiled);

    private static readonly Regex UncPath = new Regex(
        @"\\\\[^\\/:*?""<>|\r\n]+\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*",
        RegexOptions.Compiled);

    private static readonly Regex InventorFile = new Regex(
        @"(?i)\b[\w .\-()]+\.(ipt|iam|idw|ipn|ide)\b",
        RegexOptions.Compiled);

    private static readonly Regex JsonResultField = new Regex(
        @"""result""\s*:\s*""(?:\\.|[^""\\])*""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string RedactSource(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        return SecretMasker.Mask(AssignmentSecret.Replace(source, match =>
        {
            var key = match.Groups[1].Value;
            return key + " = \"<secret>\"";
        }));
    }

    /// <summary>
    /// Broad redaction for persisted/displayed payloads: assignment secrets + secret
    /// masking + URLs + local paths + Inventor file names. With
    /// <paramref name="redactResultFields"/> the value of any "result" JSON field is
    /// replaced too (send_code output may echo secrets read inside the session).
    /// </summary>
    public static string RedactForBake(string? input, bool redactResultFields = false)
    {
        if (string.IsNullOrEmpty(input)) return input!;

        var result = RedactSource(input);
        result = Url.Replace(result, "<url>");
        result = UncPath.Replace(result, "<path>");
        result = WindowsPath.Replace(result, "<path>");
        result = InventorFile.Replace(result, "<inventor_file>");
        if (redactResultFields)
            result = JsonResultField.Replace(result, "\"result\": \"<redacted>\"");
        return result;
    }

    /// <summary>Stable body hash for dedup/journal lookup — a correlation key, not a security hash.</summary>
    public static string HashBody(string? body)
    {
        body ??= string.Empty;
        using (var sha1 = SHA1.Create())
        {
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(body));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
