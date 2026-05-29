#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.ToolBaker;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// <c>apply_bake</c> — the add-in side of accepting a suggestion: validate + compile the candidate baked
/// tool from an apply request and echo back the canonical record fields the server then persists. Ported
/// from nwd-mcp.
/// </summary>
public sealed class ApplyBakeHandler : IInventorCommand
{
    public string Name => "apply_bake";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };
        try
        {
            var record = BakedToolRuntimeCommandFactory.FromApplyRequest(p);
            var data = new JObject
            {
                ["success"] = true,
                ["tool_name"] = record.Name,
                ["description"] = record.Description,
                ["params_schema"] = record.ParamsSchema,
                ["source_code"] = record.SourceCode
            };
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
        catch (Exception ex)
        {
            var data = new JObject
            {
                ["success"] = false,
                ["error_code"] = "INVALID_ARGUMENT",
                ["message"] = ex.Message
            };
            return InventorCommandResult.Success(Guid.Empty, data, meta);
        }
    }
}
#endif
