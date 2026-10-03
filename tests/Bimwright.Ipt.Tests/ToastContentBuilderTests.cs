using Bimwright.Ipt.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Toast.Tests;

public sealed class ToastContentBuilderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-toast-content-" + Guid.NewGuid().ToString("N"));
    public ToastContentBuilderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ToastModel Ok(string command, JToken? data, bool? isReadOnly = true, long ms = 42)
        => ToastContentBuilder.Build(new ToastEvent(command, true, data, null, null, ms, isReadOnly));

    [Theory]
    [InlineData("completed", true)]
    [InlineData("cancelled", true)]   // ended on purpose: not an error card
    [InlineData("failed", false)]
    public void Task_report_success_follows_the_outcome(string outcome, bool expected)
    {
        var m = Ok("report_task_result",
            new JObject { ["task_id"] = "job-1", ["outcome"] = outcome, ["summary"] = "Checked 12 parts" });
        Assert.Equal(expected, m.Success);
        Assert.Equal("job-1", m.TaskId);
        Assert.Equal(outcome, m.Outcome);
    }

    [Fact]
    public void Tool_result_success_follows_the_envelope()
    {
        Assert.True(Ok("list_open_documents", new JObject()).Success);
        Assert.False(ToastContentBuilder.Build(
            new ToastEvent("extrude", false, null, "API_ERROR", "Profile is not closed", 12, false)).Success);
    }

    [Fact]
    public void Only_a_real_local_image_is_a_safe_thumbnail_path()
    {
        var png = Path.Combine(_dir, "shot.png");
        File.WriteAllBytes(png, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        var text = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(text, "x");
        Assert.True(ToastContentBuilder.IsSafeImagePath(png));
        Assert.False(ToastContentBuilder.IsSafeImagePath(text));
        Assert.False(ToastContentBuilder.IsSafeImagePath(Path.Combine(_dir, "gone.png")));
        Assert.False(ToastContentBuilder.IsSafeImagePath(null));
        Assert.False(ToastContentBuilder.IsSafeImagePath(@"\\server\share\shot.png"));
    }

    [Fact]
    public void Capture_in_file_mode_has_safe_image_metadata_and_history_hint()
    {
        var png = Path.Combine(_dir, "capture-1.png");
        File.WriteAllBytes(png, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        var m = Ok("capture_view", new JObject { ["path"] = png, ["width"] = 1280, ["height"] = 720, ["bytes"] = 4 });

        Assert.True(m.Success);
        Assert.Equal("Saved capture-1.png · 1280×720", m.Summary);
        Assert.Equal("Click to open History", m.Detail);
        Assert.Equal(Path.GetFullPath(png), m.ThumbnailPath);
        Assert.Equal("MCP · Snapshot", m.Category);
    }

    [Fact]
    public void Capture_with_missing_file_has_no_thumbnail()
    {
        var m = Ok("capture_view", new JObject { ["output_path"] = Path.Combine(_dir, "gone.png"), ["width"] = 10, ["height"] = 10 });
        Assert.Null(m.ThumbnailPath);
        Assert.Equal("", m.Detail);
    }

    [Fact]
    public void Capture_inline_mode()
    {
        var m = Ok("capture_view", new JObject { ["base64"] = "AAAA", ["width"] = 640, ["height"] = 480 });
        Assert.Equal("Captured image · 640×480", m.Summary);
        Assert.Equal("Returned inline", m.Detail);
        Assert.Null(m.ThumbnailPath);
    }

    [Fact]
    public void Multiple_captures_report_the_count_and_preview_the_latest_image()
    {
        var first = Path.Combine(_dir, "front.png");
        var last = Path.Combine(_dir, "back.png");
        File.WriteAllBytes(first, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllBytes(last, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        var m = Ok("capture_view", new JObject
        {
            ["count"] = 2,
            ["captures"] = new JArray(
                new JObject { ["path"] = first, ["width"] = 1280, ["height"] = 720 },
                new JObject { ["path"] = last, ["width"] = 1280, ["height"] = 720 }),
        }, false);

        Assert.Equal("Captured 2 images", m.Summary);
        Assert.Equal("Click to open History", m.Detail);
        Assert.Equal(Path.GetFullPath(last), m.ThumbnailPath);
        Assert.Equal("MCP · Snapshot", m.Category);
    }

    [Fact]
    public void Multiple_captures_with_missing_files_still_report_the_count()
    {
        var m = Ok("capture_view", new JObject
        {
            ["count"] = 2,
            ["captures"] = new JArray(
                new JObject { ["path"] = Path.Combine(_dir, "gone-1.png") },
                new JObject { ["path"] = Path.Combine(_dir, "gone-2.png") }),
        }, false);

        Assert.Equal("Captured 2 images", m.Summary);
        Assert.Null(m.ThumbnailPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Document_query_identifies_the_document_without_claiming_a_save(bool dirty)
    {
        var m = Ok("get_document_info", new JObject
        {
            ["title"] = "Sample.ipt", ["path"] = @"C:\example\Sample.ipt", ["dirty"] = dirty,
        });

        Assert.Equal("Document Sample.ipt", m.Summary);
        Assert.Equal(dirty ? "Unsaved changes" : "", m.Detail);
        Assert.Equal(ToolActivityKind.Read, m.Kind);
    }

    [Theory]
    [InlineData("new_part", "Created Part1")]
    [InlineData("new_assembly", "Created Part1")]
    [InlineData("open_document", "Opened Part1")]
    public void Document_lifecycle_result_uses_the_returned_title(string command, string expected)
        => Assert.Equal(expected, Ok(command, new JObject { ["title"] = "Part1", ["path"] = null }, false).Summary);

    [Fact]
    public void Material_and_units_results_show_the_applied_value()
    {
        Assert.Equal("Material Steel", Ok("set_material", new JObject { ["material_name"] = "Steel" }, false).Summary);
        Assert.Equal("Units Millimeter", Ok("set_units", new JObject { ["length_unit"] = "kMillimeterLengthUnits" }, false).Summary);
    }

    [Fact]
    public void Send_code_shows_first_line_of_result()
    {
        var m = Ok("send_code", new JObject { ["ok"] = true, ["error"] = null, ["result"] = "made 12 holes\nsecond line" }, isReadOnly: false);
        Assert.True(m.Success);
        Assert.Equal("made 12 holes", m.Summary);
        Assert.Equal("C# script ran in Inventor", m.Detail);
        Assert.Equal("MCP · Script", m.Category);
    }

    [Fact]
    public void Send_code_falls_back_to_stdout_then_generic()
    {
        Assert.Equal("hello", Ok("send_code", new JObject { ["ok"] = true, ["result"] = null, ["stdout"] = "\nhello\nworld" }, false).Summary);
        Assert.Equal("Script finished", Ok("send_code", new JObject { ["ok"] = true, ["result"] = null, ["stdout"] = "" }, false).Summary);
    }

    [Fact]
    public void Send_code_array_and_object_results_are_counted_not_serialized()
    {
        Assert.Equal("Result: 3 items", Ok("send_code", new JObject { ["ok"] = true, ["result"] = new JArray(1, 2, 3) }, false).Summary);
        Assert.Equal("Result: 1 field", Ok("send_code", new JObject { ["ok"] = true, ["result"] = new JObject { ["a"] = 1 } }, false).Summary);
    }

    [Fact]
    public void Huge_single_line_result_is_truncated()   // Review Focus 3
    {
        var huge = new string('x', 2_000_000);
        var m = Ok("send_code", new JObject { ["ok"] = true, ["result"] = huge }, false);
        Assert.True(m.Summary.Length <= ToastContentBuilder.SummaryMax);
        Assert.EndsWith("…", m.Summary);
    }

    [Fact]
    public void Soft_send_code_failure_is_an_error_toast()   // Review Focus 4
    {
        var m = Ok("send_code", new JObject { ["ok"] = false, ["error"] = "compile error: CS0103 name 'x' does not exist" }, false);
        Assert.False(m.Success);
        Assert.Equal("MCP · Failed", m.Category);
        Assert.Equal("compile error: CS0103 name 'x' does not exist", m.Summary);
        Assert.Equal("Script error", m.Detail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Structured_payload_error_survives_an_already_failed_envelope(bool envelopeOk)
    {
        var data = new JObject
        {
            ["ok"] = false, ["rolled_back"] = true,
            ["error"] = new JObject { ["code"] = "INVALID_ARGUMENT", ["message"] = "Geometry is ambiguous." },
        };
        // RecordOutcome can already have marked the envelope failed and serialized its error object.
        var m = ToastContentBuilder.Build(new ToastEvent("add_drawing_dimension", envelopeOk, data,
            null, envelopeOk ? null : data["error"]!.ToString(), 5, false));

        Assert.False(m.Success);
        Assert.Equal("Geometry is ambiguous.", m.Summary);
        Assert.Equal("INVALID_ARGUMENT", m.Detail);
        Assert.Equal("MCP · Failed", m.Category);
    }

    [Fact]
    public void Batch_rolled_back_is_an_error_toast()   // Review Focus 4
    {
        var m = Ok("batch_execute", new JObject { ["executed"] = 3, ["rolled_back"] = true }, false);
        Assert.False(m.Success);
        Assert.Equal("Batch rolled back after a failed step", m.Summary);
    }

    [Fact]
    public void Batch_success()
        => Assert.Equal("5 commands ran", Ok("batch_execute", new JObject { ["executed"] = 5, ["rolled_back"] = false }, false).Summary);

    [Fact]
    public void Interference()
    {
        Assert.Equal("No interference", Ok("check_interference", new JObject { ["count"] = 0, ["total_volume_mm3"] = 0.0 }).Summary);
        var m = Ok("check_interference", new JObject { ["count"] = 2, ["total_volume_mm3"] = 12.5 });
        Assert.Equal("2 interfering pairs", m.Summary);
        Assert.Equal("Total 12.5 mm³", m.Detail);
    }

    [Fact]
    public void Mass_properties_switch_to_kg()
    {
        var kg = Ok("get_mass_properties", new JObject { ["mass_g"] = 2500.0, ["volume_mm3"] = 1000.0 });
        Assert.Equal("Mass 2.5 kg", kg.Summary);
        Assert.Equal("Volume 1000 mm³", kg.Detail);
        Assert.Equal("Mass 12.346 g", Ok("get_mass_properties", new JObject { ["mass_g"] = 12.3456, ["volume_mm3"] = 1.0 }).Summary);
    }

    [Fact]
    public void Export_shows_file_name()
    {
        var m = Ok("export_step", new JObject { ["output_path"] = @"C:\out\part.step" }, false);
        Assert.Equal("Saved part.step", m.Summary);
        Assert.Equal("Export STEP", m.Title);
        Assert.Equal("MCP · Export", m.Category);
    }

    [Fact]
    public void Generic_count_name_message_and_fallback()
    {
        Assert.Equal("7 items", Ok("list_parameters", new JObject { ["count"] = 7 }).Summary);
        Assert.Equal("1 item", Ok("list_bodies", new JObject { ["total"] = 1 }).Summary);
        Assert.Equal("Extrusion3", Ok("extrude", new JObject { ["feature_name"] = "Extrusion3" }, false).Summary);
        Assert.Equal("done here", Ok("close_sketch", new JObject { ["message"] = "done here" }, false).Summary);
        Assert.Equal("View Fit", Ok("view_fit", new JObject(), false).Summary);
    }

    [Theory]   // Review Focus 3: data that is not an object never throws
    [InlineData("null")]
    [InlineData("\"plain string\"")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    public void Non_object_data_uses_readable_tool_name(string json)
    {
        var m = Ok("get_document_info", JToken.Parse(json));
        Assert.True(m.Success);
        Assert.Equal("Get Document Info", m.Summary);
    }

    [Fact]
    public void Null_data_uses_readable_tool_name()
        => Assert.Equal("New Part", Ok("new_part", null, false).Summary);

    [Fact]
    public void Timeout_failure()
    {
        var m = ToastContentBuilder.Build(new ToastEvent("extrude", false, null, "TIMEOUT", "Inventor did not answer within 30000 ms", 30001, false));
        Assert.False(m.Success);
        Assert.Equal("Inventor did not answer within 30000 ms", m.Summary);
        Assert.Equal("TIMEOUT", m.Detail);
        Assert.Equal(ToolActivityKind.Write, m.Kind);
        Assert.Equal(30001, m.DurationMs);
    }

    [Fact]
    public void Unknown_command_failure()
    {
        var m = ToastContentBuilder.Build(new ToastEvent("frobnicate", false, null, "INVALID_ARGUMENT", "unknown command 'frobnicate'\nmore", 3, null));
        Assert.Equal(ToolActivityKind.Read, m.Kind);
        Assert.Equal("Frobnicate", m.Title);
        Assert.Equal("unknown command 'frobnicate'", m.Summary);
        Assert.Equal("INVALID_ARGUMENT", m.Detail);
    }

    [Fact]
    public void Failure_without_message()
        => Assert.Equal("Command failed", ToastContentBuilder.Build(new ToastEvent("extrude", false, null, null, null, 1, false)).Summary);

    [Fact]
    public void Empty_command_is_named_unknown()
        => Assert.Equal("unknown", Ok("", null).Command);
}
