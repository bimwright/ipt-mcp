using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Views.Toast;

public enum ToastTheme { Auto, Light, Dark }

/// <summary>Effective toast settings plus where each value came from (shown in the ribbon Status dialog).</summary>
public sealed record ToastSettings(bool EnableToast, ToastTheme Theme, string EnableSource, string ThemeSource);

/// <summary>
/// Add-in-side toast config: <c>%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json</c> (keys
/// <c>enableToast</c>, <c>toastTheme</c>), overridden by env <c>BIMWRIGHT_INVENTOR_ENABLE_TOAST</c> /
/// <c>BIMWRIGHT_INVENTOR_TOAST_THEME</c>. Never throws: anything unreadable means defaults.
/// </summary>
public static class ToastConfigStore
{
    public const string FileName = "iptmcp.config.json";
    public const string EnableEnv = "BIMWRIGHT_INVENTOR_ENABLE_TOAST";
    public const string ThemeEnv = "BIMWRIGHT_INVENTOR_TOAST_THEME";

    public static string DefaultPath(string descriptorDir) => Path.Combine(descriptorDir, FileName);

    public static ToastSettings Load(string path, Func<string, string?> getEnv)
    {
        var enable = true;
        var enableSource = "default";
        var theme = ToastTheme.Auto;
        var themeSource = "default";

        var json = TryRead(path, out _);
        if (json?["enableToast"] is JValue { Type: JTokenType.Boolean } e)
        {
            enable = (bool)e;
            enableSource = "config";
        }
        if (json?["toastTheme"] is JValue { Type: JTokenType.String } t && TryParseTheme((string?)t, out var jsonTheme))
        {
            theme = jsonTheme;
            themeSource = "config";
        }

        if (TryParseBool(getEnv(EnableEnv), out var envEnable))
        {
            enable = envEnable;
            enableSource = "env " + EnableEnv;
        }
        if (TryParseTheme(getEnv(ThemeEnv), out var envTheme))
        {
            theme = envTheme;
            themeSource = "env " + ThemeEnv;
        }

        return new ToastSettings(enable, theme, enableSource, themeSource);
    }

    /// <summary>
    /// Writes <c>enableToast</c> and keeps every other key. Returns false, leaving the file untouched,
    /// when the existing file has content that is not a JSON object, or when the write fails.
    /// </summary>
    public static bool SaveEnableToast(string path, bool enable)
    {
        var tmp = path + ".tmp";
        try
        {
            var existing = TryRead(path, out var hadContent);
            if (existing == null && hadContent)
                return false;   // never clobber a file the user wrote but we cannot parse

            var obj = existing ?? new JObject();
            obj["enableToast"] = enable;
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

    private static bool TryParseTheme(string? value, out ToastTheme theme)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "auto": theme = ToastTheme.Auto; return true;
            case "light": theme = ToastTheme.Light; return true;
            case "dark": theme = ToastTheme.Dark; return true;
            default: theme = ToastTheme.Auto; return false;
        }
    }
}
