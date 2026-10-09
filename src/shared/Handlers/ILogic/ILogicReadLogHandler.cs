#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Security;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>ilogic_read_log</c> — read-only tail of the file-backed iLogic log via
/// <c>LogControl.FullFileName</c> (shared-read, iLogic keeps the file open). <c>max_chars</c>
/// (default 65536) reads only the tail end — set 0 for the whole file. Log text is secret-masked
/// before returning. Not document-scoped: the iLogic log is session-wide.
/// </summary>
public sealed class ILogicReadLogHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_read_log";
    public bool IsReadOnly => true;

    internal const int DefaultMaxChars = 65536;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (!ILogicBridge.TryGet(ctx, Name, app, out var ilogic, out var failure))
            return failure!;

        int maxChars;
        try { maxChars = (int?)p["max_chars"] ?? DefaultMaxChars; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_chars must be an integer"); }
        if (maxChars < 0 || maxChars > ILogicLogReader.MaxTailChars)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_chars must be 0..1048576 (0 reads the whole file)");

        try
        {
            var path = ilogic!.LogFullFileName;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                return Ok(ctx, new JObject
                {
                    ["log_file"] = path,
                    ["text"] = "",
                    ["truncated"] = false,
                    ["exceptions"] = ExceptionsSnapshot(ilogic),
                    ["note"] = "iLogic file log is empty or not file-backed on this machine " +
                        "(iLogicLogger.config-dependent); Logger.* output goes to the iLogic pane " +
                        "instead. Rule exceptions are still captured in 'exceptions'.",
                });
            }

            var tail = ILogicLogReader.Read(path!, maxChars);
            return Ok(ctx, new JObject
            {
                ["log_file"] = path,
                ["log_bytes"] = tail.Length,
                ["bytes_read"] = tail.BytesRead,
                ["text"] = SecretMasker.Mask(tail.Text),
                ["truncated"] = tail.Truncated,
                ["exceptions"] = ExceptionsSnapshot(ilogic),
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

    /// <summary>Read-only snapshot of <c>RuleExceptionsLog</c> — never clears (this is a query).</summary>
    private static JArray ExceptionsSnapshot(ILogicAccess ilogic)
    {
        var arr = new JArray();
        foreach (var e in ilogic.GetExceptions())
            arr.Add(new JObject
            {
                ["rule"] = e.RuleName,
                ["line"] = e.LineNumber,
                ["context"] = e.Context,
                ["message"] = e.Message,
            });
        return arr;
    }
}
#endif
