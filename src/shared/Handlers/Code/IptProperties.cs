#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;

namespace Bimwright.Ipt.Scripting;

/// <summary>iProperties by their display name, across every property set.</summary>
public static partial class Ipt
{
    private const string CustomSet = "Inventor User Defined Properties";

    /// <summary>
    /// An iProperty value by name ("Part Number", "Description", "Title", a custom property …) of a document
    /// (name/path/Document/occurrence, null = active). Standard sets are searched before custom properties.
    /// </summary>
    public static object? Prop(object? document, string name)
    {
        var property = FindProperty(Resolve(document), name, out var all)
                       ?? throw new IptException($"No iProperty '{name}'. Available: {List(all)}.");
        return property.Value;
    }

    /// <summary>
    /// Sets an iProperty. A missing name is an error unless <paramref name="createCustom"/>, which adds it as a custom
    /// property. Returns the value read back.
    /// </summary>
    public static object? SetProp(object? document, string name, object value, bool createCustom = false)
    {
        var doc = Resolve(document);
        var property = FindProperty(doc, name, out var all);
        if (property == null)
        {
            if (!createCustom) throw new IptException($"No iProperty '{name}' (pass createCustom: true to add it). Available: {List(all)}.");
            try { property = doc.PropertySets[CustomSet].Add(value, name); }
            catch (Exception ex) { throw new IptException($"Could not add custom iProperty '{name}': {ex.Message}"); }
            return property.Value;
        }
        try { property.Value = value; }
        catch (Exception ex) { throw new IptException($"Could not set iProperty '{name}': {ex.Message}"); }
        return property.Value;
    }

    private static Property? FindProperty(Document doc, string name, out List<string> all)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new IptException("iProperty name is required.");
        all = new List<string>();
        Property? custom = null;
        foreach (PropertySet set in doc.PropertySets)
        {
            var isCustom = set.Name == CustomSet;
            foreach (Property p in set)
            {
                var display = Safe(() => p.DisplayName) ?? "";
                var internalName = Safe(() => p.Name) ?? "";
                all.Add(string.IsNullOrEmpty(display) ? internalName : display);
                var hit = string.Equals(display, name, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(internalName, name, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
                if (!isCustom) return p;
                custom ??= p;
            }
        }
        return custom;
    }
}
#endif
