using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>
/// Family contract FC-TGT-07: detects that the MCP client launched this server inside a Windows
/// AppContainer, where it can neither read the add-in descriptors nor reach Inventor. The gateway
/// only explains this; it never tries to leave the sandbox. <see cref="Classify"/> is pure and
/// host-free; <see cref="ProcessTokenIsAppContainer"/> is the thin Windows probe.
/// </summary>
internal static class ClientSandbox
{
    internal const string KindAppContainer = "appcontainer";
    internal const string EvidenceToken = "token_is_appcontainer";
    internal const string EvidencePath = "local_app_data_under_packages_ac";

    /// <summary>The single guidance message used by every CLIENT_SANDBOXED path.</summary>
    internal const string Message =
        "CLIENT_SANDBOXED: ipt-mcp is running inside your MCP client's sandbox (a Windows AppContainer), so it cannot see or reach Inventor on this computer. " +
        "The command was not sent. The sandbox is how your client protects your computer by limiting what agent tools can access; Inventor and ipt-mcp are working normally. " +
        "To use ipt-mcp, a person must choose to let this one MCP server run outside the sandbox in the client's settings: prefer a per-server sandbox exception; " +
        "otherwise use a client permission mode that runs local MCP servers unsandboxed. Do not loosen file or folder permissions to work around it. " +
        "Agents: report this to the user and do not try to bypass the sandbox.";

    internal sealed record Result(bool Detected, string? Kind, IReadOnlyList<string> Evidence);

    // ...\Packages\<one segment>\AC followed by a separator or the end of the path.
    private static readonly Regex PackagesAc = new(@"[\\/]Packages[\\/][^\\/]+[\\/]AC(?:[\\/]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool IsUnderPackagesAc(string? path) => !string.IsNullOrEmpty(path) && PackagesAc.IsMatch(path);

    /// <summary>Detected when the token is an AppContainer token or any of the given LocalAppData paths is under Packages\&lt;pkg&gt;\AC.</summary>
    internal static Result Classify(bool? tokenIsAppContainer, params string?[] localAppDataPaths)
    {
        var evidence = new List<string>();
        if (tokenIsAppContainer == true) evidence.Add(EvidenceToken);
        foreach (var path in localAppDataPaths ?? Array.Empty<string?>())
        {
            if (!IsUnderPackagesAc(path)) continue;
            evidence.Add(EvidencePath);
            break;
        }
        return evidence.Count > 0 ? new Result(true, KindAppContainer, evidence) : new Result(false, null, evidence);
    }

    internal static JObject ToJson(Result result, string dataDir) => new()
    {
        ["detected"] = result.Detected,
        ["kind"] = result.Kind,
        ["evidence"] = new JArray(result.Evidence),
        ["data_dir"] = dataDir,
    };

    private static readonly Lazy<bool?> CachedToken = new(ProbeToken);

    /// <summary>TokenIsAppContainer of the current process token; evaluated once. Null off Windows or on failure.</summary>
    internal static bool? ProcessTokenIsAppContainer() => CachedToken.Value;

    private const int TokenIsAppContainerClass = 29;
    private const uint TOKEN_QUERY = 0x0008;

    private static bool? ProbeToken()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token)) return null;
            try
            {
                var buffer = Marshal.AllocHGlobal(sizeof(uint));
                try
                {
                    if (!GetTokenInformation(token, TokenIsAppContainerClass, buffer, sizeof(uint), out _)) return null;
                    return Marshal.ReadInt32(buffer) != 0;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseHandle(token); }
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
