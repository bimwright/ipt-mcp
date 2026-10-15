using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.IPart;

/// <summary>A saved, immutable catalog snapshot. No Excel automation or formula evaluation.</summary>
internal sealed class IPartExcelCatalog
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal string Path { get; private set; } = "";
    internal string Sha256 { get; private set; } = "";
    internal JArray Rows { get; private set; } = new();
    internal JObject Binding { get; private set; } = new();
    internal void VerifyUnchanged() { if (IPartInput.FileHash(Path) != Sha256) throw new InvalidOperationException("Excel changed during the operation; save and retry"); }
    internal static IPartExcelCatalog Read(string path, string sheetName, string headerCell, JObject? mapping, JArray columns)
    {
        path = IPartInput.LocalPath(path, ".xlsx");
        byte[] bytes;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (file.Length < 1 || file.Length > 16 * 1024 * 1024) throw new ArgumentException("Workbook must be nonempty and at most 16 MiB");
            using var buffer = new MemoryStream(); file.CopyTo(buffer); bytes = buffer.ToArray();
        }
        var cell = Regex.Match(headerCell ?? "", @"\A\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})\z");
        if (!cell.Success) throw new ArgumentException("header_cell must be an A1-style cell");
        int col = 0; foreach (var c in cell.Groups[1].Value.ToUpperInvariant()) col = col * 26 + c - 'A' + 1;
        var first = int.Parse(cell.Groups[2].Value, CultureInfo.InvariantCulture);
        if (col > 16334 || first > 1047575) throw new ArgumentException("Catalog exceeds worksheet bounds");
        using var stream = new MemoryStream(bytes, false); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000 || zip.Entries.Any(e => e.FullName.IndexOf("vbaProject", StringComparison.OrdinalIgnoreCase) >= 0 || e.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Macros and external workbook links are not supported");
        XDocument Xml(string name)
        {
            var entry = zip.GetEntry(name) ?? throw new ArgumentException("Workbook entry missing: " + name);
            if (entry.Length > 32 * 1024 * 1024) throw new ArgumentException("Workbook XML exceeds 32 MiB");
            using var reader = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
            return XDocument.Load(reader);
        }
        var book = Xml("xl/workbook.xml");
        var sheet = book.Descendants(Main + "sheet").SingleOrDefault(s => (string?)s.Attribute("name") == sheetName) ?? throw new ArgumentException("Worksheet not found: " + sheetName);
        var id = (string?)sheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
        var relation = Xml("xl/_rels/workbook.xml.rels").Root!.Elements().Single(r => (string?)r.Attribute("Id") == id);
        var target = ((string?)relation.Attribute("Target") ?? "").Replace('\\', '/');
        if ((string?)relation.Attribute("TargetMode") == "External" || target.Length == 0 || target.Contains("..") || target.Contains(":")) throw new ArgumentException("Invalid worksheet relationship");
        target = target.StartsWith("/") ? target.Substring(1) : "xl/" + target;
        string Text(XElement e) => string.Concat(e.Descendants(Main + "t").Select(t => t.Value));
        var strings = zip.GetEntry("xl/sharedStrings.xml") is null ? Array.Empty<string>() : Xml("xl/sharedStrings.xml").Descendants(Main + "si").Select(Text).ToArray();
        var cells = Xml(target).Descendants(Main + "c").ToDictionary(c => (string)c.Attribute("r")!, StringComparer.OrdinalIgnoreCase);
        string Column(int n) { var s = ""; while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; } return s; }
        JToken Value(int c, int r)
        {
            var address = Column(c) + r;
            if (!cells.TryGetValue(address, out var element)) return JValue.CreateNull();
            var raw = (string?)element.Element(Main + "v"); var type = (string?)element.Attribute("t");
            if (element.Element(Main + "f") != null && raw is null) throw new ArgumentException(address + " has no saved formula result");
            if (type == "e") throw new ArgumentException("Excel error at " + address);
            if (type == "inlineStr") return new JValue(Text(element.Element(Main + "is")!));
            if (type == "s")
            { if (!int.TryParse(raw, out var index) || index < 0 || index >= strings.Length) throw new ArgumentException("Invalid shared string"); return new JValue(strings[index]); }
            if (type == "b") return new JValue(raw == "1");
            if (raw is null) return JValue.CreateNull();
            if (type == "str") return new JValue(raw);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number) || double.IsNaN(number)) throw new ArgumentException("Invalid numeric value at " + address);
            return new JValue(number);
        }
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i <= 50; i++)
        {
            var name = Value(col + i, first).ToString().Trim(); if (name.Length == 0) break;
            if (i == 50 || headers.ContainsKey(name)) throw new ArgumentException("Too many or duplicate catalog headers"); headers.Add(name, col + i);
        }
        var keys = new[] { "member", "part_number" }.Concat(columns.Select(c => (string)c["name"]!)).ToArray();
        mapping ??= new JObject(keys.Select(k => new JProperty(k, k)));
        if (!new HashSet<string>(keys, StringComparer.Ordinal).SetEquals(mapping.Properties().Select(p => p.Name)) || mapping.Properties().Any(p => p.Value.Type != JTokenType.String || !headers.ContainsKey((string)p.Value!)))
            throw new ArgumentException("mapping must map member, part_number and every configured column to existing headers");
        if (mapping.Properties().Select(p => (string)p.Value!).Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new ArgumentException("Each mapped column must use a distinct header");
        var memberColumn = headers[(string)mapping["member"]!];
        var memberColumnName = Column(memberColumn);
        foreach (var address in cells.Keys)
        {
            if (address.StartsWith(memberColumnName, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(address.Substring(memberColumnName.Length), out var rowNumber)
                && rowNumber > first + IPartInput.MaxRows
                && !string.IsNullOrWhiteSpace(Value(memberColumn, rowNumber).ToString()))
                throw new ArgumentException("Catalog data extends beyond 1000 rows; trailing members cannot be ignored");
        }
        var rows = new JArray(); bool gap = false;
        for (int i = 1; i <= IPartInput.MaxRows + 1; i++)
        {
            var member = Value(headers[(string)mapping["member"]!], first + i);
            if (member.Type == JTokenType.Null || string.IsNullOrWhiteSpace(member.ToString())) { gap = true; continue; }
            if (gap) throw new ArgumentException("Catalog member rows must be contiguous; a blank row precedes data");
            if (i > IPartInput.MaxRows) throw new ArgumentException("Catalog exceeds 1000 rows");
            if (member.Type != JTokenType.String) throw new ArgumentException("Member identifiers must be text");
            var values = new JObject(columns.Select(c => new JProperty((string)c["name"]!, Value(headers[(string)mapping[(string)c["name"]!]!], first + i))));
            var pn = Value(headers[(string)mapping["part_number"]!], first + i);
            rows.Add(new JObject { ["member"] = member, ["part_number"] = pn, ["values"] = values });
        }
        var hash = IPartInput.Hash(bytes);
        return new IPartExcelCatalog { Path = path, Sha256 = hash, Rows = IPartInput.Rows(rows, columns), Binding = new JObject {
            ["path"] = path, ["sheet"] = sheetName, ["header_cell"] = Column(col) + first, ["mapping"] = mapping.DeepClone(), ["source_sha256"] = hash } };
    }
}
