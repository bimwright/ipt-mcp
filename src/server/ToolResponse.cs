using System.Text;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Server;

/// <summary>
/// Single exit point for every server-side tool payload (spec F3-d). Payloads at or under
/// <see cref="PrettyPrintMaxBytes"/> compact UTF-8 bytes stay pretty-printed for transcript
/// readability; larger ones ship unindented — indentation costs ~20-50% extra bytes/tokens
/// exactly where it is most expensive.
/// </summary>
internal static class ToolResponse
{
    internal const int PrettyPrintMaxBytes = 4 * 1024;

    internal static string Serialize(object data)
    {
        var compact = JsonConvert.SerializeObject(data, Formatting.None);
        return Encoding.UTF8.GetByteCount(compact) <= PrettyPrintMaxBytes
            ? JsonConvert.SerializeObject(data, Formatting.Indented)
            : compact;
    }

    internal static string Error(string code, string message)
        => Serialize(new { ok = false, error = new { code, message } });
}
