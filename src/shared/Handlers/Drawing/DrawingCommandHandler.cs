using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

/// <summary>Uses the existing dispatcher/STA/result pipeline. Older hosts fail explicitly.</summary>
public sealed class DrawingCommandHandler : HandlerBase, IInventorCommand
{
    public string Name { get; }
    public bool IsReadOnly => Name == "get_drawing_info";
    public DrawingCommandHandler(string name) => Name = name;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly && !IsReadOnly) return Fail(ctx, InventorErrorCodes.READ_ONLY, "Drawing writes are disabled in read-only mode.");
        try
        {
            p = DrawingInput.Normalize(Name, p);
            DrawingInput.Validate(Name, p);
#if INVENTOR2027
            return DrawingOperations.Execute(ctx, Name, p);
#else
            return Fail(ctx,InventorErrorCodes.UNSUPPORTED_HOST,"Drawing tools require the verified Inventor 2027 implementation.");
#endif
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (NotSupportedException ex) { return Fail(ctx, InventorErrorCodes.UNSUPPORTED_HOST, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
