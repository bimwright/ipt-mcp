using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class DrawingToastTests
{
    [Theory]
    [InlineData("add_drawing_note", true, "Added note Fixture")]
    [InlineData("add_drawing_note", false, "Reused note Fixture")]
    [InlineData("add_drawing_table", true, "Added table Fixture")]
    [InlineData("add_drawing_table", false, "Reused table Fixture")]
    public void Note_table_toast_identifies_created_or_reused_item(string command, bool created, string expected)
    {
        var model = ToastContentBuilder.Build(new ToastEvent(command, true, new JObject { ["created"] = created, ["name"] = "Fixture" }, null, null, 5, false));
        Assert.True(model.Success); Assert.Equal(ToolActivityKind.Write, model.Kind); Assert.Equal(expected, model.Summary);
    }
    [Fact]
    public void Sheet_capture_is_a_write_and_counts_as_a_capture_without_a_thumbnail()
    {
        var model = ToastContentBuilder.Build(new ToastEvent("capture_sheet", true, new JObject { ["width"] = 1600, ["height"] = 1100, ["base64"] = "fixture" }, null, null, 12, false));
        Assert.Equal(ToolActivityKind.Write, model.Kind); Assert.Equal("MCP · Snapshot", model.Category); Assert.True(model.Success);
        var feed = new ToastFeed(); feed.Record(model, true); var card = feed.TakeRender().Card!; Assert.Equal(1, card.Captures); Assert.Equal(1, card.Succeeded);
    }
    [Fact]
    public void Rolled_back_drawing_batch_reports_the_actual_error_and_failed_category()
    {
        var model = ToastContentBuilder.Build(new ToastEvent("add_drawing_dimension", true, new JObject { ["ok"] = false, ["rolled_back"] = true, ["error"] = new JObject { ["message"] = "Geometry is ambiguous." } }, null, null, 5, false));
        Assert.False(model.Success); Assert.Equal("MCP · Failed", model.Category); Assert.Equal("Geometry is ambiguous.", model.Summary);
    }
    [Theory]
    [InlineData("add_drawing_dimension", "dimensions")]
    [InlineData("add_balloon", "balloons")]
    public void Annotation_toast_reports_effect_count(string command, string noun)
    {
        var model = ToastContentBuilder.Build(new ToastEvent(command, true, new JObject { ["count"] = 3, ["created_count"] = 2 }, null, null, 5, false)); Assert.Contains("2 created", model.Summary); Assert.Contains("3 " + noun, model.Summary);
    }
}
