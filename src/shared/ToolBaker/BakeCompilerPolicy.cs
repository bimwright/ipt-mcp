using System;

namespace Bimwright.Ipt.Shared.ToolBaker;

public sealed class BakePolicyResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Source-level banned-API gate for both <c>send_code</c> snippets and baked-tool source.
/// Rejects destructive file ops, process spawning, environment mutation, external network access,
/// reflection, and any attempt to re-enter the ToolBaker layer. Ported verbatim from nwd-mcp's
/// <c>BakeCompilerPolicy</c> with the product namespace rename.
/// </summary>
public static class BakeCompilerPolicy
{
    private static readonly string[] ForbiddenTokens =
    {
        "System.IO",
        "System.Net",
        "System.Diagnostics",
        "System.Reflection",
        "File.",
        "Directory.",
        "Process.",
        "Environment.",
        "Microsoft.Win32",
        "Activator.",
        "Assembly.",
        "MethodInfo",
        "PropertyInfo",
        "FieldInfo",
        "GetType(",
        "typeof(",
        "Socket",
        "HttpClient",
        "Bimwright.Ipt.Shared.ToolBaker"
    };

    public static BakePolicyResult ValidateSource(string source)
    {
        source = source ?? string.Empty;
        foreach (var token in ForbiddenTokens)
        {
            if (source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new BakePolicyResult { Ok = false, Error = "Baked tool source uses forbidden token: " + token };
            }
        }

        return new BakePolicyResult { Ok = true };
    }
}
