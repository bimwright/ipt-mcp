using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>Host-free validation of document-scoped, opaque sketch point references.</summary>
internal sealed class SketchPointToken
{
    private const string Prefix = "skpt1:";
    internal string DocumentId { get; }
    internal string DocumentPath { get; }
    internal string SketchKey { get; }
    internal string PointKey { get; }
    internal SketchPointToken(string documentId, string documentPath, string sketchKey, string pointKey)
    { DocumentId = documentId; DocumentPath = documentPath; SketchKey = sketchKey; PointKey = pointKey; }

    internal string Encode() => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(new JObject
    { ["d"] = DocumentId, ["f"] = DocumentPath, ["s"] = SketchKey, ["p"] = PointKey }.ToString(Formatting.None)));

    internal static SketchPointToken Decode(string? value)
    {
        try
        {
            if (value is null || value.Length > 4096 || !value.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException();
            using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(Prefix.Length))))) { MaxDepth = 4 };
            var obj = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read() || obj.Count != 4 || new[] { "d", "f", "s", "p" }.Any(k => obj[k]?.Type != JTokenType.String)) throw new FormatException();
            var result = new SketchPointToken((string)obj["d"]!, (string)obj["f"]!, (string)obj["s"]!, (string)obj["p"]!);
            if (string.IsNullOrWhiteSpace(result.DocumentId) || Convert.FromBase64String(result.SketchKey).Length == 0
                || (result.PointKey != "origin" && Convert.FromBase64String(result.PointKey).Length == 0)) throw new FormatException();
            return result;
        }
        catch (Exception ex) when (ex is FormatException || ex is JsonException || ex is ArgumentException)
        { throw new ArgumentException("Invalid point reference; refresh inventor_list_sketch_points."); }
    }
}

internal sealed class SketchPointDimensionInput
{
    internal SketchPointToken First { get; private set; } = null!;
    internal SketchPointToken Second { get; private set; } = null!;
    internal string Orientation { get; private set; } = "";
    internal double? ValueMm { get; private set; }
    internal string? Expression { get; private set; }
    internal double[]? TextPosition { get; private set; }

    internal static SketchPointDimensionInput Parse(JObject input)
    {
        var result = new SketchPointDimensionInput
        {
            First = SketchPointToken.Decode(input["point1_ref"]?.Type == JTokenType.String ? (string?)input["point1_ref"] : null),
            Second = SketchPointToken.Decode(input["point2_ref"]?.Type == JTokenType.String ? (string?)input["point2_ref"] : null),
            Orientation = input["orientation"]?.Type == JTokenType.String ? ((string)input["orientation"]!).Trim().ToLowerInvariant() : "",
        };
        if (result.Orientation != "horizontal" && result.Orientation != "vertical" && result.Orientation != "aligned")
            throw new ArgumentException("orientation must be horizontal, vertical or aligned");
        if (result.First.DocumentId != result.Second.DocumentId || !string.Equals(result.First.DocumentPath, result.Second.DocumentPath, StringComparison.OrdinalIgnoreCase)
            || result.First.SketchKey != result.Second.SketchKey) throw new ArgumentException("Both points must belong to the same document and sketch");
        if (!SketchDimensionParams.TryParseValue(input["value"], out var value, out var expression, out var error))
            throw new ArgumentException("value: " + error);
        result.ValueMm = value; result.Expression = expression;
        if (input["text_position"] is JToken text && text.Type != JTokenType.Null)
        {
            if (text is not JArray array || array.Count != 2 || array.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                throw new ArgumentException("text_position must contain two finite numbers in sketch millimetres");
            result.TextPosition = array.Select(v => (double)v).ToArray();
            if (result.TextPosition.Any(v => double.IsNaN(v) || double.IsInfinity(v))) throw new ArgumentException("text_position must be finite");
        }
        return result;
    }
}
