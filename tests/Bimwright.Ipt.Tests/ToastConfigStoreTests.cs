using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-toast-cfg-" + Guid.NewGuid().ToString("N"));
    private string Cfg => Path.Combine(_dir, ToastConfigStore.FileName);

    public ToastConfigStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Func<string, string?> Env(string? enable = null)
        => name => name == ToastConfigStore.EnableEnv ? enable : null;

    [Fact]
    public void Missing_file_and_env_gives_defaults()
    {
        var s = ToastConfigStore.Load(Cfg, Env());
        Assert.True(s.EnableToast);
        Assert.Equal("default", s.EnableSource);
    }

    [Fact]
    public void Json_values_are_used()
    {
        File.WriteAllText(Cfg, """{ "enableToast": false }""");
        var s = ToastConfigStore.Load(Cfg, Env());
        Assert.False(s.EnableToast);
        Assert.Equal("config", s.EnableSource);
    }

    [Fact]
    public void Env_overrides_json()
    {
        File.WriteAllText(Cfg, """{ "enableToast": false }""");
        var s = ToastConfigStore.Load(Cfg, Env(enable: "1"));
        Assert.True(s.EnableToast);
        Assert.Equal("env " + ToastConfigStore.EnableEnv, s.EnableSource);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("off", false)]
    [InlineData("No", false)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("YES", true)]
    [InlineData(" true ", true)]
    [InlineData("on", true)]
    public void Env_bool_spellings(string value, bool expected)
    {
        File.WriteAllText(Cfg, expected ? """{ "enableToast": false }""" : """{ "enableToast": true }""");
        Assert.Equal(expected, ToastConfigStore.Load(Cfg, Env(enable: value)).EnableToast);
    }

    [Fact]
    public void Unrecognized_env_values_are_ignored()
    {
        File.WriteAllText(Cfg, """{ "enableToast": false }""");
        var s = ToastConfigStore.Load(Cfg, Env(enable: "maybe"));
        Assert.False(s.EnableToast);
        Assert.Equal("config", s.EnableSource);
    }

    [Fact]
    public void The_retired_theme_key_and_env_have_no_effect()
    {
        File.WriteAllText(Cfg, """{ "enableToast": false, "toastTheme": "dark" }""");
        var s = ToastConfigStore.Load(Cfg, name => name == "BIMWRIGHT_INVENTOR_TOAST_THEME" ? "light" : null);
        Assert.False(s.EnableToast);
        Assert.Equal("config", s.EnableSource);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "enableToast": "yes", "toastTheme": 3 }""")]
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void Unusable_json_gives_defaults(string content)
    {
        File.WriteAllText(Cfg, content);
        var s = ToastConfigStore.Load(Cfg, Env());
        Assert.True(s.EnableToast);
        Assert.Equal("default", s.EnableSource);
    }

    [Fact]
    public void Save_preserves_other_keys()
    {
        File.WriteAllText(Cfg, """{ "foo": 1, "toastTheme": "light", "enableToast": true }""");
        Assert.True(ToastConfigStore.SaveEnableToast(Cfg, false));
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Cfg));
        Assert.Equal(1, (int)json["foo"]!);
        Assert.Equal("light", (string?)json["toastTheme"]);   // retired, but never deleted from the user's file
        Assert.False((bool)json["enableToast"]!);
    }

    [Fact]
    public void Save_creates_missing_directory()
    {
        var nested = Path.Combine(_dir, "a", "b", ToastConfigStore.FileName);
        Assert.True(ToastConfigStore.SaveEnableToast(nested, false));
        Assert.False(ToastConfigStore.Load(nested, Env()).EnableToast);
    }

    [Fact]
    public void Save_refuses_to_overwrite_a_file_it_cannot_parse()
    {
        File.WriteAllText(Cfg, "{ not json");
        Assert.False(ToastConfigStore.SaveEnableToast(Cfg, false));
        Assert.Equal("{ not json", File.ReadAllText(Cfg));
        Assert.False(File.Exists(Cfg + ".tmp"));
    }

    [Fact]
    public void Save_over_a_blank_file_writes_a_fresh_object()
    {
        File.WriteAllText(Cfg, "   ");
        Assert.True(ToastConfigStore.SaveEnableToast(Cfg, false));
        Assert.False(ToastConfigStore.Load(Cfg, Env()).EnableToast);
    }

    [Fact]
    public void Show_branding_defaults_off_and_reads_only_a_json_boolean()
    {
        Assert.False(ToastConfigStore.Load(Cfg, Env()).ShowBranding);
        File.WriteAllText(Cfg, """{ "showBranding": "yes" }""");
        Assert.False(ToastConfigStore.Load(Cfg, Env()).ShowBranding);
        File.WriteAllText(Cfg, """{ "showBranding": true }""");
        Assert.True(ToastConfigStore.Load(Cfg, Env()).ShowBranding);
    }

    [Fact]
    public void Save_show_branding_round_trips_and_keeps_enable_toast()
    {
        File.WriteAllText(Cfg, """{ "foo": 1, "enableToast": false }""");
        Assert.True(ToastConfigStore.SaveShowBranding(Cfg, true));
        var s = ToastConfigStore.Load(Cfg, Env());
        Assert.True(s.ShowBranding);
        Assert.False(s.EnableToast);
        Assert.Equal(1, (int)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Cfg))["foo"]!);

        Assert.True(ToastConfigStore.SaveShowBranding(Cfg, false));
        Assert.False(ToastConfigStore.Load(Cfg, Env()).ShowBranding);
    }

    [Fact]
    public void Save_show_branding_refuses_to_overwrite_a_file_it_cannot_parse()
    {
        File.WriteAllText(Cfg, "{ not json");
        Assert.False(ToastConfigStore.SaveShowBranding(Cfg, true));
        Assert.Equal("{ not json", File.ReadAllText(Cfg));
    }

    [Fact]
    public void Default_path_is_in_the_descriptor_dir()
        => Assert.Equal(Path.Combine(@"C:\x\ipt-mcp", "iptmcp.config.json"), ToastConfigStore.DefaultPath(@"C:\x\ipt-mcp"));

    [Theory]
    [InlineData(null, 20)]
    [InlineData("10", 10)]
    [InlineData("20", 20)]
    [InlineData("30", 30)]
    [InlineData("60", 60)]
    [InlineData("15", 20)]
    [InlineData("0", 20)]
    [InlineData("-1", 20)]
    [InlineData("2147483648", 20)]
    [InlineData("30.0", 20)]
    [InlineData("\"30\"", 20)]
    [InlineData("true", 20)]
    [InlineData("{}", 20)]
    [InlineData("null", 20)]
    public void Idle_duration_loads_only_supported_integer_values(string? jsonValue, int expected)
    {
        if (jsonValue != null) File.WriteAllText(Cfg, "{\"toastIdleSeconds\":" + jsonValue + "}");
        Assert.Equal(expected, ToastConfigStore.LoadIdleSeconds(Cfg));
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(15, 20)]
    public void Idle_duration_round_trips_without_changing_other_preferences(int seconds, int expected)
    {
        File.WriteAllText(Cfg, """{ "foo": 1, "enableToast": false, "toastDragOffset": { "x": 12, "y": 34 } }""");
        Assert.True(ToastConfigStore.SaveIdleSeconds(Cfg, seconds));
        Assert.Equal(expected, ToastConfigStore.LoadIdleSeconds(Cfg));
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Cfg));
        Assert.Equal(1, (int)json["foo"]!);
        Assert.False((bool)json["enableToast"]!);
        Assert.Equal(12, (int)json["toastDragOffset"]!["x"]!);
        Assert.Equal(34, (int)json["toastDragOffset"]!["y"]!);
    }

    [Fact]
    public void Failed_idle_save_keeps_the_existing_file()
    {
        File.WriteAllText(Cfg, "{ malformed");
        Assert.Equal(20, ToastConfigStore.LoadIdleSeconds(Cfg));
        Assert.False(ToastConfigStore.SaveIdleSeconds(Cfg, 60));
        Assert.Equal("{ malformed", File.ReadAllText(Cfg));
        Assert.False(File.Exists(Cfg + ".tmp"));
    }
}
