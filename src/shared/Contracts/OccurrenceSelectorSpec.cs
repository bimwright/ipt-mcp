using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Shared occurrence selector (S3.2) used by the bulk assembly tools. Parsed and validated
/// host-free; the add-in walks the assembly and asks <see cref="Matches"/> per occurrence.
///
/// <code>{ names?: [glob], regex?, file?: glob, path_contains?, leaf?: false, max_depth?,
///   include_suppressed?: false, limit? }</code>
/// A bare string or string array is shorthand for <c>names</c>. Globs use <c>*</c>/<c>?</c>,
/// case-insensitive; a name glob containing '/' matches the occurrence path ("SUB:1/PART:2"),
/// otherwise the occurrence name. <c>file</c> matches the referenced file name (or the full path
/// when the glob contains a separator). Criteria combine with AND; names entries with OR. No
/// criteria at all selects every occurrence (still bounded by leaf/max_depth/limit).
/// </summary>
public sealed class OccurrenceSelectorSpec
{
    public const int DefaultLimit = 1000;
    public const int MaxLimit = 20000;

    public IReadOnlyList<string> Names { get; private set; } = Array.Empty<string>();
    public string? RegexPattern { get; private set; }
    public string? File { get; private set; }
    public string? PathContains { get; private set; }
    public bool Leaf { get; private set; }
    public int? MaxDepth { get; private set; }
    public bool IncludeSuppressed { get; private set; }
    public int Limit { get; private set; } = DefaultLimit;

    private Regex[] _nameRx = Array.Empty<Regex>();
    private bool[] _namePath = Array.Empty<bool>();
    private Regex? _rx;
    private Regex? _fileRx;
    private bool _filePath;

    /// <summary>Parses a selector token; null/missing = select all. <paramref name="field"/> prefixes errors.</summary>
    public static bool TryParse(JToken? token, string field, out OccurrenceSelectorSpec spec, out string? error, int? defaultLimit = null)
    {
        spec = new OccurrenceSelectorSpec();
        if (defaultLimit is { } dl) spec.Limit = dl;
        error = null;
        if (token is null || token.Type == JTokenType.Null) return spec.Compile(field, out error);

        if (token.Type == JTokenType.String)
        {
            spec.Names = new[] { (string)token! };
            return spec.Compile(field, out error);
        }
        if (token is JArray bare)
        {
            if (!TryStrings(bare, out var names)) { error = field + " must be a selector object, a name, or an array of names"; return false; }
            spec.Names = names;
            return spec.Compile(field, out error);
        }
        if (token is not JObject o)
        {
            error = field + " must be a selector object {names?, regex?, file?, path_contains?, leaf?, max_depth?, include_suppressed?, limit?}";
            return false;
        }

        var known = new HashSet<string>(StringComparer.Ordinal) { "names", "regex", "file", "path_contains", "leaf", "max_depth", "include_suppressed", "limit" };
        var unknown = o.Properties().Select(p => p.Name).Where(n => !known.Contains(n)).ToList();
        if (unknown.Count > 0) { error = $"{field}: unknown key(s) {string.Join(", ", unknown)}; allowed: {string.Join(", ", known)}"; return false; }

        if (o["names"] is { Type: not JTokenType.Null } namesTok)
        {
            if (namesTok.Type == JTokenType.String) spec.Names = new[] { (string)namesTok! };
            else if (namesTok is JArray arr && TryStrings(arr, out var names)) spec.Names = names;
            else { error = field + ".names must be a string or an array of strings"; return false; }
        }
        if (!TryString(o, "regex", field, out var rx, out error)) return false;
        if (!TryString(o, "file", field, out var file, out error)) return false;
        if (!TryString(o, "path_contains", field, out var pc, out error)) return false;
        spec.RegexPattern = rx; spec.File = file; spec.PathContains = pc;
        if (!TryBool(o, "leaf", field, out var leaf, out error)) return false;
        if (!TryBool(o, "include_suppressed", field, out var sup, out error)) return false;
        spec.Leaf = leaf ?? false; spec.IncludeSuppressed = sup ?? false;
        if (!TryInt(o, "max_depth", field, 1, 64, out var md, out error)) return false;
        if (!TryInt(o, "limit", field, 1, MaxLimit, out var lim, out error)) return false;
        spec.MaxDepth = md;
        if (lim is { } l) spec.Limit = l;
        return spec.Compile(field, out error);
    }

