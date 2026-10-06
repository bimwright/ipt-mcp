using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Preserve partial effects while recording failed drawing outcomes truthfully.</summary>
public static class DrawingResponsePolicy
{
    /// <summary>Keep partial effects while making failed drawing outcomes visible to journals/history.</summary>
    public static void NormalizeOutcome(InventorCommandResult result)
    {
        if (result.Data is not JObject data || data.Value<bool?>("ok") != false) return;
        result.Ok = false;
        result.Error = new InventorError
        {
            Code = data["error"] is JObject details ? (string?)details["code"] ?? InventorErrorCodes.API_ERROR : InventorErrorCodes.API_ERROR,
            Message = data["error"] is JObject error ? (string?)error["message"] ?? "Drawing operation failed."
                : (string?)data["error"] ?? "Drawing operation failed."
        };
    }
}
