using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Host-free name resolution for drawing sheets and sheet items. A refusal lists what does exist,
/// so the caller can pick a valid name on the next call instead of guessing again.
/// </summary>
public static class DrawingNameResolver
{
    private const int MaxListed = 20;

    /// <summary>
    /// Sheet by exact name or code; otherwise by 1-based index ("1") or by name without Inventor's ":n" suffix ("Sheet").
    /// </summary>
    public static int Sheet(IReadOnlyList<(string Name, string? Code)> sheets, string request)
    {
        var exact = Matches(sheets.Count, i => sheets[i].Name == request || sheets[i].Code == request);
        if (exact.Length == 1) return exact[0];
        if (exact.Length == 0)
        {
            if (int.TryParse(request, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= sheets.Count) return n - 1;
            var bare = Matches(sheets.Count, i => WithoutIndex(sheets[i].Name) == request);
            if (bare.Length == 1) return bare[0];
        }
        throw new ArgumentException("Sheet must resolve uniquely: " + request + ". Pass a sheet name, its code or a 1-based index. Sheets: " +
            List(sheets.Select((s, i) => (i + 1) + "=" + s.Name + (s.Code is null ? "" : " (code " + s.Code + ")"))));
    }

    /// <summary>
    /// Item of one kind by its exact managed name. With <paramref name="title"/> (edit_drawing_table only), a table also
    /// matches by its title (case-insensitive, trimmed) when no managed name matches; titles are read only then, and a
    /// title getter that throws counts as no title. A refusal lists the candidates and names only what the caller accepts.
    /// </summary>
    public static int Item(IReadOnlyList<string?> names, string kind, string request, Func<int, string?>? title = null)
    {
        var byName = Matches(names.Count, i => names[i] == request);
        if (byName.Length == 1) return byName[0];
        if (names.Count == 0)
            throw new ArgumentException("Drawing target must resolve uniquely on the selected sheet: no " + kind + " exists on it. Check the sheet parameter.");
        string?[]? titles = null;
        var found = byName.Length;
        if (title != null && byName.Length == 0)
        {
            titles = Enumerable.Range(0, names.Count).Select(i => SafeTitle(title, i)).ToArray();
            var byTitle = Matches(names.Count, i => titles[i] is { } t && string.Equals(t.Trim(), request.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byTitle.Length == 1) return byTitle[0];
            found = byTitle.Length;
        }
        var prefix = "Drawing target must resolve uniquely on the selected sheet: " + found + " " + kind + " match '" + request + "'. Candidates: ";
        if (titles == null)
            throw new ArgumentException(prefix + List(names.Select(n => n is null ? "(unnamed)" : "name=" + n)) +
                ". Pass the exact managed name, or the item's locator from inventor_get_drawing_info(include=items).");
        throw new ArgumentException(prefix + List(names.Select((n, i) => n is null && titles[i] is null ? "(unnamed)"
                : (n is null ? "" : "name=" + n) + (n is not null && titles[i] is not null ? " " : "") + (titles[i] is null ? "" : "title=" + titles[i]))) +
            ". For an untitled or same-titled " + kind + ", pass locator (from inventor_get_drawing_info(include=items)) instead of name.");
    }

    private static string? SafeTitle(Func<int, string?> title, int index)
    {
        try { return title(index); }
        catch (Exception) { return null; }   // a COM title getter can throw; treat it as no title
    }

    private static string WithoutIndex(string name)
    {
        var colon = name.LastIndexOf(':');
        return colon > 0 && name.Substring(colon + 1).All(char.IsDigit) && colon < name.Length - 1 ? name.Substring(0, colon) : name;
    }

    private static int[] Matches(int count, Func<int, bool> match) => Enumerable.Range(0, count).Where(match).ToArray();

    private static string List(IEnumerable<string> values)
    {
        var all = values.ToArray();
        return string.Join("; ", all.Take(MaxListed)) + (all.Length > MaxListed ? "; … (" + (all.Length - MaxListed) + " more)" : "");
    }
}
