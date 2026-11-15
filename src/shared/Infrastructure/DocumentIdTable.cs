using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Lifetime ids for open Inventor documents (family contract FC-DOC-01). The key is the document's
/// InternalName; <see cref="Forget"/> runs when Inventor closes it, so reopening the file gets a new id.
/// </summary>
public sealed class DocumentIdTable
{
    public static DocumentIdTable Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);

    public string IdFor(string key)
    {
        lock (_gate)
        {
            if (!_ids.TryGetValue(key, out var id))
                _ids[key] = id = "doc-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            return id;
        }
    }

    public void Forget(string key)
    {
        lock (_gate) _ids.Remove(key);
    }
}
