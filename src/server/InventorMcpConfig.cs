using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

public sealed class InventorMcpConfig
{
    public bool ReadOnly { get; set; }
    public bool EnableSendCode { get; set; }
    public bool EnableToolBaker { get; set; } = true;
    public bool EnableAdaptiveBake { get; set; }
    public int TimeoutMs { get; set; } = 30000;
    public int MaxResponseBytes { get; set; } = 5_000_000;
    public int SpillRetentionHours {get;set;}=36;
    public bool EnableOutputGuard { get; set; } = true;
    public int OutputWarningBytes { get; set; } = 64 * 1024;
    public int OutputStrongWarningBytes { get; set; } = 256 * 1024;
    public int OutputBudgetBytes { get; set; } = 1024 * 1024;
    public string? TargetId { get; set; }
    public List<string> Toolsets { get; set; } = new();

    public string DescriptorDirectory { get; set; } = Bimwright.Setup.RuntimeLayout.ForCurrentUser("ipt-mcp").RuntimeRoot;
    public string BakeDirectory { get; set; } = Path.Combine(
        Bimwright.Setup.RuntimeLayout.ForCurrentUser("ipt-mcp").DataRoot, "baked");

    public static InventorMcpConfig Load(string[] args)
    {
        var config = new InventorMcpConfig();
        ApplyJson(config, args);   // lowest precedence
        ApplyEnv(config);          // middle
        ApplyCli(config, args);    // highest
        if(config.SpillRetentionHours<=0)throw new ArgumentException("spillRetentionHours must be positive.");
        if (config.OutputWarningBytes < 512 || config.OutputStrongWarningBytes < config.OutputWarningBytes || config.OutputBudgetBytes < Math.Max(4096, config.OutputStrongWarningBytes) || config.MaxResponseBytes < 4096)
            throw new ArgumentException("Output thresholds must be ordered (warning >= 512); outputBudgetBytes and maxResponseBytes must be >= 4096.");
        return config;
    }

    // --- precedence layer 1: JSON file (via --config <path>) ---
    private static void ApplyJson(InventorMcpConfig c, string[] args)
    {
        var path = ValueAfter(args, "--config");
        if (path is null || !File.Exists(path)) return;
        var o = JObject.Parse(File.ReadAllText(path));
        if (o["readOnly"] is { } ro) c.ReadOnly = ro.Value<bool>();
        if (o["enableSendCode"] is { } sc) c.EnableSendCode = sc.Value<bool>();
        if (o["enableToolBaker"] is { } tb) c.EnableToolBaker = tb.Value<bool>();
        if (o["enableAdaptiveBake"] is { } ab) c.EnableAdaptiveBake = ab.Value<bool>();
        if (o["timeoutMs"] is { } tm) c.TimeoutMs = tm.Value<int>();
        if (o["maxResponseBytes"] is { } mb) c.MaxResponseBytes = mb.Value<int>();
        if (o["spillRetentionHours"] is { } sr) c.SpillRetentionHours = sr.Value<int>();
        if (o["enableOutputGuard"] is { } og) c.EnableOutputGuard = og.Value<bool>();
        if (o["outputWarningBytes"] is { } ow) c.OutputWarningBytes = ow.Value<int>();
        if (o["outputStrongWarningBytes"] is { } os) c.OutputStrongWarningBytes = os.Value<int>();
        if (o["outputBudgetBytes"] is { } ob) c.OutputBudgetBytes = ob.Value<int>();
        if (o["target"] is { } tg) c.TargetId = tg.Value<string>();
        if (o["toolsets"] is JArray arr) c.Toolsets = arr.Select(x => x.Value<string>()!).ToList();
    }

    // --- precedence layer 2: environment variables ---
    private static void ApplyEnv(InventorMcpConfig c)
    {
        if (Bool("BIMWRIGHT_INVENTOR_READ_ONLY") is { } ro) c.ReadOnly = ro;
        if (Bool("BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE") is { } sc) c.EnableSendCode = sc;
        if (Bool("BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER") is { } tb) c.EnableToolBaker = tb;
        if (Bool("BIMWRIGHT_INVENTOR_ENABLE_ADAPTIVE_BAKE") is { } ab) c.EnableAdaptiveBake = ab;
        if (Int("BIMWRIGHT_INVENTOR_TIMEOUT_MS") is { } tm) c.TimeoutMs = tm;
        if (Int("BIMWRIGHT_INVENTOR_MAX_RESPONSE_BYTES") is { } mb) c.MaxResponseBytes = mb;
        if (Int("BIMWRIGHT_INVENTOR_SPILL_RETENTION_HOURS") is { } sr) c.SpillRetentionHours = sr;
        if (Bool("BIMWRIGHT_INVENTOR_OUTPUT_GUARD") is { } og) c.EnableOutputGuard = og;
        if (Int("BIMWRIGHT_INVENTOR_OUTPUT_WARNING_BYTES") is { } ow) c.OutputWarningBytes = ow;
        if (Int("BIMWRIGHT_INVENTOR_OUTPUT_STRONG_WARNING_BYTES") is { } os) c.OutputStrongWarningBytes = os;
        if (Int("BIMWRIGHT_INVENTOR_OUTPUT_BUDGET_BYTES") is { } ob) c.OutputBudgetBytes = ob;
        var tg = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_TARGET");
        if (!string.IsNullOrWhiteSpace(tg)) c.TargetId = tg;
        var ts = Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_TOOLSETS");
        if (!string.IsNullOrWhiteSpace(ts)) c.Toolsets = SplitCsv(ts);
    }

    // --- precedence layer 3: CLI flags (highest) ---
    private static void ApplyCli(InventorMcpConfig c, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--read-only":            c.ReadOnly = NextBool(args, ref i, true); break;
                case "--enable-send-code":     c.EnableSendCode = true; break;
                case "--disable-toolbaker":    c.EnableToolBaker = false; break;
                case "--enable-adaptive-bake": c.EnableAdaptiveBake = true; break;
                case "--toolsets":             c.Toolsets = SplitCsv(Next(args, ref i)); break;
                case "--target":               c.TargetId = Next(args, ref i); break;
                case "--timeout-ms":           if (int.TryParse(Next(args, ref i), out var t)) c.TimeoutMs = t; break;
                case "--max-response-bytes":   if (int.TryParse(Next(args, ref i), out var m)) c.MaxResponseBytes = m; break;
                case "--spill-retention-hours": if (int.TryParse(Next(args, ref i),out var hours)) c.SpillRetentionHours=hours; break;
                case "--disable-output-guard": c.EnableOutputGuard = false; break;
                case "--enable-output-guard": c.EnableOutputGuard = true; break;
                case "--output-warning-bytes": c.OutputWarningBytes = int.Parse(Next(args, ref i)); break;
                case "--output-strong-warning-bytes": c.OutputStrongWarningBytes = int.Parse(Next(args, ref i)); break;
                case "--output-budget-bytes": c.OutputBudgetBytes = int.Parse(Next(args, ref i)); break;
            }
        }
    }

    private static List<string> SplitCsv(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? ValueAfter(string[] a, string flag)
    {
        var i = Array.IndexOf(a, flag);
        return (i >= 0 && i + 1 < a.Length) ? a[i + 1] : null;
    }
    private static string Next(string[] a, ref int i) => (i + 1 < a.Length) ? a[++i] : "";
    private static bool NextBool(string[] a, ref int i, bool bareValue)
    {
        if (i + 1 < a.Length && bool.TryParse(a[i + 1], out var b)) { i++; return b; }
        return bareValue;
    }
    private static bool? Bool(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return null;
        return v.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
    private static int? Int(string name)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : null;
}
