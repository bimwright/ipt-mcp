using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Host-free contracts behind the cycle-2 typed tools: the shared occurrence selector (S3.2),
/// occurrence poses (E3) and the create_part recipe (E2).
/// </summary>
public sealed class TypedToolContractTests
{
    // ---- occurrence selector -------------------------------------------------------------

    private static OccurrenceSelectorSpec Sel(string json)
    {
        Assert.True(OccurrenceSelectorSpec.TryParse(JToken.Parse(json), "selector", out var s, out var e), e);
        return s;
    }

    [Fact]
    public void Null_selector_selects_everything_unsuppressed()
    {
        Assert.True(OccurrenceSelectorSpec.TryParse(null, "selector", out var s, out _));
        Assert.False(s.HasCriteria);
        Assert.True(s.Matches("A:1", "A:1", null, 1, true, false));
        Assert.False(s.Matches("A:1", "A:1", null, 1, true, suppressed: true));
    }

    [Fact]
    public void Bare_string_and_array_are_names_shorthand()
    {
        Assert.Equal(new[] { "BEAM*" }, Sel("\"BEAM*\"").Names);
        Assert.Equal(new[] { "A", "B" }, Sel("[\"A\",\"B\"]").Names);
    }

    [Theory]
    [InlineData("{\"names\":[\"BEAM*\"]}", "BEAM-200:3", "SUB:1/BEAM-200:3", true)]
    [InlineData("{\"names\":[\"beam*\"]}", "BEAM-200:3", "BEAM-200:3", true)]
    [InlineData("{\"names\":[\"PLATE*\"]}", "FRAME-PLATE:1", "FRAME-PLATE:1", false)]
    [InlineData("{\"names\":[\"SUB:1/*\"]}", "BEAM:1", "SUB:1/BEAM:1", true)]
    [InlineData("{\"names\":[\"SUB:2/*\"]}", "BEAM:1", "SUB:1/BEAM:1", false)]
    [InlineData("{\"regex\":\"^SUB:\\\\d+/BEAM\"}", "BEAM:1", "SUB:7/BEAM:1", true)]
    [InlineData("{\"path_contains\":\"stair\"}", "X:1", "STAIR-FRAME:1/X:1", true)]
    public void Name_path_and_regex_matching(string selector, string name, string path, bool expected)
        => Assert.Equal(expected, Sel(selector).Matches(name, path, null, path.Count(c => c == '/') + 1, true, false));

    [Theory]
    [InlineData("{\"file\":\"FRAME-*.ipt\"}", @"D:\p\parts\FRAME-SHS.ipt", true)]
    [InlineData("{\"file\":\"FRAME-*.ipt\"}", @"D:\p\parts\PLATE.ipt", false)]
    [InlineData("{\"file\":\"*\\\\parts\\\\*.ipt\"}", @"D:\p\parts\PLATE.ipt", true)]
    [InlineData("{\"file\":\"*/old/*\"}", @"D:\p\parts\PLATE.ipt", false)]
    public void File_glob_matches_name_or_full_path(string selector, string file, bool expected)
        => Assert.Equal(expected, Sel(selector).Matches("X:1", "X:1", file, 1, true, false));

    [Fact]
    public void Leaf_depth_and_suppression_filters()
    {
        var leaf = Sel("{\"leaf\":true}");
        Assert.False(leaf.Matches("SUB:1", "SUB:1", null, 1, isLeaf: false, false));
        Assert.True(leaf.Matches("P:1", "SUB:1/P:1", null, 2, isLeaf: true, false));

        var top = Sel("{\"max_depth\":1}");
        Assert.False(top.Matches("P:1", "SUB:1/P:1", null, 2, true, false));

        var sup = Sel("{\"include_suppressed\":true}");
        Assert.True(sup.Matches("P:1", "P:1", null, 1, true, suppressed: true));
    }

