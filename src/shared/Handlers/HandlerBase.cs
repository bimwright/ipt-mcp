using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// Convenience base for <see cref="IInventorCommand"/> handlers: concise <c>Ok</c>/<c>Fail</c>
/// result builders that stamp the response meta from the command context.
/// API-agnostic — handlers themselves cast <c>ctx.Application</c> to <c>Inventor.Application</c>.
/// (Id and duration are normalized by <see cref="CommandDispatcher"/> after Execute, so passing
/// <see cref="Guid.Empty"/> here is intentional.)
/// </summary>
public abstract class HandlerBase
{
    protected static InventorResponseMeta Meta(InventorCommandContext ctx) => new()
    {
        TargetId = ctx.TargetId,
        InventorYear = ctx.InventorYear == 0 ? (int?)null : ctx.InventorYear,
    };

    protected static InventorCommandResult Ok(InventorCommandContext ctx, JToken? data) =>
        InventorCommandResult.Success(Guid.Empty, data, Meta(ctx));

    protected static InventorCommandResult Fail(InventorCommandContext ctx, string code, string message) =>
        InventorCommandResult.Fail(Guid.Empty, code, message, Meta(ctx));

    internal static InventorCommandResult FailForSupport(InventorCommandContext ctx, string code, string message) =>
        InventorCommandResult.Fail(Guid.Empty, code, message, Meta(ctx));
}
