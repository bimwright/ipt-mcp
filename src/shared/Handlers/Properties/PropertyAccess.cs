#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Properties;

/// <summary>
/// Helpers for locating iProperty sets and properties by name without relying on the collection
/// indexers (which throw when the key is missing). Matching is case-insensitive, falls back to a
/// set's <c>InternalName</c>, and retries with the "Inventor " prefix so both "Summary Information"
/// and "Inventor Summary Information" resolve.
/// </summary>
internal static class PropertyAccess
{
    public static PropertySet? FindSet(global::Inventor.Document doc, string name)
    {
        var found = FindSetExact(doc, name);
        // "Inventor " prefix is optional: "Summary Information" aliases
        // "Inventor Summary Information" (found by F5 — the tool description's example
        // used the short name and a caller following it got 'set not found').
        if (found is null && !name.StartsWith("Inventor ", StringComparison.OrdinalIgnoreCase))
            found = FindSetExact(doc, "Inventor " + name);
        return found;
    }

    private static PropertySet? FindSetExact(global::Inventor.Document doc, string name)
    {
        foreach (PropertySet set in doc.PropertySets)
        {
            if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                return set;
            string? internalName = null;
            try { internalName = set.InternalName; } catch { /* not all sets expose it */ }
            if (!string.IsNullOrEmpty(internalName) && string.Equals(internalName, name, StringComparison.OrdinalIgnoreCase))
                return set;
        }
        return null;
    }

    public static Property? FindProperty(PropertySet set, string name)
    {
        foreach (Property prop in set)
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop;
            string? displayName = null;
            try { displayName = prop.DisplayName; } catch { /* optional */ }
            if (!string.IsNullOrEmpty(displayName) && string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase))
                return prop;
        }
        return null;
    }
}
#endif
