using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.SheetFabrication;

/// <summary>Runs thickness recovery before returning control to a caller's batch.</summary>
internal static class SheetFabricationThicknessRecovery
{
    internal static InventorCommandResult Fail(InventorCommandContext ctx, Exception error, Action? restore)
    {
        var message = error.Message;
        var rollbackFailed = false;
        try { restore?.Invoke(); }
        catch (Exception rollback)
        {
            rollbackFailed = true;
            message += " | thickness rollback failed: " + rollback.Message;
        }
        var result = HandlerBase.FailForSupport(ctx, InventorErrorCodes.API_ERROR, message);
        result.Data = new JObject
        {
            ["rolled_back"] = restore is not null && !rollbackFailed,
            ["mutation_applied"] = rollbackFailed ? new JValue("unknown") : new JValue(false),
            ["rollback_failed"] = rollbackFailed,
        };
        return result;
    }
}
