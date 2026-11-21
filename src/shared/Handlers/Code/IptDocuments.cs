#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;

namespace Bimwright.Ipt.Scripting;

public static partial class Ipt
{
    /// <summary>The active document (throws when none is open).</summary>
    public static Document Active => App.ActiveDocument ?? throw new IptException("No document is active in Inventor.");

    /// <summary>Every document loaded in this Inventor session (including invisible referenced ones), optionally of one type.</summary>
    public static List<Document> Docs(DocumentTypeEnum? type = null)
    {
        var list = new List<Document>();
        foreach (Document d in App.Documents)
        {
            IptGuard.Check("Ipt.Docs", 0);
            if (type == null || d.DocumentType == type) list.Add(d);
        }
        return list;
    }

    /// <summary>
    /// A loaded document by full path, file name (with or without extension), display name, or a unique part of its
    /// path — tried in that order. Never opens a file (see <see cref="Open"/>). Throws when nothing or several match.
    /// </summary>
    public static Document Doc(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) throw new IptException("Doc needs a file name or path.");
        var key = nameOrPath.Trim().Replace('/', '\\');
        var docs = Docs();
        var stages = new Func<Document, bool>[]
        {
            d => string.Equals(Path(d), key, StringComparison.OrdinalIgnoreCase),
            d => string.Equals(FileName(d), key, StringComparison.OrdinalIgnoreCase),
            d => string.Equals(System.IO.Path.GetFileNameWithoutExtension(Path(d)), key, StringComparison.OrdinalIgnoreCase),
            d => string.Equals(Safe(() => d.DisplayName), key, StringComparison.OrdinalIgnoreCase),
            d => Path(d).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0,
        };
        foreach (var stage in stages)
        {
            var hits = docs.Where(stage).ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1)
                throw new IptException($"{hits.Count} loaded documents match '{nameOrPath}': {List(hits.Select(Path))}. Pass the full path.");
        }
        throw new IptException($"No loaded document matches '{nameOrPath}'. Loaded: {List(docs.Select(FileName))}. " +
                               "Use Ipt.Open(path) to open a file.");
    }

    /// <summary>The document at <paramref name="path"/>: the loaded one if it is already open, otherwise opened (hidden by default).</summary>
    public static Document Open(string path, bool visible = false)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new IptException("Open needs a full file path.");
        var key = path.Trim().Replace('/', '\\');
        foreach (var d in Docs())
            if (string.Equals(Path(d), key, StringComparison.OrdinalIgnoreCase)) return d;
        try { return App.Documents.Open(key, visible); }
        catch (Exception ex) { throw new IptException($"Could not open '{key}': {ex.Message}"); }
    }

    /// <summary>A part: by name/path, from a Document, or the part an occurrence references. No argument = the active document.</summary>
    public static PartDocument Part(object? source = null) =>
        Resolve(source) is PartDocument p ? p : throw WrongType(source, "a part (.ipt)");

    /// <summary>An assembly: by name/path, from a Document, or the subassembly an occurrence references. No argument = the active document.</summary>
    public static AssemblyDocument Asm(object? source = null) =>
        Resolve(source) is AssemblyDocument a ? a : throw WrongType(source, "an assembly (.iam)");

    /// <summary>A drawing: by name/path or from a Document. No argument = the active document.</summary>
    public static DrawingDocument Dwg(object? source = null) =>
        Resolve(source) is DrawingDocument d ? d : throw WrongType(source, "a drawing (.idw/.dwg)");

    /// <summary>The document an occurrence's definition lives in (what <c>(Document)o.Definition.Document</c> means).</summary>
    public static Document DefDoc(ComponentOccurrence occurrence)
    {
        if (occurrence is null) throw new IptException("DefDoc needs an occurrence.");
        try { return (Document)occurrence.Definition.Document; }
        catch (Exception ex) { throw new IptException($"Occurrence '{Safe(() => occurrence.Name)}' has no loaded definition ({ex.Message})."); }
    }

    /// <summary>The file name (with extension) of a document or of the file an occurrence references.</summary>
    public static string FileName(object source) => System.IO.Path.GetFileName(source switch
    {
        ComponentOccurrence o => Path(DefDoc(o)),
        Document d => Path(d),
        _ => Path(Resolve(source)),
    });

    /// <summary>The component definition of a part or assembly (PartComponentDefinition / AssemblyComponentDefinition).</summary>
    public static object Def(object? source = null) => Resolve(source) switch
    {
        PartDocument p => p.ComponentDefinition,
        AssemblyDocument a => a.ComponentDefinition,
        var d => throw new IptException($"{Describe(d)} has no component definition (it is not a part or assembly)."),
    };

    // ---- internals ---------------------------------------------------------------------------------------------

    internal static Document Resolve(object? source) => source switch
    {
        null => Active,
        Document d => d,
        string s => Doc(s),
        ComponentOccurrence o => DefDoc(o),
        PartComponentDefinition pd => (Document)pd.Document,
        AssemblyComponentDefinition ad => (Document)ad.Document,
        _ => throw new IptException($"expected a document name/path, Document or ComponentOccurrence, got {Describe(source)}."),
    };

    private static IptException WrongType(object? source, string expected)
    {
        var d = Resolve(source);
        return new IptException($"{Describe(d)} ({Safe(() => d.DocumentType.ToString())}) is not {expected}.");
    }

    internal static string Path(Document d) => Safe(() => d.FullFileName) ?? "";

    internal static T? Safe<T>(Func<T> read)
    {
        try { return read(); } catch { return default; }
    }
}
#endif
