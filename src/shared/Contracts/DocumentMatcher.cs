using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Resolves the optional <c>document</c> tool parameter against the documents Inventor already has
/// in memory (open windows AND documents loaded as assembly references). Tiers, first hit wins:
/// full path → display name → file name → file name without extension → unique substring; all case-insensitive, with
/// '/' and '\' treated alike. A tier with several hits is ambiguous (error lists them); nothing is
/// opened or activated. API-agnostic so the rules are unit-tested without Inventor.
/// </summary>
public static class DocumentMatcher
{
    public readonly struct Candidate
    {
        public Candidate(string? fullPath, string? displayName) { FullPath = fullPath ?? ""; DisplayName = displayName ?? ""; }
        public string FullPath { get; }
        public string DisplayName { get; }
    }

    public static bool TryMatch(string query, IReadOnlyList<Candidate> docs, out int index, out string? error)
    {
        index = -1;
        error = null;
        var q = Normalize(query);
        if (q.Length == 0)
        {
            error = "document must be a full path or a document name";
            return false;
        }

        Func<Candidate, bool>[] tiers =
        {
            d => d.FullPath.Length > 0 && Normalize(d.FullPath) == q,
            d => Normalize(d.DisplayName) == q,
            d => FileName(d.FullPath) == q,
            d => StripExt(FileName(d.FullPath)) == q || StripExt(Normalize(d.DisplayName)) == q,
            // last resort: a unique substring of the file/display name (ambiguity is still an error)
            d => FileName(d.FullPath).Contains(q) || Normalize(d.DisplayName).Contains(q),
        };
        foreach (var tier in tiers)
        {
            var hits = Enumerable.Range(0, docs.Count).Where(i => tier(docs[i])).ToList();
            if (hits.Count == 1)
            {
                index = hits[0];
                return true;
            }
            if (hits.Count > 1)
            {
                error = $"document '{query}' is ambiguous ({hits.Count} matches): "
                        + string.Join("; ", hits.Take(10).Select(i => Describe(docs[i])))
                        + ". Pass the full path.";
                return false;
            }
        }

        var names = docs.Select(Describe).Take(25).ToList();
        error = $"document '{query}' is not open in Inventor (it is never opened automatically). "
                + (names.Count == 0 ? "No documents are open."
                    : "Open documents: " + string.Join("; ", names) + (docs.Count > names.Count ? $"; … {docs.Count - names.Count} more" : ""));
        return false;
    }

    // "folder\file.ipt" rather than the full path: error text is sanitized and drive-rooted paths
    // would collapse to "<path>"; the parent folder is usually enough to disambiguate.
    private static string Describe(Candidate d)
    {
        if (d.FullPath.Length == 0) return d.DisplayName;
        var parts = d.FullPath.Replace('/', '\\').Split('\\');
        return parts.Length >= 2 ? parts[parts.Length - 2] + "\\" + parts[parts.Length - 1] : d.FullPath;
    }

    private static string Normalize(string? s) => (s ?? "").Trim().Replace('/', '\\').ToLowerInvariant();

    private static string FileName(string? path)
    {
        var n = Normalize(path);
        var i = n.LastIndexOf('\\');
        return i >= 0 ? n.Substring(i + 1) : n;
    }

    private static string StripExt(string name)
    {
        var i = name.LastIndexOf('.');
        return i > 0 ? name.Substring(0, i) : name;
    }
}
