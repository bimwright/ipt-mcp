#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// <c>health</c> — read-only liveness probe. Reports the add-in year, process id, and whether an
/// active document is open (with its document type). STA-bound: casts <see cref="InventorCommandContext.Application"/>
/// to <c>Inventor.Application</c>.
/// </summary>
public sealed class HealthHandler : IInventorCommand
{
    public string Name => "health";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; }
        catch { doc = null; }

        var data = new JObject
        {
            ["inventor_year"] = ctx.InventorYear,
            ["process_id"] = System.Diagnostics.Process.GetCurrentProcess().Id,
            ["has_active_document"] = doc != null,
            ["document_type"] = doc != null ? doc.DocumentType.ToString() : null,
        };
        return InventorCommandResult.Success(Guid.Empty, data, meta);
    }
}
#endif
