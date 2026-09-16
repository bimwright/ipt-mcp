using System;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Security;

public static class SecretMasker
{
    // "auth_token": "…", password="…", api_key: '…' — quoted key-value secrets in JSON or code.
    private static readonly Regex KeyValueSecret = new(
        "(?i)\\b((?:auth[_-]?token|access[_-]?token|refresh[_-]?token|id[_-]?token|api[_-]?key|apikey|password|passwd|client[_-]?secret|authorization)[\"']?\\s*[:=]\\s*[\"'])([^\"']*)([\"'])",
        RegexOptions.Compiled);
    // Authorization: Bearer <token> — require 8+ token chars so "Bearer tokens" prose survives.
    private static readonly Regex BearerToken = new(
        "\\bBearer\\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongSecret = new("\\b[A-Za-z0-9+/=]{24,}\\b", RegexOptions.Compiled);

    public static string Mask(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        var s = KeyValueSecret.Replace(input, "$1***$3");
        s = BearerToken.Replace(s, "Bearer ***");
        s = LongSecret.Replace(s, "***");
        return s;
    }
}
