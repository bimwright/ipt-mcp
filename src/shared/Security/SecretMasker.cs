using System;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Security;

public static class SecretMasker
{
    /// <summary>
    /// Environment switch for the long-token heuristic (any 24+ character alphanumeric run is treated
    /// as a possible secret). On by default; <c>0</c>/<c>false</c>/<c>off</c>/<c>no</c> turns it off on a
    /// machine where long identifiers (COM type names such as <c>ComponentOccurrencesEnumerator</c>,
    /// part names) must stay readable. Key-value credentials and Bearer tokens are always masked.
    /// Read by both the MCP server and the add-in process, so set it for both (user environment).
    /// </summary>
    public const string LongTokenEnvVar = "BIMWRIGHT_INVENTOR_MASK_LONG_TOKENS";

    // "auth_token": "…", password="…", api_key: '…' — quoted key-value secrets in JSON or code.
    private static readonly Regex KeyValueSecret = new(
        "(?i)\\b((?:auth[_-]?token|access[_-]?token|refresh[_-]?token|id[_-]?token|api[_-]?key|apikey|password|passwd|client[_-]?secret|authorization)[\"']?\\s*[:=]\\s*[\"'])([^\"']*)([\"'])",
        RegexOptions.Compiled);
    // Authorization: Bearer <token> — require 8+ token chars so "Bearer tokens" prose survives.
    private static readonly Regex BearerToken = new(
        "\\bBearer\\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongSecret = new("\\b[A-Za-z0-9+/=]{24,}\\b", RegexOptions.Compiled);

    /// <summary>Whether the long-token heuristic runs. Defaults from <see cref="LongTokenEnvVar"/>; tests may set it.</summary>
    public static bool MaskLongTokens { get; set; } = LongTokenMaskingFromEnvironment(Environment.GetEnvironmentVariable(LongTokenEnvVar));

    public static bool LongTokenMaskingFromEnvironment(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return !(v == "0" || v == "false" || v == "off" || v == "no");
    }

    public static string Mask(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        var s = KeyValueSecret.Replace(input, "$1***$3");
        s = BearerToken.Replace(s, "Bearer ***");
        if (MaskLongTokens) s = LongSecret.Replace(s, "***");
        return s;
    }
}
