#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

public sealed class ViewFitHandler : HandlerBase, IInventorCommand
{
    public string Name => "view_fit";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        var app = (Application)context.Application!;
        var activeView = app.ActiveView;

        if (activeView == null)
        {
            return Fail(context, "NO_DOCUMENT", "No active view to fit");
        }

        try
        {
            activeView.Fit();
            activeView.Update();
            return Ok(context, new JObject { ["fitted"] = true });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to fit active view: " + ex.Message);
        }
    }
}
#endif
