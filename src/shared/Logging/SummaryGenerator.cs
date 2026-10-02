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
                    case "capture_sheet":
                        return "sheet snapshot";
                    case "new_drawing":
                        return Truncate("drawing " + result?.Value<string>("document"), MaxLength);
                    case "get_drawing_info":
                        return Truncate(result?.Value<string>("document") + " · " + result?["sheets"]?.Value<int>("total") + " sheets", MaxLength);
                    case "add_sheet": case "add_drawing_view": case "add_section_view": case "edit_drawing_view":
                        return Truncate(result?.Value<string>("name") ?? toolName, MaxLength);
                    case "set_title_block":
                        return Truncate("title block " + result?.Value<string>("title_block"), MaxLength);
                    case "add_drawing_note": case "add_drawing_table": case "add_drawing_symbol":
                        return Truncate((result?.Value<bool>("created") == true ? "created " : "existing ") + result?.Value<string>("name"), MaxLength);
                    case "edit_drawing_annotation":
                        return (result?.Value<int>("updated_count") ?? 0) + " annotations updated";
                    case "delete_drawing_items":
                        return (result?.Value<bool>("dry_run") == true ? "preview " + result?.Value<int>("count") : "deleted " + result?.Value<int>("deleted_count")) + " items";
                    case "edit_drawing_table":
                        return Truncate((result?.Value<bool>("rebuilt") == true ? "rebuilt " : "updated ") + result?.Value<string>("name"), MaxLength);
                    case "set_drawing_styles":
                        return result?.Value<int>("count") + " styles / " + result?.Value<int>("affected_count") + " affected items";
                    case "edit_sheet":
                        return Truncate(result?.Value<bool>("deleted") == true ? "deleted sheet " + result?.Value<string>("sheet") : "updated sheet " + result?.Value<string>("name"), MaxLength);
                    case "add_drawing_dimension": case "add_balloon":
                        return (result?.Value<int>("created_count") ?? 0) + " created / " + (result?.Value<int>("count") ?? 0) + " items";
                    case "export_drawing":
                        return (result?.Value<int>("completed_count") ?? 0) + " files · " + result?.Value<string>("format");
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
