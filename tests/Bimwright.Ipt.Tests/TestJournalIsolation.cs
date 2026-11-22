using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Bimwright.Ipt.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Clears the developer's own <c>BIMWRIGHT_*</c> overrides and redirects the call journal to a temp
/// file before anything can read them (e.g. <see cref="ServerLogger"/>'s static constructor), so a
/// local run sees the same defaults as CI and never appends to the real journal (spec F1-R2).
/// ModuleInitializer runs before any test code or type initializer in this assembly.
/// </summary>
internal static class TestJournalIsolation
{
    [ModuleInitializer]
    internal static void RedirectCallJournal()
    {
        var overrides = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(k => k.StartsWith("BIMWRIGHT_", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var name in overrides)
            Environment.SetEnvironmentVariable(name, null);

        Environment.SetEnvironmentVariable(
            ServerLogger.LogPathEnvVar,
            Path.Combine(Path.GetTempPath(), "ipt-mcp-tests", "calls.jsonl"));

        // Retention tests rely on pruning: pin BIMWRIGHT_KEEP_ALL_LOGS off; KeepAllLogsTests pin it on.
        Bimwright.Ipt.Shared.Logging.LogRetentionPolicy.ReadValue = () => null;
    }
}
