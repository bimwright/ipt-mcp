using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

// A type parked under an "Inventor.*" namespace to exercise the API-object guard in
// ScriptResultToken without needing the real Inventor interop.
namespace Inventor.F2Harness
{
    public class FakeApiObject { }
}

namespace Bimwright.Ipt.Tests
{
    /// <summary>
    /// F2 contract pieces that are unit-testable without Inventor:
    /// <c>ScriptResultToken</c> (F2-a return-value marshalling), <c>StaQueueStats</c>
    /// (F2-b health counters) and <c>PluginClient.ResolveEffectiveTimeout</c> (F2-b timeout clamp).
    /// </summary>
    public sealed class SendCodeContractTests
    {
        // ---- F2-a: script return value -------------------------------------------------

        [Fact]
        public void ToResultToken_null_value_yields_null_without_error()
        {
            var token = ScriptResultToken.ToResultToken(null, out var error);
            Assert.Null(token);
            Assert.Null(error);
        }

        [Fact]
        public void ToResultToken_anonymous_dto_serializes()
        {
            var token = ScriptResultToken.ToResultToken(new { a = 1, b = "x" }, out var error);
            Assert.Null(error);
            var obj = Assert.IsType<JObject>(token);
            Assert.Equal(1, obj["a"]!.Value<int>());
            Assert.Equal("x", obj["b"]!.Value<string>());
        }

        [Fact]
        public void ToResultToken_primitives_and_arrays_serialize()
        {
            Assert.Equal(42, ScriptResultToken.ToResultToken(42, out _)!.Value<int>());
            var arr = Assert.IsType<JArray>(ScriptResultToken.ToResultToken(new[] { 1, 2, 3 }, out _));
            Assert.Equal(3, arr.Count);
        }

        [Fact]
        public void ToResultToken_passthroughs_jtoken()
        {
            var input = new JObject { ["k"] = 1 };
            var token = ScriptResultToken.ToResultToken(input, out var error);
            Assert.Null(error);
            Assert.Same(input, token);
        }

        [Fact]
        public void ToResultToken_rejects_inventor_api_objects_with_dto_guidance()
        {
            var token = ScriptResultToken.ToResultToken(new Inventor.F2Harness.FakeApiObject(), out var error);
            Assert.Null(token);
            Assert.Equal(ScriptResultToken.ApiObjectError, error);
        }

        private sealed class SelfLoop
        {
            public SelfLoop? Self;
        }

        [Fact]
        public void ToResultToken_unserializable_value_returns_error_not_exception()
        {
            var loop = new SelfLoop();
            loop.Self = loop; // JToken.FromObject throws on the reference loop
            var token = ScriptResultToken.ToResultToken(loop, out var error);
            Assert.Null(token);
            Assert.NotNull(error);
            Assert.Contains("not JSON-serializable", error);
            Assert.Contains("return a DTO", error);
        }

        // ---- F2-b: STA queue counters ---------------------------------------------------

        [Fact]
        public void StaQueueStats_track_queued_executing_completed()
        {
            var s = new StaQueueStats();
            Assert.Equal(0, s.PendingCommands);
            Assert.Equal(0, s.ExecutingCommands);

            s.OnQueued();
            Assert.Equal(1, s.PendingCommands);
            Assert.Equal(0, s.ExecutingCommands);

            s.OnStarted();
            Assert.Equal(1, s.PendingCommands);   // still outstanding while executing
            Assert.Equal(1, s.ExecutingCommands);

            s.OnCompleted();
            Assert.Equal(0, s.PendingCommands);
            Assert.Equal(0, s.ExecutingCommands);
        }

        [Fact]
        public void StaQueueStats_abandoned_item_rolls_back()
        {
            var s = new StaQueueStats();
            s.OnQueued();
            s.OnAbandoned();
            Assert.Equal(0, s.PendingCommands);
        }

        // ---- F2-b: per-call timeout clamp ------------------------------------------------

        [Theory]
        [InlineData(30000, null, 30000)]        // default = server config
        [InlineData(30000, 5000, 5000)]         // caller override
        [InlineData(30000, 999999, 600000)]     // spec cap: 600 000 ms
        [InlineData(30000, 600000, 600000)]
        [InlineData(30000, 0, 1)]               // clamped to a valid lower bound
        [InlineData(30000, -50, 1)]
        public void ResolveEffectiveTimeout_clamps_per_call_budget(int configured, int? requested, int expected)
            => Assert.Equal(expected, PluginClient.ResolveEffectiveTimeout(configured, requested));
    }
}
