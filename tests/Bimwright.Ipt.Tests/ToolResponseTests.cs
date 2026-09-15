using Bimwright.Ipt.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests
{
    /// <summary>
    /// Spec F3-d: <see cref="ToolResponse"/> pretty-prints payloads at or under 4 KiB compact
    /// UTF-8 and ships larger ones compact, so big responses stop paying the indentation tax.
    /// </summary>
    public sealed class ToolResponseTests
    {
        [Fact]
        public void Small_payload_is_indented()
        {
            var text = ToolResponse.Serialize(new { a = 1, b = "x" });
            Assert.Contains('\n', text);
            Assert.Equal(1, JObject.Parse(text)["a"]!.Value<int>());
        }

        [Fact]
        public void Large_payload_is_compact()
        {
            var items = Enumerable.Range(0, 200)
                .Select(i => new { id = i, name = "item-" + i })
                .ToArray();
            var text = ToolResponse.Serialize(items);
            Assert.DoesNotContain('\n', text);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) > ToolResponse.PrettyPrintMaxBytes);
        }

        [Fact]
        public void Boundary_4096_indented_4097_compact()
        {
            // {"s":"..."} compact = content length + 8 bytes (ASCII only).
            var at4096 = ToolResponse.Serialize(new { s = new string('x', 4096 - 8) });
            Assert.Contains('\n', at4096);

            var at4097 = ToolResponse.Serialize(new { s = new string('x', 4097 - 8) });
            Assert.DoesNotContain('\n', at4097);
        }

        [Fact]
        public void Error_shape_is_unchanged()
        {
            var obj = JObject.Parse(ToolResponse.Error("TIMEOUT", "x"));
            Assert.False(obj["ok"]!.Value<bool>());
            Assert.Equal("TIMEOUT", obj["error"]!["code"]!.Value<string>());
            Assert.Equal("x", obj["error"]!["message"]!.Value<string>());
        }
    }
}
