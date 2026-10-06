using System;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>
    /// Bridge between command completion and the send-code journal — ported from rvt-mcp.
    /// Called from the listener thread alongside the session-log add; failures never
    /// reach the command path.
    /// </summary>
    internal static class SendCodeJournalGate
    {
        public static void OnSendCodeLogged(
            string commandName,
            string? paramsJson,
            string? codeSnippet,
            bool success,
            long durationMs,
            string? resultError,
            string? resultJson)
        {
            if (commandName != "send_code")
                return;

            try
            {
                var cfg = IptPrivacyConfig.Load();
                SendCodeJournal.TryAppend(
                    cfg,
                    McpLogger.CurrentSessionId,
                    codeSnippet,
                    success,
                    durationMs,
                    resultError,
                    resultJson);
            }
            catch { }
        }
    }
}
