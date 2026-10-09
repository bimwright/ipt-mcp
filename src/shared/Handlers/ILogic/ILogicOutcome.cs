using System;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

internal static class ILogicOutcome
{
    internal static InventorCommandResult Complete(JObject data, InventorResponseMeta meta, int? status = null, string? error = null)
    {
        var failed = error is not null || status is not null and not 0
            || data["exceptions"] is JArray { Count: > 0 } || data["cleanup_error"] is not null
            || data["rule_deleted"]?.Value<bool?>() == false || data["result_property_restored"]?.Value<bool?>() == false;
        if (!failed) return InventorCommandResult.Success(Guid.Empty, data, meta);
        data["ran"] = false;
        if (data["auto_ran"] is not null) data["auto_ran"] = false;
        var result = InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.API_ERROR,
            error ?? (data["cleanup_error"] is not null ? "iLogic cleanup failed; inspect cleanup_error."
                : "iLogic execution failed; inspect result and exceptions."), meta);
        result.Data = data;
        return result;
    }
}
