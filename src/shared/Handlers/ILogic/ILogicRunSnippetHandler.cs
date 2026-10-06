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
/// <c>ilogic_run_snippet</c> — run arbitrary iLogic VB <c>code</c> as a temporary internal rule.
/// Send-code-grade: only registered when <c>EnableSendCode</c> and additionally fenced in the
/// dispatcher. Uses the temp-rule pattern — <c>AddRule</c> compiles and runs, then
/// <c>try/finally DeleteRule</c>; before creating one it sweeps stale <c>TempMCPRule_*</c> residue
/// left by interrupted runs so agent VB never stays embedded in a customer document. If the
/// snippet logs <c>MCP_RESULT_BEGIN…MCP_RESULT_END</c> via <c>Logger.Info</c>, the last marker is
/// scraped from the iLogic log and returned as <c>result</c> (secret-masked).
/// </summary>
public sealed class ILogicRunSnippetHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_run_snippet";
    public bool IsReadOnly => false;

    internal const string TempRulePrefix = "TempMCPRule_";
    internal const string ResultBegin = "MCP_RESULT_BEGIN";
    internal const string ResultEnd = "MCP_RESULT_END";
    private const int LogTailChars = 262144;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var code = (string?)p["code"];
        if (string.IsNullOrWhiteSpace(code))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "code is required (iLogic VB source)");

        var doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var failure);
        if (failure != null) return failure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        var app = (Application)ctx.Application!;
        if (!ILogicBridge.TryGet(ctx, Name, app, out var ilogic, out failure))
            return failure!;

        var ruleName = TempRulePrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
        var swept = 0;
        var deleted = false;
        try
        {
            using var silent = SilentOperationScope.Enter(app, p);
            var prevSilent = ilogic!.SilentOperation;
            try
            {
                ilogic.SilentOperation = true;

                // Residue sweep: a crashed run would otherwise leave agent VB embedded in the doc.
                foreach (var r in ilogic.GetRules(doc))
                {
                    if (!r.Name.StartsWith(TempRulePrefix, StringComparison.Ordinal)) continue;
                    try { ilogic.DeleteRule(doc, r.Name); swept++; } catch { }
                }

                ilogic.AddRule(doc, ruleName, code!); // AddRule compiles AND runs
            }
            finally
            {
                try { deleted = ilogic.DeleteRule(doc, ruleName) >= 0; } catch { }
                ilogic.SilentOperation = prevSilent;
            }

            return Ok(ctx, new JObject
            {
                ["ran"] = true,
                ["rule_deleted"] = deleted,
                ["residue_swept"] = swept,
                ["result"] = ScrapeResultMarker(ilogic),
            });
        }
        catch (Exception ex)
        {
            // Even on failure, report whether the temp rule leaked so the agent can act on it.
            var err = Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message);
            if (err.Data is JObject d)
            {
                d["rule_deleted"] = deleted;
                d["residue_swept"] = swept;
            }
            return err;
        }
    }

    /// <summary>Last <c>MCP_RESULT_BEGIN…MCP_RESULT_END</c> payload in the iLogic log tail, or null.</summary>
    private static string? ScrapeResultMarker(ILogicAccess ilogic)
    {
        try
        {
            var path = ilogic.LogFullFileName;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            string tail;
            using (var fs = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8, true))
            {
                var all = sr.ReadToEnd();
                tail = all.Length > LogTailChars ? all.Substring(all.Length - LogTailChars) : all;
            }
            var end = tail.LastIndexOf(ResultEnd, StringComparison.Ordinal);
            if (end < 0) return null;
            var begin = tail.LastIndexOf(ResultBegin, end, StringComparison.Ordinal);
            if (begin < 0) return null;
            begin += ResultBegin.Length;
            var payload = tail.Substring(begin, end - begin).Trim();
            return payload.Length == 0 ? null : SecretMasker.Mask(payload);
        }
        catch { return null; }
    }
}
#endif
