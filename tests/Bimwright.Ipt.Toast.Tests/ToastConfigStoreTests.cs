using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-toast-cfg-" + Guid.NewGuid().ToString("N"));
    private string Cfg => Path.Combine(_dir, ToastConfigStore.FileName);

    public ToastConfigStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Func<string, string?> Env(string? enable = null, string? theme = null)
        => name => name == ToastConfigStore.EnableEnv ? enable : name == ToastConfigStore.ThemeEnv ? theme : null;

    [Fact]
    public void Missing_file_and_env_gives_defaults()
    {
        var s = ToastConfigStore.Load(Cfg, Env());
        Assert.True(s.EnableToast);
        Assert.Equal(ToastTheme.Auto, s.Theme);
        Assert.Equal("default", s.EnableSource);
        Assert.Equal("default", s.ThemeSource);
    }

    [Fact]
    public void Json_values_are_used()
    {
        File.WriteAllText(Cfg, """{ "enableToast": false, "toastTheme": "Dark" }""");
        var s = ToastConfigStore.Load(Cfg, Env());
        Assert.False(s.EnableToast);
        Assert.Equal(ToastTheme.Dark, s.Theme);
        Assert.Equal("config", s.EnableSource);
        Assert.Equal("config", s.ThemeSource);
    }

    [Fact]
    public void Env_overrides_json()
    {
        File.WriteAllText(Cfg, """{ "enableToast": false, "toastTheme": "dark" }""");
        var s = ToastConfigStore.Load(Cfg, Env(enable: "1", theme: "light"));
        Assert.True(s.EnableToast);
        Assert.Equal(ToastTheme.Light, s.Theme);
        Assert.Equal("env " + ToastConfigStore.EnableEnv, s.EnableSource);
        Assert.Equal("env " + ToastConfigStore.ThemeEnv, s.ThemeSource);
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
        File.WriteAllText(Cfg, """{ "enableToast": false, "toastTheme": "dark" }""");
        var s = ToastConfigStore.Load(Cfg, Env(enable: "maybe", theme: "purple"));
        Assert.False(s.EnableToast);
        Assert.Equal(ToastTheme.Dark, s.Theme);
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
        Assert.Equal(ToastTheme.Auto, s.Theme);
    }

    [Fact]
    public void Save_preserves_other_keys()
    {
        File.WriteAllText(Cfg, """{ "foo": 1, "toastTheme": "light", "enableToast": true }""");
        Assert.True(ToastConfigStore.SaveEnableToast(Cfg, false));
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Cfg));
        Assert.Equal(1, (int)json["foo"]!);
        Assert.Equal("light", (string?)json["toastTheme"]);
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
    public void Default_path_is_in_the_descriptor_dir()
        => Assert.Equal(Path.Combine(@"C:\x\ipt-mcp", "iptmcp.config.json"), ToastConfigStore.DefaultPath(@"C:\x\ipt-mcp"));
}
