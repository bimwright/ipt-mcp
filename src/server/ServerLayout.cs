using System;
using System.IO;
using Bimwright.Setup;

namespace Bimwright.Ipt.Server;

/// <summary>
/// The gateway's per-user paths. Normally they come from <see cref="RuntimeLayout"/> unchanged.
/// Family contract FC-TGT-07: inside a client AppContainer the redirected LocalAppData
/// (<c>...\Packages\&lt;pkg&gt;\AC</c>) has a parent the container cannot stat, so RuntimeLayout's
/// reparse check throws UnauthorizedAccessException and the server would die before MCP initialize.
/// Only in that case the server falls back to an unverified folder under the redirected LocalAppData
/// (inside the sandbox; nothing real is there), so it can start and answer CLIENT_SANDBOXED.
/// Outside a sandbox every RuntimeLayout failure is rethrown as before.
/// </summary>
internal sealed record ServerLayout(string DataRoot, string RuntimeRoot, string GatewayLogPath, RuntimeLayout? Layout)
{
    internal const string ProductId = "ipt-mcp";

    /// <summary>True when this paths set is the sandbox fallback (no verified RuntimeLayout).</summary>
    internal bool SandboxFallback => Layout is null;

    internal static ServerLayout From(RuntimeLayout layout) => new(layout.DataRoot, layout.RuntimeRoot, layout.GatewayLogPath, layout);

    /// <summary>Same paths an unmanaged RuntimeLayout would use under <paramref name="localAppData"/>, without its checks.</summary>
    internal static ServerLayout Fallback(string localAppData)
    {
        var family = Path.Combine(localAppData, "Bimwright");
        var root = Path.Combine(family, ProductId);
        return new(root, root, Path.Combine(family, ProductId + "-calls.jsonl"), null);
    }

    private static string LocalAppData() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Whether this process runs in a client AppContainer (token or redirected LocalAppData).</summary>
    internal static bool ProcessSandboxed(string? extraRoot = null) =>
        ClientSandbox.Classify(ClientSandbox.ProcessTokenIsAppContainer(), LocalAppData(), extraRoot).Detected;

    /// <summary>RuntimeLayout.ForCurrentUser, or the sandbox fallback when only the AppContainer denies it.</summary>
    internal static ServerLayout ForCurrentUser() =>
        Resolve(() => RuntimeLayout.ForCurrentUser(ProductId), () => ProcessSandboxed(), LocalAppData());

    /// <summary>RuntimeLayout.Resolve for an explicit --local-app-data root, or the sandbox fallback inside an AppContainer.</summary>
    internal static ServerLayout ForRoot(string localAppDataRoot) =>
        Resolve(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Inventor requires Windows.");
            return RuntimeLayout.Resolve(localAppDataRoot, System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, ProductId);
        }, () => ProcessSandboxed(localAppDataRoot), localAppDataRoot);

    internal static ServerLayout Resolve(Func<RuntimeLayout> resolve, Func<bool> sandboxed, string localAppData)
    {
        try { return From(resolve()); }
        catch (UnauthorizedAccessException) when (sandboxed()) { return Fallback(localAppData); }
    }

    /// <summary>
    /// Program startup: RuntimeLayout.StartForCurrentUser (writer lease), or null when only the AppContainer
    /// denies it; the config then keeps its fallback paths.
    /// </summary>
    internal static RuntimeLayout? StartForCurrentUser() =>
        Start(() => RuntimeLayout.StartForCurrentUser(ProductId), () => ProcessSandboxed());

    /// <summary>Program startup for an explicit --local-app-data root, or null when only the AppContainer denies it.</summary>
    internal static RuntimeLayout? StartForRoot(string localAppDataRoot) =>
        Start(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Inventor requires Windows.");
            return RuntimeLayout.Start(localAppDataRoot, System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, ProductId);
        }, () => ProcessSandboxed(localAppDataRoot));

    internal static RuntimeLayout? Start(Func<RuntimeLayout> start, Func<bool> sandboxed)
    {
        try { return start(); }
        catch (UnauthorizedAccessException) when (sandboxed()) { return null; }
    }
}
