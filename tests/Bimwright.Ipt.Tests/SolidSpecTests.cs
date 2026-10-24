using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class SolidSpecTests
{
    private static (bool ok, SolidSpec spec, JArray errors) Parse(string json)
    {
        var ok = SolidSpec.TryParse(JToken.Parse(json), out var spec, out var errors);
        return (ok, spec, errors);
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

    [Fact]
    public void Box_becomes_an_xy_rectangle_extruded_from_min_z()
    {
        var (ok, spec, _) = Parse("[{\"name\":\"plate\",\"box\":{\"min\":[10,20,5],\"max\":[110,70,15]}}]");
        Assert.True(ok);
        var it = Assert.Single(spec.Items);
        Assert.Equal("new", it.Op);
        Assert.Equal(new double[] { 0, 0, 5 }, it.OriginMm);
        Assert.Equal(10, it.DepthMm, 9);
        var r = Assert.IsType<PartRecipe.Rect>(Assert.Single(it.Profile));
        Assert.Equal((10.0, 20.0, 110.0, 70.0), (r.X1, r.Y1, r.X2, r.Y2));
    }

    [Fact]
    public void Tube_frame_points_from_a_to_b_and_wall_makes_a_hole()
    {
        var (ok, spec, _) = Parse("[{\"name\":\"pipe\",\"tube\":{\"a\":[0,0,0],\"b\":[0,300,400],\"od\":60,\"wall\":5}}]");
        Assert.True(ok);
        var it = spec.Items[0];
        Assert.Equal(500, it.DepthMm, 9);
        var n = Cross(it.U, it.V);
        Assert.Equal(0.6, n[1], 9);
        Assert.Equal(0.8, n[2], 9);
        var c = Assert.IsType<PartRecipe.Circle>(it.Profile[0]);
        Assert.Equal(30, c.R, 9);
        Assert.Equal(25, Assert.IsType<PartRecipe.Circle>(Assert.Single(c.Inner)).R, 9);
    }

    [Theory]
    [InlineData("x", 0, 10, 10)]
    [InlineData("y", 1, 10, -10)]
    [InlineData("z", 2, 10, 10)]
    public void Prism_depth_is_signed_along_the_sketch_normal(string axis, int k, double to, double depth)
    {
        var (ok, spec, _) = Parse($"[{{\"name\":\"p\",\"prism\":{{\"axis\":\"{axis}\",\"from\":0,\"to\":{to},\"profile\":{{\"rect\":[20,10]}}}}}}]");
        Assert.True(ok);
        var it = spec.Items[0];
        Assert.Equal(depth, it.DepthMm, 9);
        // the extrusion ends at +to along the axis whatever the frame's handedness
        var axisVec = new double[3];
        axisVec[k] = 1;
        Assert.Equal(to, it.DepthMm * Dot(Cross(it.U, it.V), axisVec), 9);
    }

    [Fact]
    public void Extrude_uses_the_plane_pose_in_mm()
    {
        var (ok, spec, _) = Parse("[{\"name\":\"rib\",\"extrude\":{\"plane\":{\"origin_mm\":[5,6,7],\"x_axis\":[0,1,0],\"y_axis\":[0,0,1]},\"depth\":-8,\"profile\":[{\"circle\":{\"d\":4}}]}}]");
        Assert.True(ok);
        var it = spec.Items[0];
        Assert.Equal(new double[] { 5, 6, 7 }, it.OriginMm.Select(v => Math.Round(v, 9)).ToArray());
        Assert.Equal(-8, it.DepthMm);
    }

    [Fact]
    public void Every_problem_is_reported_with_its_index_and_path()
    {
        var (ok, _, errors) = Parse("""
        [
          {"name":"a","box":{"min":[0,0,0],"max":[10,10,10]}},
          {"name":"a","box":{"min":[0,0,0],"max":[10,10,10]}},
          {"name":"c","op":"cut","box":{"min":[0,0,0],"max":[1,1,1]}},
          {"name":"d","tube":{"a":[0,0,0],"b":[0,0,10],"od":10,"wall":5}},
          {"name":"e","prism":{"axis":"z","from":0,"to":5,"profile":{"polyline":[[0,0],[10,10],[10,0],[0,10]]}}},
          {"name":"f","prism":{"axis":"z","from":0,"to":5,"profile":{"rect":[10,10],"inner":[{"circle":{"d":12}}]}}},
          {"name":"g","prism":{"axis":"z","from":0,"to":5,"profile":[{"rect":[10,10]},{"rect":{"w":10,"h":10,"center":[5,0]}}]}},
          {"name":"h","box":{"min":[0,0,0],"max":[1,1,1]},"tube":{"a":[0,0,0],"b":[1,1,1],"od":1}},
          {"name":"i","op":"join","target":"later","box":{"min":[0,0,0],"max":[1,1,1]}},
          {"name":"later","box":{"min":[0,0,0],"max":[1,1,1]}},
          {"name":"k","colour":"red","box":{"min":[0,0,0],"max":[1,1,1]}}
        ]
        """);
        Assert.False(ok);
        var byIndex = errors.Select(e => ((int)e["index"]!, (string)e["path"]!, (string)e["message"]!)).ToList();
        Assert.Contains(byIndex, e => e.Item1 == 1 && e.Item3.Contains("duplicates items[0]"));
        Assert.Contains(byIndex, e => e.Item1 == 2 && e.Item2 == "items[2].target");
        Assert.Contains(byIndex, e => e.Item1 == 3 && e.Item2 == "items[3].tube.wall");
        Assert.Contains(byIndex, e => e.Item1 == 4 && e.Item3.Contains("crosses itself"));
        Assert.Contains(byIndex, e => e.Item1 == 5 && e.Item2.EndsWith(".inner[0]"));
        Assert.Contains(byIndex, e => e.Item1 == 6 && e.Item2.EndsWith(".profile[1]"));
        Assert.Contains(byIndex, e => e.Item1 == 7 && e.Item3.Contains("exactly one shape"));
        Assert.Contains(byIndex, e => e.Item1 == 8 && e.Item3.Contains("created later by items[9]"));
        Assert.Contains(byIndex, e => e.Item1 == 10 && e.Item2 == "items[10].colour");
        Assert.DoesNotContain(byIndex, e => e.Item1 is 0 or 9);
    }

    [Fact]
    public void Profile_errors_keep_their_recipe_path()
    {
        var (ok, _, errors) = Parse("[{\"name\":\"p\",\"prism\":{\"axis\":\"z\",\"from\":0,\"to\":5,\"profile\":[{\"rect\":[10,10]},{\"rect\":{\"w\":-1,\"h\":2}}]}}]");
        Assert.False(ok);
        Assert.Equal("items[0].prism.profile[1].rect", (string?)errors[0]["path"]);
    }

    [Fact]
    public void Separate_loops_and_holes_inside_their_outer_loop_are_valid()
    {
        var (ok, _, errors) = Parse("""
        [{"name":"flange","prism":{"axis":"z","from":0,"to":12,"profile":[
           {"circle":{"d":200},"inner":[{"circle":{"d":80}},{"circle":{"d":12,"center":[70,0]}},{"circle":{"d":12,"center":[-70,0]}}]},
           {"polyline":[[150,0],[180,0,1],[180,30],[150,30]]}]}}]
        """);
        Assert.True(ok, errors.ToString());
    }

    [Fact]
    public void Defaults_fill_missing_shape_keys_and_items_override_them()
    {
        var ok = SolidSpec.TryParse(JToken.Parse("""
        [{"name":"post","tube":{"a":[0,0,0],"b":[0,0,1000]}},
         {"op":"cut","target":"post","tube":{"a":[-50,0,1000],"b":[50,0,1000],"wall":0}}]
        """), out var spec, out var errors, JToken.Parse("{\"tube\":{\"od\":48.3,\"wall\":5.08}}"));
        Assert.True(ok, errors.ToString());
        var post = Assert.IsType<PartRecipe.Circle>(spec.Items[0].Profile[0]);
        Assert.Equal(24.15, post.R, 9);
        Assert.Single(post.Inner);
        var saddle = Assert.IsType<PartRecipe.Circle>(spec.Items[1].Profile[0]);
        Assert.Equal(24.15, saddle.R, 9);
        Assert.Empty(saddle.Inner);
    }

    [Fact]
    public void Join_and_cut_names_are_optional_but_new_bodies_need_one()
    {
        var (ok, spec, _) = Parse("""
        [{"name":"plate","box":{"min":[0,0,0],"max":[10,10,10]}},
         {"op":"cut","target":"plate","box":{"min":[2,2,-1],"max":[4,4,11]}}]
        """);
        Assert.True(ok);
        Assert.Equal("cut_1", spec.Items[1].Name);

        var (bad, _, errors) = Parse("[{\"box\":{\"min\":[0,0,0],\"max\":[1,1,1]}}]");
        Assert.False(bad);
        Assert.Equal("items[0].name", (string?)errors[0]["path"]);
    }

    [Fact]
    public void Defaults_must_be_keyed_by_shape_kind()
    {
        Assert.False(SolidSpec.TryParse(JToken.Parse("[{\"name\":\"a\",\"box\":{\"min\":[0,0,0],\"max\":[1,1,1]}}]"),
            out _, out var errors, JToken.Parse("{\"pipe\":{\"od\":5}}")));
        Assert.Equal("defaults.pipe", (string?)errors[0]["path"]);
    }

    [Fact]
    public void Item_count_is_capped()
    {
        var many = new JArray(Enumerable.Range(0, SolidSpec.MaxItems + 1)
            .Select(i => new JObject { ["name"] = "b" + i, ["box"] = JObject.Parse("{\"min\":[0,0,0],\"max\":[1,1,1]}") }));
        Assert.False(SolidSpec.TryParse(many, out _, out var errors));
        Assert.Contains("at most", (string?)errors[0]["message"]);
    }
}
