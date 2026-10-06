#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// The plugin references <c>Autodesk.iLogic.*</c> with <c>&lt;Private&gt;false&lt;/Private&gt;</c> — the
/// assemblies must resolve from the running Inventor's <c>Bin</c> (or <c>Bin\iLogicBin</c>) at first
/// use. net48 relies on <c>AppDomain.AssemblyResolve</c>; the net8+/net10 plugin shells load into a
/// private <see cref="AssemblyLoadContext"/> that does not probe Inventor's Bin, so the hook is
/// installed on the plugin's own context. Idempotent; a miss simply leaves the typed path to fall
/// back to <c>dynamic</c>.
/// </summary>
internal static class ILogicAssemblyResolver
{
    private static int _installed;

    internal static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;

        string? bin = null;
        try { bin = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName); } catch { }
        if (string.IsNullOrEmpty(bin)) return;

        var dirs = new[] { Path.Combine(bin!, "iLogicBin"), bin! };

#if NET8_0_OR_GREATER
        var alc = AssemblyLoadContext.GetLoadContext(typeof(ILogicAssemblyResolver).Assembly);
        if (alc is null) return;
        alc.Resolving += (ctx, name) =>
        {
            if (name.Name is null || !name.Name.StartsWith("Autodesk.iLogic", StringComparison.Ordinal)) return null;
            foreach (var d in dirs)
            {
                var candidate = Path.Combine(d, name.Name + ".dll");
                if (File.Exists(candidate)) return ctx.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
#else
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            if (name is null || !name.StartsWith("Autodesk.iLogic", StringComparison.Ordinal)) return null;
            foreach (var d in dirs)
            {
                var candidate = Path.Combine(d, name + ".dll");
                if (File.Exists(candidate)) return System.Reflection.Assembly.LoadFrom(candidate);
            }
            return null;
        };
#endif
    }
}
#endif
