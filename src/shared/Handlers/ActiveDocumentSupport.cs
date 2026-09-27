#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers;

internal static class ActiveDocumentSupport
{
    /// <summary>
    /// The command's target document: the optional <c>document</c> parameter (full path or name,
    /// resolved by <see cref="DocumentMatcher"/> among documents already in memory — never opened
    /// or activated), else the active document. Returns null with <paramref name="failure"/> set
    /// when the parameter does not resolve; returns null with no failure when there is simply no
    /// active document (callers keep their existing NO_DOCUMENT message).
    /// </summary>
    public static global::Inventor.Document? ResolveTarget(InventorCommandContext ctx, JObject? p, out InventorCommandResult? failure)
    {
        failure = null;
        var app = (Application)ctx.Application!;
        var query = p?["document"]?.Type == JTokenType.String ? (string?)p["document"] : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            try { return app.ActiveDocument; } catch { return null; }
        }

        var docs = new List<global::Inventor.Document>();
        var candidates = new List<DocumentMatcher.Candidate>();
        foreach (global::Inventor.Document d in app.Documents)
        {
            string? path = null, name = null;
            try { path = d.FullFileName; } catch { }
            try { name = d.DisplayName; } catch { }
            docs.Add(d);
            candidates.Add(new DocumentMatcher.Candidate(path, name));
        }
        if (!DocumentMatcher.TryMatch(query!, candidates, out var index, out var error))
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.INVALID_ARGUMENT, error!);
            return null;
        }
        return docs[index];
    }

    public static bool TryGetActivePart(
        InventorCommandContext ctx,
        string commandName,
        out Application app,
        out PartDocument part,
        out InventorCommandResult? failure)
        => TryGetPart(ctx, null, commandName, out app, out part, out failure);

    public static bool TryGetActiveAssembly(
        InventorCommandContext ctx,
        string commandName,
        out Application app,
        out AssemblyDocument assembly,
        out InventorCommandResult? failure)
        => TryGetAssembly(ctx, null, commandName, out app, out assembly, out failure);

    /// <summary>Part target: <c>p.document</c> when given, else the active document.</summary>
    public static bool TryGetPart(
        InventorCommandContext ctx,
        JObject? p,
        string commandName,
        out Application app,
        out PartDocument part,
        out InventorCommandResult? failure)
    {
        app = (Application)ctx.Application!;
        part = null!;

        var doc = ResolveTarget(ctx, p, out failure);
        if (failure != null) return false;
        if (doc is null)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
            return false;
        }

        if (doc is not PartDocument partDoc)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, commandName + " requires a part document");
            return false;
        }

        part = partDoc;
        return true;
    }

    /// <summary>Assembly target: <c>p.document</c> when given, else the active document.</summary>
    public static bool TryGetAssembly(
        InventorCommandContext ctx,
        JObject? p,
        string commandName,
        out Application app,
        out AssemblyDocument assembly,
        out InventorCommandResult? failure)
    {
        app = (Application)ctx.Application!;
        assembly = null!;

        var doc = ResolveTarget(ctx, p, out failure);
        if (failure != null) return false;
        if (doc is null)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
            return false;
        }

        if (doc is not AssemblyDocument assemblyDoc)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, commandName + " requires an assembly document");
            return false;
        }

        assembly = assemblyDoc;
        return true;
    }
}
#endif
