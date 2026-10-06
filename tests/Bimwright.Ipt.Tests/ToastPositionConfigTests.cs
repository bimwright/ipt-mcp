using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ToastPositionConfigTests
{
    [Fact]
    public void Position_roundtrips_and_preserves_other_preferences()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ipt-toast-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "config.json");
        try
        {
            File.WriteAllText(path, "{\"enableToast\":false,\"showBranding\":true,\"other\":42}");
            Assert.True(ToastConfigStore.SavePosition(path, new ToastPositionOptions(true, true, true, -120, 25)));
            var loaded = ToastConfigStore.LoadPosition(path);
            Assert.True(loaded.Right && loaded.Bottom && loaded.DragEnabled);
            Assert.Equal(-120, loaded.OffsetX);
            Assert.Equal(25, loaded.OffsetY);
            Assert.True(ToastConfigStore.SaveEnableToast(path, true));
            Assert.True(ToastConfigStore.LoadPosition(path).HasOffset);
            Assert.True(ToastConfigStore.SavePosition(path, loaded.WithCorner(false, true)));
            Assert.False(ToastConfigStore.LoadPosition(path).HasOffset);
            var json = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(42, (int)json["other"]!);
            Assert.True((bool)json["showBranding"]!);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("{\"toastHorizontalAlign\":true,\"toastVerticalAlign\":12,\"toastDragEnabled\":\"true\",\"toastDragOffset\":{\"x\":1}}")]
    [InlineData("{\"toastDragOffset\":{\"x\":\"NaN\",\"y\":2}}")]
    [InlineData("[]")]
    [InlineData("broken json")]
    public void Invalid_values_use_safe_defaults_without_clobbering_input(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "ipt-toast-invalid-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, text);
            var loaded = ToastConfigStore.LoadPosition(path);
            Assert.False(loaded.Right || loaded.Bottom || loaded.DragEnabled || loaded.HasOffset);
            if (text == "[]" || text == "broken json")
            {
                Assert.False(ToastConfigStore.SavePosition(path, new ToastPositionOptions(true)));
                Assert.Equal(text, File.ReadAllText(path));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Native_model_duration_and_inline_capture_reach_timeline()
    {
        var activity = new ActivityAggregator();
        activity.Record(new ToastModel("capture_sheet", "Capture Sheet", "", "Saved", "", null,
            ToolActivityKind.Read, true, 152), true);
        var card = activity.TakeRender().Card;
        Assert.Equal(1, card.Images);
        Assert.Equal(152, card.RecentEntries[0].DurationMs);
        Assert.Contains("152 ms", card.RecentEntries[0].TooltipText);
    }
}