    [Theory]
    [InlineData("{\"nmes\":[\"x\"]}", "unknown key")]
    [InlineData("{\"regex\":\"(\"}", "not a valid regular expression")]
    [InlineData("{\"limit\":0}", "limit")]
    [InlineData("{\"max_depth\":\"2\"}", "max_depth")]
    [InlineData("{\"names\":[1]}", "names")]
    [InlineData("42", "selector object")]
    public void Invalid_selectors_are_rejected(string json, string expected)
    {
        Assert.False(OccurrenceSelectorSpec.TryParse(JToken.Parse(json), "selector", out _, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Suggestions_prefer_substring_hits()
    {
        var s = Sel("{\"names\":[\"PLATE*\"]}");
        var near = s.Suggest(new[] { "BOLT:1", "FRAME-PLATE:1", "NUT:2", "FRAME-SHS:1" }, 2);
        Assert.Equal("FRAME-PLATE:1", near[0]);
    }

    // ---- pose ----------------------------------------------------------------------------

    private static PoseSpec Pose(string json)
    {
        Assert.True(PoseSpec.TryParse(JToken.Parse(json), "pose", out var p, out var e), e);
        return p;
    }

    private static void Near(double[] expected, double[] actual)
    {
        for (var i = 0; i < 3; i++) Assert.Equal(expected[i], actual[i], 9);
    }

    [Fact]
    public void Origin_is_converted_to_cm_and_identity_axes_by_default()
    {
        var p = Pose("{\"origin_mm\":[100,-20,5]}");
        Near(new[] { 10.0, -2.0, 0.5 }, p.OriginCm);
        Near(new[] { 1.0, 0, 0 }, p.X);
        Near(new[] { 0, 0, 1.0 }, p.Z);
    }

    [Fact]
    public void Rotation_z_90_turns_x_into_y()
    {
        var p = Pose("{\"rotation_deg\":[0,0,90]}");
        Near(new[] { 0, 1.0, 0 }, p.X);
        Near(new[] { -1.0, 0, 0 }, p.Y);
        Near(new[] { 0, 0, 1.0 }, p.Z);
    }

    [Fact]
    public void Axes_are_normalised_and_z_is_their_cross_product()
    {
        var p = Pose("{\"origin_mm\":[0,0,0],\"x_axis\":[0,2,0],\"y_axis\":[0,0,5]}");
        Near(new[] { 0, 1.0, 0 }, p.X);
        Near(new[] { 0, 0, 1.0 }, p.Y);
        Near(new[] { 1.0, 0, 0 }, p.Z);
    }

    [Fact]
    public void Matrix_form_reads_columns_as_axes_and_translation_in_mm()
    {
        var p = Pose("{\"matrix\":[0,-1,0,500, 1,0,0,0, 0,0,1,20, 0,0,0,1]}");
        Near(new[] { 0, 1.0, 0 }, p.X);
        Near(new[] { -1.0, 0, 0 }, p.Y);
        Near(new[] { 50.0, 0, 2.0 }, p.OriginCm);
    }

    [Theory]
    [InlineData("{\"x_axis\":[1,0,0],\"y_axis\":[1,1,0]}", "orthogonal")]
    [InlineData("{\"x_axis\":[1,0,0]}", "x_axis and y_axis")]
    [InlineData("{\"rotation_deg\":[0,0,90],\"x_axis\":[1,0,0],\"y_axis\":[0,1,0]}", "not both")]
    [InlineData("{\"matrix\":[1,2,3]}", "16 numbers")]
    [InlineData("{\"origin\":[1,2,3]}", "unknown key")]
    [InlineData("{\"origin_mm\":[1,2]}", "origin_mm")]
    public void Invalid_poses_are_rejected(string json, string expected)
    {
        Assert.False(PoseSpec.TryParse(JToken.Parse(json), "pose", out _, out var error));
        Assert.Contains(expected, error);
    }

    // ---- create_part recipe --------------------------------------------------------------

    private const string Bracket = """
    { "save_as": "C:\\out\\B.ipt", "material": "Steel",
      "iproperties": { "Part Number": "B-1", "Title": "Bracket", "Custom:Zone": 3 },
      "parameters": [ { "name": "t", "expression": 12 } ],
      "features": [
        { "sketch": { "plane": "XY", "profile": { "polyline": [[0,0],[200,0],[200,80,0.41421356],[160,120],[0,120],[0,0]],
                                                  "inner": [ { "circle": { "d": 30, "center": [100,60] } } ] } } },
        { "extrude": { "distance": "t" } },
        { "sketch": { "name": "Rib", "plane": { "origin_mm": [0,60,0], "x_axis": [1,0,0], "y_axis": [0,0,1] }, "profile": { "rect": [40, 48] } } },
        { "extrude": { "distance": 8, "direction": "symmetric" } },
        { "hole": { "face": { "normal": "+z" }, "at": [[180,20,12]], "diameter": 9, "depth": 5 } },
        { "fillet": { "radius": 2, "edges": { "kind": "circular" } } }
      ] }
    """;

    [Fact]
    public void Full_recipe_parses_into_ordered_features()
    {
        Assert.True(PartRecipe.TryParse(JToken.Parse(Bracket), out var r, out var e), e);
        Assert.Equal(6, r.Features.Count);
        Assert.Equal(new[] { "sketch", "extrude", "sketch", "extrude", "hole", "fillet" }, r.Features.Select(f => f.Kind).ToArray());

        var outline = (PartRecipe.SketchFeature)r.Features[0];
        Assert.Equal("MCP_Sketch1", outline.Name);
        var poly = (PartRecipe.Polyline)outline.Shapes[0];
        Assert.Equal(5, poly.Points.Count);   // repeated closing point dropped
        Assert.Single(poly.Inner);

        var ex1 = (PartRecipe.ExtrudeFeature)r.Features[1];
        Assert.Equal("MCP_Sketch1", ex1.Sketch);   // defaults to the last sketch
        Assert.Equal("t", (string?)ex1.Distance);
        Assert.Equal("Rib", ((PartRecipe.ExtrudeFeature)r.Features[3]).Sketch);

        var rib = (PartRecipe.SketchFeature)r.Features[2];
        Assert.NotNull(rib.PlanePose);
        var rect = (PartRecipe.Rect)rib.Shapes[0];
        Assert.Equal(-20, rect.X1);
        Assert.Equal(24, rect.Y2);

        var hole = (PartRecipe.PassThroughFeature)r.Features[4];
        Assert.Equal("hole", hole.Command);
        Assert.Equal("+Z", (string?)hole.Params["face"]!["normal"]);
        Assert.False((bool)hole.Params["through"]!);
        Assert.Equal(5, (double)hole.Params["depth_mm"]!);
        Assert.Equal(180, (double)hole.Params["points_mm"]![0]![0]!);

        Assert.Contains(("Design Tracking Properties", "Part Number", "B-1"), r.IProperties);
        Assert.Contains(("Inventor Summary Information", "Title", "Bracket"), r.IProperties);
        Assert.Contains(("Custom", "Zone", "3"), r.IProperties);
        Assert.Equal(("t", "12", "mm"), r.Parameters[0]);
        Assert.True(r.Plan().Count >= 8);
    }

    [Theory]
    [InlineData("{\"features\":[]}", "recipe.features")]
    [InlineData("{\"features\":[{\"extrude\":{\"distance\":5}}]}", "features[0].extrude.sketch")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}}]}", "needs at least one extrude")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":0}}]}", "features[1].extrude.distance")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5,\"sketch\":\"nope\"}}]}", "unknown sketch 'nope'")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"hexagon\":{}}}},{\"extrude\":{\"distance\":5}}]}", "features[0].sketch.profile")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"polyline\":[[0,0],[1,0],[1,0],[0,1]]}}},{\"extrude\":{\"distance\":5}}]}", "duplicate consecutive point")]
    [InlineData("{\"features\":[{\"revolve\":{}}]}", "unknown feature 'revolve'")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5}},{\"hole\":{\"face\":{\"normal\":\"+z\"},\"at\":[[0,0,5]],\"diameter\":3}}]}", "exactly one of through")]
    [InlineData("{\"save_as\":\"C:\\\\x.stp\",\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5}}]}", "recipe.save_as")]
    [InlineData("{\"close_after\":true,\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5}}]}", "recipe.close_after")]
    [InlineData("{\"features\":[{\"sketch\":{\"plane\":{\"origin_mm\":[0,0,0]},\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5}}]}", "features[0].sketch.plane")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5,\"operation\":\"add\"}}]}", "features[1].extrude.operation")]
    [InlineData("{\"feature\":[]}", "unknown key 'feature'")]
    [InlineData("{\"features\":[{\"sketch\":{\"name\":\"PLATE\",\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5,\"name\":\"PLATE\"}}]}", "features[1].extrude.name")]
    [InlineData("{\"features\":[{\"sketch\":{\"profile\":{\"rect\":[10,10]}}},{\"extrude\":{\"distance\":5,\"name\":\"BODY\"}},{\"sketch\":{\"name\":\"BODY\",\"profile\":{\"rect\":[5,5]}}},{\"extrude\":{\"distance\":2}}]}", "browser names must be unique")]
    public void Recipe_errors_name_their_json_path(string json, string expected)
    {
        Assert.False(PartRecipe.TryParse(JToken.Parse(json), out _, out var error));
        Assert.Contains(expected, error);
    }
}
