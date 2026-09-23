using System;
using System.Text;

namespace Bimwright.Ipt.Shared.Views.Toast;

public sealed record StatusInfo(
    string TargetId,
    int InventorYear,
    string Transport,
    string? PipeName,
    int Port,
    bool SendCodeEnabled,
    bool ReadOnly,
    ToastSettings Toast,
    bool ToastOnNow,
    string? LastPaletteDecision,
    string ConfigPath);

/// <summary>Copy for the ribbon Status dialog. Host-free so it is unit-tested.</summary>
public static class StatusText
{
    public static string Build(StatusInfo s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Target: {s.TargetId} (Inventor {s.InventorYear})");
        sb.AppendLine("Transport: " + (string.Equals(s.Transport, "pipe", StringComparison.OrdinalIgnoreCase)
            ? "Named Pipe " + s.PipeName
            : $"TCP port {s.Port} (loopback)"));
        sb.AppendLine("send_code (add-in opt-in): " + OnOff(s.SendCodeEnabled));
        sb.AppendLine("Read-only lock: " + OnOff(s.ReadOnly));
        sb.AppendLine();
        sb.AppendLine($"Toasts: {OnOff(s.ToastOnNow)} (startup value from {s.Toast.EnableSource})");
        if (s.Toast.EnableSource.StartsWith("env", StringComparison.Ordinal))
            sb.AppendLine($"  {ToastConfigStore.EnableEnv} is set: it wins over the ribbon choice at the next start.");
        sb.AppendLine($"Toast theme: {s.Toast.Theme.ToString().ToLowerInvariant()} (from {s.Toast.ThemeSource})");
        sb.AppendLine("  Last palette: " + (s.LastPaletteDecision ?? "no toast shown yet"));
        sb.AppendLine("Config file: " + s.ConfigPath);
        sb.AppendLine();
        sb.AppendLine("Privacy");
        sb.AppendLine("  Toasts show command names and short results on this screen only. Nothing is sent anywhere.");
        sb.AppendLine("  Auto theme reads the screen colour behind a toast in memory. Pixels are never saved or logged.");
        sb.Append("  The MCP server keeps its own call journal (BIMWRIGHT_INVENTOR_CALL_LOG, default %LOCALAPPDATA%\\Bimwright\\ipt-mcp-calls.jsonl).");
        return sb.ToString();
    }

    private static string OnOff(bool v) => v ? "ON" : "OFF";
}
