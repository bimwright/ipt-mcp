#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers;

internal static class ActiveDocumentSupport
{
    public static bool TryGetActivePart(
        InventorCommandContext ctx,
        string commandName,
        out Application app,
        out PartDocument part,
        out InventorCommandResult? failure)
    {
        app = (Application)ctx.Application!;
        part = null!;
        failure = null;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
            return false;
        }

        if (activeDoc is not PartDocument partDoc)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, commandName + " requires an active part document");
            return false;
        }

        part = partDoc;
        return true;
    }

    public static bool TryGetActiveAssembly(
        InventorCommandContext ctx,
        string commandName,
        out Application app,
        out AssemblyDocument assembly,
        out InventorCommandResult? failure)
    {
        app = (Application)ctx.Application!;
        assembly = null!;
        failure = null;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
            return false;
        }

        if (activeDoc is not AssemblyDocument assemblyDoc)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, commandName + " requires an active assembly document");
            return false;
        }

        assembly = assemblyDoc;
        return true;
    }
}
#endif
