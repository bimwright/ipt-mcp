using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.ILogic;
using Bimwright.Ipt.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ILogicPolicyTests
{
    [Fact]
    public void Gateway_errors_preserve_rule_and_cleanup_diagnostics_in_the_tool_response()
    {
        var data = new JObject { ["rule_deleted"] = false, ["exceptions"] = new JArray(new JObject { ["line"] = 3 }) };
        var error = new InventorGatewayException(InventorErrorCodes.API_ERROR, "rule failed", data);
        var response = JObject.Parse(ToolResponse.Error(error));
        Assert.False((bool)response["ok"]!);
        Assert.False((bool)response["data"]!["rule_deleted"]!);
        Assert.Equal(3, (int)response["data"]!["exceptions"]![0]!["line"]!);
    }

    private sealed class Store : ILogicResultStore
    {
        public bool Exists;
        public object? Value;
        public bool IgnoreDelete, IgnoreWrite;
        public bool TryRead(out object? value) { value = Value; return Exists; }
        public void Write(object value) { if (!IgnoreWrite) { Exists = true; Value = value; } }
        public void Delete() { if (!IgnoreDelete) { Exists = false; Value = null; } }
    }

    [Fact]
    public void Rule_errors_are_failures_with_execution_diagnostics()
    {
        var data = new JObject { ["ran"] = true, ["exceptions"] = new JArray(new JObject { ["context"] = "RuleCompiling" }) };
        var result = ILogicOutcome.Complete(data, new InventorResponseMeta(), 0);
        Assert.False(result.Ok);
        Assert.Same(data, result.Data);
        Assert.False((bool)data["ran"]!);
        Assert.Single((JArray)result.Data!["exceptions"]!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Native_failure_status_never_reports_success(int status)
        => Assert.False(ILogicOutcome.Complete(new JObject(), new InventorResponseMeta(), status).Ok);

    [Theory]
    [InlineData("rule_deleted")]
    [InlineData("result_property_restored")]
    public void Incomplete_cleanup_is_a_failure_even_without_a_native_exception(string field)
    {
        var result = ILogicOutcome.Complete(new JObject { [field] = false }, new InventorResponseMeta());
        Assert.False(result.Ok);
        Assert.False((bool)result.Data![field]!);
    }

    [Fact]
    public void Successful_status_with_no_diagnostics_is_success()
        => Assert.True(ILogicOutcome.Complete(new JObject { ["exceptions"] = new JArray() }, new InventorResponseMeta(), 0).Ok);

    [Fact]
    public void Existing_result_is_preserved_and_not_returned_as_a_fresh_result()
    {
        var store = new Store { Exists = true, Value = "user-value" };
        var channel = new ILogicResultChannel(store);
        channel.Begin();
        Assert.Null(channel.Read());
        channel.Restore();
        Assert.True(store.Exists);
        Assert.Equal("user-value", store.Value);
    }

    [Fact]
    public void A_new_result_is_read_and_the_existing_property_is_restored()
    {
        var store = new Store { Exists = true, Value = "user-value" };
        var channel = new ILogicResultChannel(store);
        channel.Begin();
        store.Write("{\"result\":42}");
        Assert.Equal("{\"result\":42}", channel.Read());
        channel.Restore();
        Assert.Equal("user-value", store.Value);
    }

    [Fact]
    public void Existing_non_string_property_is_rejected_without_mutation()
    {
        var store = new Store { Exists = true, Value = 42 };
        var channel = new ILogicResultChannel(store);
        Assert.Throws<ArgumentException>(() => channel.Begin());
        channel.Restore();
        Assert.True(store.Exists);
        Assert.Equal(42, store.Value);
    }

    [Fact]
    public void New_channel_is_removed_even_when_execution_throws()
    {
        var store = new Store();
        var channel = new ILogicResultChannel(store);
        try { channel.Begin(); throw new InvalidOperationException(); }
        catch (InvalidOperationException) { }
        finally { channel.Restore(); }
        Assert.False(store.Exists);
    }

    [Fact]
    public void Cleanup_failures_cannot_be_hidden()
    {
        var store = new Store { IgnoreDelete = true };
        var channel = new ILogicResultChannel(store);
        channel.Begin();
        Assert.Throws<InvalidOperationException>(() => channel.Restore());
        var existing = new Store { Exists = true, Value = "original" };
        var restore = new ILogicResultChannel(existing);
        restore.Begin();
        existing.IgnoreWrite = true;
        Assert.Throws<InvalidOperationException>(() => restore.Restore());
    }

    [Fact]
    public void Large_results_are_rejected_without_returning_broken_truncated_JSON()
    {
        var store = new Store();
        var channel = new ILogicResultChannel(store);
        channel.Begin();
        store.Write(new string('x', ILogicResultChannel.MaxChars + 1));
        Assert.Throws<InvalidOperationException>(() => channel.Read());
        channel.Restore();
        Assert.False(store.Exists);
    }

    public static IEnumerable<object[]> Encodings()
    {
        yield return new object[] { new UTF8Encoding(false) };
        yield return new object[] { new UTF8Encoding(true) };
        yield return new object[] { Encoding.Unicode };
        yield return new object[] { Encoding.BigEndianUnicode };
        yield return new object[] { Encoding.UTF32 };
        yield return new object[] { new UTF32Encoding(true, true) };
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Tail_read_is_bounded_and_decodes_BOM_and_multibyte_text(Encoding encoding)
    {
        var path = Path.GetTempFileName();
        try
        {
            var text = new string('x', 2000000) + " tiếng Việt 🛠 cuối";
            File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
            var tail = ILogicLogReader.Read(path, 19);
            var expected = text.Substring(text.Length - 19);
            if (char.IsLowSurrogate(expected[0])) expected = expected.Substring(1);
            Assert.Equal(expected, tail.Text);
            Assert.True(tail.Truncated);
            Assert.InRange(tail.BytesRead, 0, 4 * 19 + 8);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Log_reader_handles_empty_and_whole_file_and_an_open_writer()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Equal("", ILogicLogReader.Read(path, 100).Text);
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var bytes = Encoding.UTF8.GetBytes("old\nnew 🛠");
            writer.Write(bytes);
            writer.Flush();
            Assert.Equal("old\nnew 🛠", ILogicLogReader.Read(path, 0).Text);
            Assert.Equal("🛠", ILogicLogReader.Read(path, 2).Text);
            Assert.Equal("", ILogicLogReader.Read(path, 1).Text);
        }
        finally { File.Delete(path); }
    }
}
