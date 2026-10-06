using System;
using System.IO;
using System.Runtime.CompilerServices;
using Bimwright.Ipt.Server;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Redirects the call journal to a temp file before anything can touch <see cref="ServerLogger"/>'s
/// static constructor, so test runs never append to the real journal (spec F1-R2). ModuleInitializer
/// runs before any test code or type initializer in this assembly.
/// </summary>
internal static class TestJournalIsolation
{
    [ModuleInitializer]
    internal static void RedirectCallJournal()
        => Environment.SetEnvironmentVariable(
            ServerLogger.LogPathEnvVar,
            Path.Combine(Path.GetTempPath(), "ipt-mcp-tests", "calls.jsonl"));
}
