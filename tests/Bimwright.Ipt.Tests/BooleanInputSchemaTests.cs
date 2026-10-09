using System.Text.Json;
using Bimwright.Ipt.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class BooleanInputSchemaTests
{
    [Fact]
    public void Compatibility_rewrite_changes_schema_positions_only()
    {
        using var input = JsonDocument.Parse("""
        {"type":"object","properties":{"a":true,"b":{"type":"boolean","default":true},
        "c":{"items":true},"d":{"anyOf":[true,{"const":true}]},"e":false},
        "additionalProperties":true,"examples":[{"a":true}],"default":{"a":true}}
        """);
        var result = JObject.Parse(BooleanInputSchema.Normalize(input.RootElement).GetRawText());
        Assert.Equal(JTokenType.Object, result["properties"]!["a"]!.Type);
        Assert.Empty((JObject)result["properties"]!["a"]!);
        Assert.Empty((JObject)result["properties"]!["c"]!["items"]!);
        Assert.True(result["properties"]!["b"]!.Value<bool>("default"));
        Assert.False(result["properties"]!.Value<bool>("e"));
        Assert.True(result.Value<bool>("additionalProperties"));
        Assert.True(result["examples"]![0]!.Value<bool>("a"));
        Assert.True(result["default"]!.Value<bool>("a"));
        Assert.Equal(JsonValueKind.True, input.RootElement.GetProperty("properties").GetProperty("a").ValueKind);
    }
}
