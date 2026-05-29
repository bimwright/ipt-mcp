#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// Resolves the opaque string ids the MCP surface passes for model edges and sketch entities.
/// Phase 1 has no persistent reference-key id scheme, so ids are <b>1-based positional indices</b>
/// into the active document's collections (the convention shared by the rvt/nwd gateways):
/// <list type="bullet">
/// <item>An <c>edge_id</c> indexes the solid body's <c>Edges</c> collection. Accepted forms:
/// <c>"3"</c>, <c>"edge:3"</c>, or <c>"body:1/edge:3"</c> (body then edge, both 1-based).</item>
/// <item>An <c>entity_id</c> indexes a sketch's <c>SketchEntities</c> collection: <c>"5"</c> or
/// <c>"entity:5"</c>.</item>
/// </list>
/// These are stable for a given feature tree state and are what the read-only query tools (WS3-A)
/// report back, so an agent round-trips the same ids it received.
/// </summary>
internal static class EntityResolver
{
    /// <summary>Parse a 1-based index out of an id string, tolerating a <paramref name="prefix"/>: form.</summary>
    public static int ParseIndex(string id, string prefix)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("empty id");
        var s = id.Trim();
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s.Substring(slash + 1);
        var colon = s.IndexOf(':');
        if (colon >= 0)
        {
            var p = s.Substring(0, colon);
            if (!p.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"expected '{prefix}:' id, got '{id}'");
            s = s.Substring(colon + 1);
        }
        if (!int.TryParse(s, out var idx) || idx < 1)
            throw new ArgumentException($"id '{id}' is not a valid 1-based index");
        return idx;
    }

    /// <summary>Optional leading <c>body:N/</c> segment of an edge id (defaults to body 1).</summary>
    public static int ParseBodyIndex(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return 1;
        var s = id.Trim();
        var slash = s.IndexOf('/');
        if (slash < 0) return 1;
        var head = s.Substring(0, slash);
        var colon = head.IndexOf(':');
        if (colon < 0) return 1;
        if (!head.Substring(0, colon).Equals("body", StringComparison.OrdinalIgnoreCase)) return 1;
        return int.TryParse(head.Substring(colon + 1), out var b) && b >= 1 ? b : 1;
    }

    /// <summary>Resolve a model edge from a part component definition by positional id.</summary>
    public static Edge ResolveEdge(PartComponentDefinition def, string edgeId)
    {
        var bodies = def.SurfaceBodies;
        if (bodies.Count < 1)
            throw new ArgumentException("the part has no solid bodies to select edges from");
        var bodyIdx = ParseBodyIndex(edgeId);
        if (bodyIdx > bodies.Count)
            throw new ArgumentException($"body index {bodyIdx} out of range (1..{bodies.Count})");
        var edges = bodies[bodyIdx].Edges;
        var edgeIdx = ParseIndex(edgeId, "edge");
        if (edgeIdx > edges.Count)
            throw new ArgumentException($"edge index {edgeIdx} out of range (1..{edges.Count})");
        return edges[edgeIdx];
    }

    /// <summary>Resolve a sketch entity from a sketch by positional id.</summary>
    public static SketchEntity ResolveSketchEntity(PlanarSketch sketch, string entityId)
    {
        var ents = sketch.SketchEntities;
        var idx = ParseIndex(entityId, "entity");
        if (idx > ents.Count)
            throw new ArgumentException($"entity index {idx} out of range (1..{ents.Count})");
        return (SketchEntity)ents[idx];
    }

    /// <summary>Find a named planar sketch on the part definition, or null.</summary>
    public static PlanarSketch? FindSketch(PartComponentDefinition def, string name)
    {
        foreach (PlanarSketch s in def.Sketches)
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }
}
#endif
