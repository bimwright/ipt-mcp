using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>
    /// One-line summary for a call-history row, ported from rvt-mcp. Tool-specific cases
    /// keep the grid readable without opening the detail pane; everything else falls back
    /// to "N rows/items" or "OK".
    /// </summary>
    public static class SummaryGenerator
    {
        private const int MaxLength = 60;

        public static string Generate(string toolName, string? paramsJson, string? resultJson, bool success, string? error)
        {
            if (!success)
                return Truncate(error ?? "Failed", MaxLength);

            try
            {
                var result = !string.IsNullOrEmpty(resultJson) ? JObject.Parse(resultJson) : null;
                var parms = !string.IsNullOrEmpty(paramsJson) ? JObject.Parse(paramsJson) : null;

                switch (toolName)
                {
                    case "send_code":
                        return FormatSendCode(result);
                    case "list_open_documents":
                        return FormatCount(result, "documents");
                    case "capture_view":
                        return "view snapshot";
                    case "report_task_result":
                        return FormatTaskResult(result, parms);
                    default:
                        return FormatGeneric(toolName, result);
                }
            }
            catch
            {
                return success ? "OK" : "Failed";
            }
        }

        private static string FormatSendCode(JObject? result)
        {
            var text = result?.Value<string>("result") ?? result?.Value<string>("stdout");
            if (string.IsNullOrEmpty(text)) return "OK (no output)";
            var firstLine = text.Split('\n')[0];
            return Truncate(firstLine, MaxLength);
        }

        private static string FormatCount(JObject? result, string noun)
        {
            var count = result?.Value<int?>("count");
            return count.HasValue ? $"{count} {noun}" : "OK";
        }

        private static string FormatTaskResult(JObject? result, JObject? parms)
        {
            var summary = parms?.Value<string>("summary");
            if (!string.IsNullOrEmpty(summary)) return Truncate(summary, MaxLength);
            var outcome = parms?.Value<string>("outcome");
            return !string.IsNullOrEmpty(outcome) ? outcome! : "OK";
        }

        private static string FormatGeneric(string toolName, JObject? result)
        {
            var rowCount = result?.Value<int?>("rowCount");
            if (rowCount.HasValue) return $"{rowCount} rows";
            var count = result?.Value<int?>("count");
            if (count.HasValue) return $"{count} items";
            return "OK";
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }
    }
}
