using System;
using Bimwright.Inventor.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Tests;

/// <summary>
/// Round-trip (serialize → deserialize) equality for the wire DTOs, and verification
/// that the meta year field serializes as <c>inventor_year</c> (never navisworks_year).
/// </summary>
public sealed class EnvelopeTests
{
    [Fact]
    public void Envelope_round_trips()
    {
        var env = new InventorCommandEnvelope
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Command = "extrude",
            Params = new JObject { ["sketch_name"] = "Sketch1", ["distance_mm"] = 12.5 },
            TimeoutMs = 15000,
            AuthToken = "tok-abc",
        };

        var json = JsonConvert.SerializeObject(env);
        var back = JsonConvert.DeserializeObject<InventorCommandEnvelope>(json)!;

        Assert.Equal(env.Id, back.Id);
        Assert.Equal(env.Command, back.Command);
        Assert.Equal(env.TimeoutMs, back.TimeoutMs);
        Assert.Equal(env.AuthToken, back.AuthToken);
        Assert.True(JToken.DeepEquals(env.Params, back.Params));
    }

    [Fact]
    public void Envelope_uses_snake_case_wire_names()
    {
        var json = JsonConvert.SerializeObject(new InventorCommandEnvelope { Command = "health", TimeoutMs = 5000 });
        var o = JObject.Parse(json);

        Assert.True(o.ContainsKey("command"));
        Assert.True(o.ContainsKey("timeout_ms"));
        Assert.True(o.ContainsKey("auth_token"));
    }

    [Fact]
    public void Result_success_round_trips()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var meta = new InventorResponseMeta { TargetId = "inventor-2025-99", InventorYear = 2025, DurationMs = 7 };
        var result = InventorCommandResult.Success(id, new JObject { ["feature_name"] = "Extrusion1" }, meta);

        var json = JsonConvert.SerializeObject(result);
        var back = JsonConvert.DeserializeObject<InventorCommandResult>(json)!;

        Assert.Equal(id, back.Id);
        Assert.True(back.Ok);
        Assert.Null(back.Error);
        Assert.Equal("inventor-2025-99", back.Meta.TargetId);
        Assert.Equal(2025, back.Meta.InventorYear);
        Assert.Equal(7, back.Meta.DurationMs);
        Assert.True(JToken.DeepEquals(result.Data, back.Data));
    }

    [Fact]
    public void Result_failure_round_trips_error()
    {
        var id = Guid.NewGuid();
        var result = InventorCommandResult.Fail(id, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
            "extrude requires an active part document", new InventorResponseMeta { InventorYear = 2024 });

        var json = JsonConvert.SerializeObject(result);
        var back = JsonConvert.DeserializeObject<InventorCommandResult>(json)!;

        Assert.False(back.Ok);
        Assert.NotNull(back.Error);
        Assert.Equal("WRONG_DOCUMENT_TYPE", back.Error!.Code);
        Assert.Equal(2024, back.Meta.InventorYear);
    }

    [Fact]
    public void Meta_year_field_is_inventor_year_not_navisworks_year()
    {
        var json = JsonConvert.SerializeObject(new InventorResponseMeta { InventorYear = 2027 });
        var o = JObject.Parse(json);

        Assert.True(o.ContainsKey("inventor_year"));
        Assert.False(o.ContainsKey("navisworks_year"));
        Assert.Equal(2027, (int)o["inventor_year"]!);
    }
}
