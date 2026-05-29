using System;

namespace Bimwright.Inventor.Shared.Contracts;

public static class ResponseSizeGuard
{
    public static bool Check(string serialized, int maxBytes, out InventorError? error)
    {
        var size = System.Text.Encoding.UTF8.GetByteCount(serialized);
        if (size <= maxBytes) { error = null; return true; }
        error = new InventorError
        {
            Code = "RESPONSE_TOO_LARGE",
            Message = $"Response {size} bytes exceeds the configured limit of {maxBytes} bytes. " +
                      "Narrow the query (max_items/max_depth) and retry."
        };
        return false;
    }
}
