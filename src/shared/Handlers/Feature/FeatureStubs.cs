#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

public class HoleHandler : IInventorCommand
{
    public string Name => "hole";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}

public class CircularPatternHandler : IInventorCommand
{
    public string Name => "circular_pattern";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}

public class RectangularPatternHandler : IInventorCommand
{
    public string Name => "rectangular_pattern";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
        => InventorCommandResult.Fail(System.Guid.Empty, "NOT_IMPLEMENTED", "Command not implemented yet", new InventorResponseMeta());
}
#endif
