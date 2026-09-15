using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>
/// Append-only JSONL of every add-in round-trip (and server-side meta tools).
/// Path: %LOCALAPPDATA%\Bimwright\ipt-mcp-calls.jsonl, overridable via the
/// BIMWRIGHT_INVENTOR_CALL_LOG environment variable (env-only: this class is static and
/// initializes before config is loaded). Params are stored as-is (including send_code bodies)
/// so MCP development can replay real calls. The finish line keeps <c>success</c> at envelope
/// level and adds the script-level outcome (<c>data_ok</c>/<c>data_error</c>/<c>stdout_bytes</c>),
/// response size, add-in duration and target; those keys are always present (null when n/a).
/// </summary>
internal static class ServerLogger
{
    internal const string LogPathEnvVar = "BIMWRIGHT_INVENTOR_CALL_LOG";
    internal const int MaxDataErrorChars = 300;

    internal static readonly string SessionId = BuildSessionId(DateTime.UtcNow, Environment.ProcessId);

    private static readonly string LogPath;
    private static readonly object Gate = new object();

    static ServerLogger()
    {
        LogPath = ResolveLogPath(Environment.GetEnvironmentVariable(LogPathEnvVar));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        }
        catch
        {
            // A bad override must not make the type unusable; writes then fail inside their own try.
        }
    }

    public static void LogStart(string requestId, string toolName, object? parameters)
    {
        try
        {
            WriteEntry(new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                session_id = SessionId,
                request_id = requestId,
                tool = toolName,
                phase = "start",
                @params = parameters
            });
        }
        catch
        {
            // Logging must never break a CAD call.
        }
    }

    public static void LogFinish(string requestId, string toolName, bool success, long durationMs, string? errorMsg = null,
        string? errorCode = null, string? targetId = null, long? responseBytes = null, long? pluginDurationMs = null,
        JToken? data = null)
    {
        try
        {
            WriteEntry(BuildFinishEntry(SessionId, requestId, toolName, success, durationMs, errorMsg, errorCode,
                targetId, responseBytes, pluginDurationMs, data));
        }
        catch
        {
        }
    }

    internal static string BuildSessionId(DateTime startUtc, int processId) =>
        "server-" + startUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
                  + "-" + processId.ToString(CultureInfo.InvariantCulture);

    internal static string ResolveLogPath(string? envOverride) =>
        string.IsNullOrWhiteSpace(envOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "ipt-mcp-calls.jsonl")
            : envOverride;

    internal static string? TruncateError(string? s, int max = MaxDataErrorChars) =>
        s is null || s.Length <= max ? s : s.Substring(0, max);

    /// <summary>
    /// Script-level outcome of send_code / run_baked_tool payloads: <c>ok</c> only when it is a JSON
    /// boolean, <c>error</c> (truncated) and <c>stdout</c> byte count only when they are strings.
    /// </summary>
    internal static (bool? ok, string? error, int? stdoutBytes) ExtractDataOutcome(JToken? data)
    {
        if (data is not JObject obj) return (null, null, null);

        bool? ok = obj["ok"] is { Type: JTokenType.Boolean } okToken ? okToken.Value<bool>() : null;
        string? error = obj["error"] is { Type: JTokenType.String } errorToken
            ? TruncateError(errorToken.Value<string>())
            : null;
        int? stdoutBytes = obj["stdout"] is { Type: JTokenType.String } stdoutToken
            ? Encoding.UTF8.GetByteCount(stdoutToken.Value<string>()!)
            : null;
        return (ok, error, stdoutBytes);
    }

    internal static JObject BuildFinishEntry(string sessionId, string requestId, string tool, bool success,
        long durationMs, string? error, string? errorCode, string? targetId, long? responseBytes,
        long? pluginDurationMs, JToken? data)
    {
        var (dataOk, dataError, stdoutBytes) = ExtractDataOutcome(data);
        return new JObject
        {
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["session_id"] = sessionId,
            ["request_id"] = requestId,
            ["tool"] = tool,
            ["phase"] = "finish",
            ["success"] = success,
            ["duration_ms"] = durationMs,
            ["error"] = NullableString(error),
            ["error_code"] = NullableString(errorCode),
            ["target_id"] = NullableString(targetId),
            ["response_bytes"] = responseBytes,
            ["plugin_duration_ms"] = pluginDurationMs,
            ["data_ok"] = dataOk,
            ["data_error"] = NullableString(dataError),
            ["stdout_bytes"] = stdoutBytes,
        };
    }

    // A null string converts to a JValue typed String; emit a real JSON null token instead.
    private static JToken NullableString(string? value) => value is null ? JValue.CreateNull() : new JValue(value);

    private static void WriteEntry(object entry)
    {
        var line = JsonConvert.SerializeObject(entry, Formatting.None);
        lock (Gate)
        {
            File.AppendAllText(LogPath, line + "\n");
        }
    }
}
