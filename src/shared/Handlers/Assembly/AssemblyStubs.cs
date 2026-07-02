#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public class CheckInterferenceHandler : IInventorCommand
{
    public string Name => "check_interference";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}

public class MeasureMinDistanceHandler : IInventorCommand
{
    public string Name => "measure_min_distance";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}

public class GetAssemblyBomHandler : IInventorCommand
{
    public string Name => "get_assembly_bom";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}
#endif
