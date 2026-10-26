using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Newtonsoft reading for tool arguments and wire requests. Newtonsoft's defaults turn an ISO date-time string into a
/// Date token, so a handler reading it back as a string gets another format and offset; here strings stay as sent.
/// </summary>
public static class WireJson
{
    public static readonly JsonSerializerSettings Settings = new() { DateParseHandling = DateParseHandling.None };

    public static JsonTextReader Reader(string json) => new(new StringReader(json))
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Double,
    };

    public static JToken Load(string json)
    {
        using var reader = Reader(json);
        return JToken.Load(reader);
    }

    public static JObject LoadObject(string json)
        => Load(json) as JObject ?? throw new JsonReaderException("Expected a JSON object.");
}
