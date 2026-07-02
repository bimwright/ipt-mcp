#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Handlers.Export;

public class ViewFitHandler : IInventorCommand
{
    public string Name => "view_fit";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}

public class SetViewOrientationHandler : IInventorCommand
{
    public string Name => "set_view_orientation";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}
#endif
