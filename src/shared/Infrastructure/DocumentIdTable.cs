using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Lifetime ids for open Inventor documents (family contract FC-DOC-01). The key is the document's
/// InternalName plus its full file name (<see cref="Key"/>); <see cref="Forget"/> runs when Inventor
/// closes it, so reopening the file gets a new id.
/// </summary>
public sealed class DocumentIdTable
{
    public static DocumentIdTable Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);

    /// <summary>
    /// InternalName alone is not unique: a file copied outside Inventor (a checkpoint restore, an Explorer copy)
    /// keeps it, and both copies can be open at once. The path tells them apart. Save-As changes the key, so
    /// save_document repins (DocumentTargetPolicy.Repin).
    /// </summary>
    public static string Key(string internalName, string? fullFileName)
        => internalName + "|" + (fullFileName ?? "").ToUpperInvariant();

    public string IdFor(string key)
    {
        lock (_gate)
        {
            if (!_ids.TryGetValue(key, out var id))
                _ids[key] = id = "doc-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            return id;
        }
    }

    /// <summary>True while the document that got <paramref name="id"/> is open (its id is dropped on close).</summary>
    public bool IsOpen(string id)
    {
        lock (_gate) return _ids.ContainsValue(id);
    }

    public void Forget(string key)
    {
        lock (_gate) _ids.Remove(key);
    }
}
