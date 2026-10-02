using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>Effective toast setting plus where the value came from (shown in the ribbon Status dialog).</summary>
public sealed record ToastSettings(bool EnableToast, string EnableSource, bool ShowBranding = false);

/// <summary>
/// Add-in-side toast config: <c>%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json</c> (keys
/// <c>enableToast</c>, overridden by env <c>BIMWRIGHT_INVENTOR_ENABLE_TOAST</c>, and <c>showBranding</c>).
/// <c>toastIdleSeconds</c> accepts 10, 20, 30 or 60 seconds and defaults to 20.
/// Never throws: anything unreadable means defaults. Other keys in the file, such as the retired <c>toastTheme</c>,
/// are ignored and kept.
/// </summary>
public static class ToastConfigStore
{
    private static readonly object WriteGate = new();
    public const string FileName = "iptmcp.config.json";
    public const string EnableEnv = "BIMWRIGHT_INVENTOR_ENABLE_TOAST";
    public const int DefaultIdleSeconds = 20;
    public static readonly int[] IdleChoices = { 10, 20, 30, 60 };

    public static string DefaultPath(string descriptorDir) => Path.Combine(descriptorDir, FileName);

    public static ToastSettings Load(string path, Func<string, string?> getEnv)
    {
        var enable = true;
        var enableSource = "default";
        var showBranding = false;

        var json = TryRead(path, out _);
        if (json?["enableToast"] is JValue { Type: JTokenType.Boolean } e)
        {
            enable = (bool)e;
            enableSource = "config";
        }
        if (json?["showBranding"] is JValue { Type: JTokenType.Boolean } b)
            showBranding = (bool)b;

        if (TryParseBool(getEnv(EnableEnv), out var envEnable))
        {
            enable = envEnable;
            enableSource = "env " + EnableEnv;
        }

        return new ToastSettings(enable, enableSource, showBranding);
    }

    public static bool SaveEnableToast(string path, bool enable) => Save(path, "enableToast", enable);

    public static bool SaveShowBranding(string path, bool show) => Save(path, "showBranding", show);

    public static int NormalizeIdleSeconds(int seconds)
        => Array.IndexOf(IdleChoices, seconds) >= 0 ? seconds : DefaultIdleSeconds;

    public static int LoadIdleSeconds(string path)
    {
        var value = TryRead(path, out _)?["toastIdleSeconds"];
        return value?.Type == JTokenType.Integer && int.TryParse(value.ToString(), out var seconds)
            ? NormalizeIdleSeconds(seconds) : DefaultIdleSeconds;
    }

    public static bool SaveIdleSeconds(string path, int seconds)
        => Update(path, root => root["toastIdleSeconds"] = NormalizeIdleSeconds(seconds));

    public static ToastPositionOptions LoadPosition(string path)
    {
        var root = TryRead(path, out _);
        var offset = root?["toastDragOffset"] as JObject;
        double? Number(JToken? value) => value?.Type is JTokenType.Integer or JTokenType.Float
            ? (double?)value : null;
        return new ToastPositionOptions(
            root?["toastHorizontalAlign"]?.Type == JTokenType.String && (string?)root["toastHorizontalAlign"] == "right",
            root?["toastVerticalAlign"]?.Type == JTokenType.String && (string?)root["toastVerticalAlign"] == "bottom",
            root?["toastDragEnabled"] is JValue { Type: JTokenType.Boolean } drag && (bool)drag,
            Number(offset?["x"]), Number(offset?["y"]));
    }

    public static bool SavePosition(string path, ToastPositionOptions options) => Update(path, root =>
    {
        root["toastHorizontalAlign"] = options.Right ? "right" : "left";
        root["toastVerticalAlign"] = options.Bottom ? "bottom" : "top";
        root["toastDragEnabled"] = options.DragEnabled;
        if (options.HasOffset)
            root["toastDragOffset"] = new JObject { ["x"] = options.OffsetX, ["y"] = options.OffsetY };
        else root.Remove("toastDragOffset");
    });

    /// <summary>
    /// Writes one boolean key and keeps every other key. Returns false, leaving the file untouched,
    /// when the existing file has content that is not a JSON object, or when the write fails.
    /// </summary>
    private static bool Save(string path, string key, bool value)
        => Update(path, obj => obj[key] = value);

    private static bool Update(string path, Action<JObject> update)
    {
        lock (WriteGate)
        {
            var tmp = path + ".tmp";
            try
            {
                var existing = TryRead(path, out var hadContent);
                if (existing == null && hadContent)
                    return false;   // never clobber a file the user wrote but we cannot parse

                var obj = existing ?? new JObject();
                update(obj);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(tmp, obj.ToString(Formatting.Indented));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
                return true;
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                return false;
            }
        }
    }

    /// <summary>
    /// The file as a JSON object, or null. <paramref name="hadContent"/> separates "missing or blank"
    /// (false) from "has text that is not a usable object" (true).
    /// </summary>
    private static JObject? TryRead(string path, out bool hadContent)
    {
        hadContent = false;
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;
            hadContent = true;
            return JToken.Parse(text) as JObject;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseBool(string? value, out bool result)
    {
        result = false;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on":
                result = true;
                return true;
            case "0": case "false": case "no": case "off":
                return true;
            default:
                return false;
        }
    }
}