    private bool Compile(string field, out string? error)
    {
        error = null;
        try
        {
            _nameRx = Names.Select(GlobToRegex).ToArray();
            _namePath = Names.Select(n => n.IndexOf('/') >= 0).ToArray();
            _rx = RegexPattern is null ? null : new Regex(RegexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            _fileRx = File is null ? null : GlobToRegex(File);
            _filePath = File is not null && (File.IndexOf('\\') >= 0 || File.IndexOf('/') >= 0);
        }
        catch (ArgumentException ex)
        {
            error = $"{field}.regex is not a valid regular expression: {ex.Message}";
            return false;
        }
        if (Names.Any(string.IsNullOrWhiteSpace)) { error = field + ".names entries must be non-empty"; return false; }
        return true;
    }

    public bool HasCriteria => Names.Count > 0 || RegexPattern != null || File != null || PathContains != null;

    /// <summary>Does one occurrence match? <paramref name="path"/> is "TOP:1/SUB:2" with '/' separators.</summary>
    public bool Matches(string name, string path, string? filePath, int depth, bool isLeaf, bool suppressed)
    {
        if (suppressed && !IncludeSuppressed) return false;
        if (Leaf && !isLeaf) return false;
        if (MaxDepth is { } md && depth > md) return false;
        if (_nameRx.Length > 0)
        {
            var any = false;
            for (var i = 0; i < _nameRx.Length && !any; i++)
                any = _nameRx[i].IsMatch(_namePath[i] ? path : name);
            if (!any) return false;
        }
        if (_rx != null && !_rx.IsMatch(path)) return false;
        if (_fileRx != null)
        {
            var f = (filePath ?? "").Replace('/', '\\');
            var target = _filePath ? f : f.Substring(f.LastIndexOf('\\') + 1);
            if (!_fileRx.IsMatch(target.Replace('\\', '/'))) return false;
        }
        if (PathContains != null && path.IndexOf(PathContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    /// <summary>A short human description for error messages.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Names.Count > 0) parts.Add("names=" + string.Join("|", Names));
        if (RegexPattern != null) parts.Add("regex=" + RegexPattern);
        if (File != null) parts.Add("file=" + File);
        if (PathContains != null) parts.Add("path_contains=" + PathContains);
        if (Leaf) parts.Add("leaf");
        if (MaxDepth != null) parts.Add("max_depth=" + MaxDepth);
        if (IncludeSuppressed) parts.Add("include_suppressed");
        return parts.Count == 0 ? "(all occurrences)" : string.Join(", ", parts);
    }

    /// <summary>Up to <paramref name="take"/> candidate names closest to the name globs (for "no match" errors).</summary>
    public IReadOnlyList<string> Suggest(IEnumerable<string> candidates, int take = 8)
    {
        var probe = (Names.FirstOrDefault() ?? RegexPattern ?? PathContains ?? File ?? "").Replace("*", "").Replace("?", "").ToLowerInvariant();
        if (probe.Length == 0) return candidates.Take(take).ToList();
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(c => (c, score: Score(probe, c.ToLowerInvariant())))
            .OrderBy(x => x.score).ThenBy(x => x.c, StringComparer.OrdinalIgnoreCase)
            .Take(take).Select(x => x.c).ToList();
    }

    // Substring hits first, then edit distance.
    private static int Score(string probe, string c)
        => c.Contains(probe) ? c.Length - probe.Length : 1000 + Levenshtein(probe, c);

    private static int Levenshtein(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var tmp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = tmp;
            }
        }
        return d[b.Length];
    }

    public static Regex GlobToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob.Replace('\\', '/'))
            .Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool TryStrings(JArray arr, out IReadOnlyList<string> values)
    {
        var list = new List<string>();
        foreach (var t in arr)
        {
            if (t.Type != JTokenType.String) { values = Array.Empty<string>(); return false; }
            list.Add((string)t!);
        }
        values = list;
        return true;
    }

    private static bool TryString(JObject o, string key, string field, out string? value, out string? error)
    {
        value = null; error = null;
        var t = o[key];
        if (t is null || t.Type == JTokenType.Null) return true;
        if (t.Type != JTokenType.String || string.IsNullOrEmpty((string?)t)) { error = $"{field}.{key} must be a non-empty string"; return false; }
        value = (string)t!;
        return true;
    }

    private static bool TryBool(JObject o, string key, string field, out bool? value, out string? error)
    {
        value = null; error = null;
        var t = o[key];
        if (t is null || t.Type == JTokenType.Null) return true;
        if (t.Type != JTokenType.Boolean) { error = $"{field}.{key} must be a boolean"; return false; }
        value = (bool)t;
        return true;
    }

    private static bool TryInt(JObject o, string key, string field, int min, int max, out int? value, out string? error)
    {
        value = null; error = null;
        var t = o[key];
        if (t is null || t.Type == JTokenType.Null) return true;
        if (t.Type != JTokenType.Integer || (long)t < min || (long)t > max) { error = $"{field}.{key} must be an integer {min}..{max}"; return false; }
        value = (int)t;
        return true;
    }
}
