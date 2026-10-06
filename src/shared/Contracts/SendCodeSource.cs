using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Assembles a <c>send_code</c> submission from saved code modules plus the agent's script.
/// Every part is prefixed with a <c>#line 1 "&lt;source&gt;"</c> directive so compiler diagnostics
/// and runtime stack frames report <c>module:&lt;name&gt;</c> / <c>script</c> and the line inside
/// that part. Leading <c>using</c> directives are hoisted to the top (C# scripts only accept them
/// before any code) and blanked in place so line numbers do not shift. API-agnostic.
/// </summary>
public static class SendCodeSource
{
    public const string ScriptSource = "script";
    public const string ModulePrefix = "module:";

    public static readonly Regex ModuleNamePattern = new(@"^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant);

    // `using X.Y;`, `using static X.Y;`, `using A = X.Y;` — not `using (…)` / `using var`.
    private static readonly Regex UsingDirective = new(
        @"^\s*using\s+(?:static\s+)?(?:[A-Za-z_]\w*\s*=\s*)?[A-Za-z_][\w.]*(?:<[\w.,\s<>]*>)?\s*;\s*(?://.*)?$",
        RegexOptions.CultureInvariant);

    public sealed class Part
    {
        public Part(string source, string code) { Source = source; Code = code ?? ""; }
        public string Source { get; }
        public string Code { get; }
    }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 text (first 16 chars — enough for provenance).</summary>
    public static string Hash(string code)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(code ?? ""));
        var sb = new StringBuilder(32);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Modules in the given order, then the script.</summary>
    public static string Build(IReadOnlyList<Part> modules, string script)
    {
        var parts = new List<Part>(modules ?? Array.Empty<Part>()) { new Part(ScriptSource, script) };
        var head = new StringBuilder();
        var body = new StringBuilder();
        foreach (var part in parts)
        {
            var lines = SplitLines(part.Code);
            var inHeader = true;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!inHeader) break;
                var line = lines[i];
                if (UsingDirective.IsMatch(line))
                {
                    head.Append("#line ").Append(i + 1).Append(" \"").Append(part.Source).Append("\"\n");
                    head.Append(line.TrimEnd()).Append('\n');
                    lines[i] = "";
                }
                else if (!IsTrivia(line))
                {
                    inHeader = false;
                }
            }
            body.Append("#line 1 \"").Append(part.Source).Append("\"\n");
            foreach (var l in lines) body.Append(l).Append('\n');
        }
        return head.ToString() + body.ToString();
    }

    // Blank lines, // comments and #directives other than #line may precede usings.
    private static bool IsTrivia(string line)
    {
        var t = line.Trim();
        return t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal);
    }

    private static string[] SplitLines(string code)
        => code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static readonly Regex StackFrame = new(@" in (?<file>(?:script|module:[a-z0-9_]+)):line (?<line>\d+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// First stack frame that points into the script or a module (innermost first). Returns
    /// false when the runtime did not emit line info.
    /// </summary>
    public static bool TryGetFailingLocation(string? stackTrace, out string source, out int line)
    {
        source = "";
        line = 0;
        if (string.IsNullOrEmpty(stackTrace)) return false;
        var m = StackFrame.Match(stackTrace);
        if (!m.Success) return false;
        source = m.Groups["file"].Value;
        line = int.Parse(m.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>1-based line of <paramref name="code"/>, trimmed; null when out of range.</summary>
    public static string? LineText(string code, int line)
    {
        var lines = SplitLines(code ?? "");
        if (line < 1 || line > lines.Length) return null;
        var t = lines[line - 1].Trim();
        return t.Length > 200 ? t.Substring(0, 200) : t;
    }
}
