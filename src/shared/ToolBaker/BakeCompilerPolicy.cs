using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.ToolBaker;

public sealed class BakePolicyResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Source-level banned-API gate for both <c>send_code</c> snippets and baked-tool source
/// (spec F2-c). The scan is token-aware rather than a raw substring match:
/// comments and string/char literals are stripped first (interpolation holes — including
/// raw-string holes — are still scanned as code), and tokens match on word
/// boundaries, case-sensitively — C# itself is case-sensitive, so a banned API is only
/// reachable under its exact casing and loose matching only produced false positives
/// (e.g. <c>VisibleSocket</c>, a lowercase <c>file.</c> variable).
///
/// Type-metadata reads (<c>typeof</c>, <c>GetType</c>) are allowed; reflection that would
/// *invoke or load* code (GetMethod/GetProperty/…, Invoke/DynamicInvoke/BeginInvoke,
/// Activator, Assembly.Load, CreateDelegate) stays blocked.
///
/// Pure path-string helpers are allowed in exactly one shape: a fully-qualified call
/// <c>System.IO.Path.{GetFileName, GetFileNameWithoutExtension, GetExtension, GetDirectoryName,
/// Combine, ChangeExtension}(…)</c>. They only transform strings; anything that touches the file
/// system (<c>File.</c>, <c>Directory.</c>, <c>GetTempFileName</c>, <c>GetFullPath</c>, …),
/// <c>using System.IO;</c> and aliases stay blocked.
///
/// Best-effort gate, not a sandbox: Inventor-API file writes
/// (SaveAs/SaveCopyAs/translators) are intentionally not restricted (spec F2-d).
/// </summary>
public static class BakeCompilerPolicy
{
    // (token reported in the error message, regex matched against the stripped source)
    private static readonly (string Token, string Pattern)[] Forbidden =
    {
        ("System.IO",          @"\bSystem\s*\.\s*IO\b"),
        ("System.Net",         @"\bSystem\s*\.\s*Net\b"),
        ("System.Diagnostics", @"\bSystem\s*\.\s*Diagnostics\b"),
        ("System.Reflection",  @"\bSystem\s*\.\s*Reflection\b"),
        ("File.",              @"\bFile\s*\."),
        ("Directory.",         @"\bDirectory\s*\."),
        ("Process",            @"\bProcess\b"),
        ("Environment.",       @"\bEnvironment\s*\."),
        ("Microsoft.Win32",    @"\bMicrosoft\s*\.\s*Win32\b"),
        ("Activator.",         @"\bActivator\s*\."),
        ("Assembly.Load",      @"\bAssembly\s*\.\s*Load"),   // Load/LoadFrom/LoadFile/…
        ("MethodInfo",         @"\bMethodInfo\b"),
        ("PropertyInfo",       @"\bPropertyInfo\b"),
        ("FieldInfo",          @"\bFieldInfo\b"),
        ("GetMethod(",         @"\bGetMethods?\s*\("),
        ("GetProperty(",       @"\bGetPropert(?:y|ies)\s*\("),
        ("GetField(",          @"\bGetFields?\s*\("),
        ("GetMember(",         @"\bGetMembers?\s*\("),
        ("GetEvent(",          @"\bGetEvents?\s*\("),
        ("GetConstructor(",    @"\bGetConstructors?\s*\("),
        ("Invoke(",            @"\bInvoke\s*\("),
        ("DynamicInvoke(",     @"\bDynamicInvoke\s*\("),
        ("BeginInvoke(",       @"\bBeginInvoke\s*\("),
        ("EndInvoke(",         @"\bEndInvoke\s*\("),
        ("CreateDelegate(",    @"\bCreateDelegate\s*\("),
        ("Socket",             @"\bSocket\b"),
        ("HttpClient",         @"\bHttpClient\b"),
        ("Bimwright.Ipt.Shared.ToolBaker", @"\bBimwright\s*\.\s*Ipt\s*\.\s*Shared\s*\.\s*ToolBaker\b"),
    };

    // Declared before Hints: static initializers run in textual order.
    internal static readonly string[] AllowedPathMethods =
    {
        "GetFileNameWithoutExtension", "GetFileName", "GetExtension", "GetDirectoryName", "Combine", "ChangeExtension",
    };

    // Extra guidance appended to the rejection for tokens agents commonly hit by accident.
    private static readonly System.Collections.Generic.Dictionary<string, string> Hints = new()
    {
        ["File."] = " (System.IO file access is blocked. If you meant Inventor's Document.File, read referenced "
                  + "file paths with inventor_get_document_info(references=true) instead.)",
        // Spelled with a bracket so the long method name survives SecretMasker on the way out.
        ["System.IO"] = " (only fully-qualified System.IO.Path.GetFileName[WithoutExtension] / GetExtension / "
                      + "GetDirectoryName / Combine / ChangeExtension(…) string helpers are allowed; no `using System.IO;` or aliases.)",
    };

