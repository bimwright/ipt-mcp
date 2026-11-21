using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Advisory <c>helper_hint</c> for <c>send_code</c> (<c>script_helpers_v1</c>): names the <c>Ipt.*</c> helper for each
/// lookup the script writes by hand (a loop over <c>app.Documents</c>, occurrences, sheets, views, tables …). Only a
/// hint; the script runs unchanged. Host-free, so the server builds it from the code it sends.
/// </summary>
public static class SendCodeHelperHints
{
    /// <summary>Descriptor capability: the add-in imports <c>Ipt</c>, guards loops and cleans returned numbers.</summary>
    public const string Capability = "script_helpers_v1";

    /// <summary>
    /// Descriptor capability of add-ins that also have <c>Ipt.Api</c>, <c>Ipt.Box(Point, Point)</c> and
    /// <c>Bbox.ToArray()</c> / a <c>(double[])</c> cast of a <c>Bbox</c>. They report v1 as well; the gateway names these helpers only to a target that reports v2.
    /// </summary>
    public const string CapabilityV2 = "script_helpers_v2";

    internal const int MaxSuggestions = 3;

    // JsonElement.GetProperty("x") is ordinary code, so only the calls that list members count as probing the API.
    private static readonly Regex Reflection = new(@"\.Get(Properties|Methods|Members)\s*\(|\bSystem\.Reflection\b", RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Use)[] Rules =
    {
        (new Regex(@"\bin\s+app\.Documents\b|app\.Documents\s*\.\s*(Cast|OfType)\s*<", RegexOptions.Compiled),
            "Ipt.Doc(name|path) / Ipt.Docs(type) instead of looping app.Documents"),
        (new Regex(@"\bin\s+[\w\.\[\]""]*\.(AllLeafOccurrences|AllReferencedOccurrences|SubOccurrences|Occurrences)\b|\.(AllLeafOccurrences|SubOccurrences|Occurrences)\s*\.\s*(Cast|OfType)\s*<", RegexOptions.Compiled),
            "Ipt.Occs(asm, new { regex = \"...\" }) / Ipt.Occ(asm, name) / Ipt.Leaves(asm) instead of looping occurrences"),
        (new Regex(@"\bin\s+[\w\.\[\]""]*\.Sheets\b|\.Sheets\s*\.\s*(Cast|OfType)\s*<", RegexOptions.Compiled),
            "Ipt.Sheet(dwg, name|index) instead of looping Sheets"),
        (new Regex(@"\bin\s+[\w\.\[\]""]*\.DrawingViews\b|\.DrawingViews\s*\.\s*(Cast|OfType)\s*<", RegexOptions.Compiled),
            "Ipt.View(dwgOrSheet, name) / Ipt.Views(dwgOrSheet) instead of looping DrawingViews"),
        (new Regex(@"\.CustomTables\b", RegexOptions.Compiled),
            "Ipt.Table(dwgOrSheet, title|index) / Ipt.TableText(table) for tables"),
        (new Regex(@"\.TextBoxes\b|\.GeneralNotes\b|\.LeaderNotes\b", RegexOptions.Compiled),
            "Ipt.Texts(dwgOrSheet) lists sketch texts (also inside views), general and leader notes with sheet, view and position"),
        (new Regex(@"\.RangeBox\b", RegexOptions.Compiled),
            "Ipt.BboxMm(occ|body|face|edge|doc) instead of RangeBox math"),
        (new Regex(@"\bPropertySets\b", RegexOptions.Compiled),
            "Ipt.Prop(doc, name) / Ipt.SetProp(doc, name, value) instead of walking PropertySets"),
    };

    private static readonly Regex Rounding = new(@"\bMath\.Round\s*\(", RegexOptions.Compiled);

    /// <summary>Null when the script hand-writes none of the known lookups.</summary>
    public static string? Match(string? code, bool rawNumbers = false, bool v2 = false)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var uses = new List<string>();
        if (v2 && Reflection.IsMatch(code!))
            uses.Add("Ipt.Api(\"TypeName\", \"filter\") lists a type's real members and signatures instead of reflection (which is blocked)");
        foreach (var (pattern, use) in Rules)
            if (pattern.IsMatch(code!)) uses.Add(use);
        if (!rawNumbers && Rounding.IsMatch(code!))
            uses.Add("no Math.Round for float noise: returned doubles are already cleaned (numbers: clean_v1)");
        if (uses.Count == 0) return null;
        return "Reuse Ipt.* helpers: " + string.Join("; ", uses.Take(MaxSuggestions)) +
               (uses.Count > MaxSuggestions ? $" (+{uses.Count - MaxSuggestions} more)" : "") +
               ". Return Ipt.Help() for every signature.";
    }
}
