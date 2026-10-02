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
/// Never throws: anything unreadable means defaults. Other keys in the file, such as the retired <c>toastTheme</c>,
/// are ignored and kept.
/// </summary>
public static class ToastConfigStore
{
    public const string FileName = "iptmcp.config.json";
    public const string EnableEnv = "BIMWRIGHT_INVENTOR_ENABLE_TOAST";

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

    /// <summary>
    /// Writes one boolean key and keeps every other key. Returns false, leaving the file untouched,
    /// when the existing file has content that is not a JSON object, or when the write fails.
    /// </summary>
    private static bool Save(string path, string key, bool value)
    {
        var tmp = path + ".tmp";
        try
        {
            var existing = TryRead(path, out var hadContent);
            if (existing == null && hadContent)
                return false;   // never clobber a file the user wrote but we cannot parse

            var obj = existing ?? new JObject();
            obj[key] = value;
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
