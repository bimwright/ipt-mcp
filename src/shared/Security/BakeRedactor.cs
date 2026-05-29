using System.Text.RegularExpressions;

namespace Bimwright.Inventor.Shared.Security;

/// <summary>
/// Redacts secrets out of baked-tool source before it is persisted to the server registry.
/// Masks assignment-style credentials and then runs <see cref="SecretMasker"/>. Ported from nwd-mcp.
/// </summary>
public static class BakeRedactor
{
    private static readonly Regex AssignmentSecret = new Regex(
        @"(?i)\b(api[_-]?key|auth[_-]?token|password|secret|token)\b\s*=\s*[""'][^""']+[""']",
        RegexOptions.Compiled);

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
}