    // Fully-qualified pure Path call → neutral token before the forbidden scan. The look-behind
    // stops `Foo.System.IO.Path…` from matching; the trailing `(` requires an actual call (a
    // method group or `using static` stays blocked).
    private static readonly Regex AllowedPathCall = new(
        @"(?<![\w.])(?:global\s*::\s*)?System\s*\.\s*IO\s*\.\s*Path\s*\.\s*(?:"
        + string.Join("|", AllowedPathMethods) + @")\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex[] ForbiddenPatterns = BuildPatterns();

    private static Regex[] BuildPatterns()
    {
        var patterns = new Regex[Forbidden.Length];
        for (var i = 0; i < Forbidden.Length; i++)
            patterns[i] = new Regex(Forbidden[i].Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        return patterns;
    }

    /// <summary>
    /// <paramref name="contextLabel"/> prefixes the error ("Baked tool" keeps the historical
    /// message for ToolBaker paths; send_code passes "send_code").
    /// </summary>
    public static BakePolicyResult ValidateSource(string source, string contextLabel = "Baked tool")
    {
        var stripped = AllowedPathCall.Replace(StripLiteralsAndComments(source ?? string.Empty), "__AllowedPathCall(");
        for (var i = 0; i < ForbiddenPatterns.Length; i++)
        {
            if (ForbiddenPatterns[i].IsMatch(stripped))
            {
                return new BakePolicyResult
                {
                    Ok = false,
                    Error = contextLabel + " source uses forbidden token: " + Forbidden[i].Token
                            + (Hints.TryGetValue(Forbidden[i].Token, out var hint) ? hint : "")
                };
            }
        }

        return new BakePolicyResult { Ok = true };
    }

    /// <summary>
    /// Returns <paramref name="source"/> with comments and string/char literal contents removed
    /// (their characters are not copied). Interpolation holes in non-raw interpolated strings
    /// are still scanned as code, recursively.
    /// </summary>
    internal static string StripLiteralsAndComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;
        ScanCode(source, sb, ref i, holeCloseBraces: 0);
        return sb.ToString();
    }

    // Scans code, copying non-literal characters. When holeCloseBraces > 0 this is the body of
    // an interpolation hole: returns just past the closing brace run of that length.
    private static void ScanCode(string s, StringBuilder sb, ref int i, int holeCloseBraces)
    {
        var n = s.Length;
        var braceDepth = 0;
        while (i < n)
        {
            var c = s[i];

            if (c == '/' && i + 1 < n && s[i + 1] == '/')
            {
                i += 2;
                while (i < n && s[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                i += 2;
                while (i < n && !(s[i] == '*' && i + 1 < n && s[i + 1] == '/')) i++;
                i = Math.Min(i + 2, n);
                continue;
            }
            if (c == '\'')
            {
                i++;
                while (i < n && s[i] != '\'') i += (s[i] == '\\' ? 2 : 1);
                i = Math.Min(i + 1, n);
                continue;
            }
            if (c == '{')
            {
                sb.Append(c);
                i++;
                braceDepth++;
                continue;
            }
            if (c == '}')
            {
                if (holeCloseBraces > 0 && braceDepth == 0)
                {
                    var closeRun = 1;
                    while (i + closeRun < n && s[i + closeRun] == '}') closeRun++;
                    if (closeRun >= holeCloseBraces) { i += holeCloseBraces; return; }
                }
                sb.Append(c);
                i++;
                if (braceDepth > 0) braceDepth--;
                continue;
            }
            if (c == '$' || c == '@' || c == '"')
            {
                // Possible literal prefix: any mix of '$' (interpolated) and '@' (verbatim),
                // then a quote run (>=3 = raw string).
                var j = i;
                var dollars = 0;
                var verbatim = false;
                while (j < n && (s[j] == '$' || s[j] == '@'))
                {
                    if (s[j] == '$') dollars++; else verbatim = true;
                    j++;
                }
                if (j < n && s[j] == '"')
                {
                    var quotes = 0;
                    while (j + quotes < n && s[j + quotes] == '"') quotes++;
                    if (quotes >= 3)
                    {
                        i = j + quotes;
                        SkipRawString(s, sb, ref i, quotes, dollars);
                    }
                    else
                    {
                        i = j + 1;   // one quote opens a delimited literal ("" is just an empty one)
                        SkipDelimitedString(s, sb, ref i, verbatim, dollars);
                    }
                    continue;
                }
                // Not a literal (e.g. an @-escaped identifier) — emit the prefix run as code.
                while (i < j) { sb.Append(s[i]); i++; }
                continue;
            }

            sb.Append(c);
            i++;
        }
    }

    // Regular/verbatim/interpolated literal (single-quote delimiters). Body is skipped except
    // interpolation holes (single '{' … '}'; '{{'/'}}' are escapes), which are scanned as code.
    private static void SkipDelimitedString(string s, StringBuilder sb, ref int i, bool verbatim, int dollars)
    {
        var n = s.Length;
        while (i < n)
        {
            var c = s[i];
            if (!verbatim && c == '\\') { i = Math.Min(i + 2, n); continue; }
            if (verbatim && c == '"' && i + 1 < n && s[i + 1] == '"') { i += 2; continue; }
            if (c == '"') { i++; return; }
            if (dollars > 0 && c == '{')
            {
                if (i + 1 < n && s[i + 1] == '{') { i += 2; continue; }
                i++;
                ScanCode(s, sb, ref i, holeCloseBraces: 1);
                continue;
            }
            i++;
        }
    }

    // Raw literal (3+ quote delimiters). A run of >= quotes closes it. With >=1 '$' prefix a run
    // of >= dollars '{' opens an interpolation hole, closed by the same number of '}'.
    private static void SkipRawString(string s, StringBuilder sb, ref int i, int quotes, int dollars)
    {
        var n = s.Length;
        while (i < n)
        {
            var c = s[i];
            if (c == '"')
            {
                var run = 1;
                while (i + run < n && s[i + run] == '"') run++;
                i += run;
                if (run >= quotes) return;
                continue;
            }
            if (dollars > 0 && c == '{')
            {
                var run = 1;
                while (i + run < n && s[i + run] == '{') run++;
                i += run;
                if (run >= dollars) ScanCode(s, sb, ref i, holeCloseBraces: run);
                continue;
            }
            i++;
        }
    }
}
