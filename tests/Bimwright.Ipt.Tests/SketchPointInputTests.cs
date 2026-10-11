using System.Text;
using Bimwright.Ipt.Shared.Handlers.Sketch;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class SketchPointInputTests
{
    private static string Token(string sketch = "AQ==", string document = "doc", string path = "C:\\part.ipt", string point = "Ag==")
        => new SketchPointToken(document, path, sketch, point).Encode();
    private static JObject Input() => new() { ["point1_ref"] = Token(point: "origin"), ["point2_ref"] = Token(), ["orientation"] = "horizontal", ["value"] = 25 };

    [Theory]
    [InlineData("horizontal")]
    [InlineData("vertical")]
    [InlineData("aligned")]
    public void Point_dimension_keeps_numeric_mm_and_orientation(string orientation)
    {
        var p = Input(); p["orientation"] = orientation;
        var result = SketchPointDimensionInput.Parse(p);
        Assert.Equal(25, result.ValueMm); Assert.Null(result.Expression); Assert.Equal(orientation, result.Orientation);
    }
    [Fact]
    public void Point_dimension_preserves_expression_and_label_position()
    {
        var p = Input(); p["value"] = "HoleX / 2"; p["text_position"] = new JArray(-15, 23.5);
        var result = SketchPointDimensionInput.Parse(p);
        Assert.Equal("HoleX / 2", result.Expression); Assert.Null(result.ValueMm); Assert.Equal(new[] { -15.0, 23.5 }, result.TextPosition);
    }
    [Theory]
    [InlineData("point1_ref", "1")]
    [InlineData("point2_ref", "skpt1:!")]
    [InlineData("orientation", "diagonal")]
    public void Point_dimension_rejects_invalid_selectors(string field, string value)
    { var p = Input(); p[field] = value; Assert.Throws<ArgumentException>(() => SketchPointDimensionInput.Parse(p)); }
    [Theory]
    [InlineData("sketch")]
    [InlineData("document")]
    [InlineData("path")]
    public void Point_dimension_rejects_cross_scope_references(string scope)
    {
        var p = Input(); p["point2_ref"] = Token(sketch: scope == "sketch" ? "Aw==" : "AQ==", document: scope == "document" ? "other" : "doc", path: scope == "path" ? "C:\\copy.ipt" : "C:\\part.ipt");
        Assert.Throws<ArgumentException>(() => SketchPointDimensionInput.Parse(p));
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("\"\"")]
    public void Point_dimension_rejects_invalid_values(string json)
    { var p = Input(); p["value"] = JToken.Parse(json); Assert.Throws<ArgumentException>(() => SketchPointDimensionInput.Parse(p)); }
    [Theory]
    [InlineData("[1]")]
    [InlineData("[1,2,3]")]
    [InlineData("[1,\"2\"]")]
    public void Point_dimension_rejects_invalid_label_positions(string json)
    { var p = Input(); p["text_position"] = JToken.Parse(json); Assert.Throws<ArgumentException>(() => SketchPointDimensionInput.Parse(p)); }
    [Fact]
    public void Point_reference_rejects_duplicate_fields_trailing_json_and_oversize()
    {
        foreach (var raw in new[] { "{d:'doc',d:'other',f:'',s:'AQ==',p:'Ag=='}", "{d:'doc',f:'',s:'AQ==',p:'Ag=='}{}", "{d:'doc',f:'',s:'',p:'origin'}" })
            Assert.Throws<ArgumentException>(() => SketchPointToken.Decode("skpt1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))));
        Assert.Throws<ArgumentException>(() => SketchPointToken.Decode("skpt1:" + new string('A', 5000)));
    }
}
