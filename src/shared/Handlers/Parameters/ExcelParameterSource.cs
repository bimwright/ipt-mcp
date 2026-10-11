using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

/// <summary>Reads saved numeric parameter rows without starting Excel or evaluating workbook code.</summary>
internal sealed class ExcelParameterSource
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    internal const int MaxParameters = 1000;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal sealed class Row
    {
        internal string Name { get; }
        internal double Value { get; }
        internal string Units { get; }
        internal Row(string name, double value, string units) { Name = name; Value = value; Units = units; }
    }
    internal string Path { get; }
    internal string StartCell { get; }
    internal string Sheet { get; }
    internal string Sha256 { get; }
    internal IReadOnlyList<Row> Rows { get; }
    internal string TableId => Identity(Path, StartCell);

    private ExcelParameterSource(string path, string startCell, string sheet, string hash, List<Row> rows)
    { Path = path; StartCell = startCell; Sheet = sheet; Sha256 = hash; Rows = rows; }

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"\A[A-Za-z]:[\\/]"))
            throw new ArgumentException("path must be an absolute local-drive .xlsx path; URLs and network paths are not supported");
        var full = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetExtension(full), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("only macro-free .xlsx workbooks are supported");
        return full;
    }

    internal static string NormalizeCell(string cell)
    {
        var match = Regex.Match(cell ?? "", @"\A\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})\z");
        if (!match.Success) throw new ArgumentException("start_cell must be a single A1-style cell on the first worksheet");
        var col = ColumnNumber(match.Groups[1].Value);
        var row = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (col > 16381 || row > 1048576) throw new ArgumentException("start_cell is outside the worksheet parameter-block bounds");
        return match.Groups[1].Value.ToUpperInvariant() + row.ToString(CultureInfo.InvariantCulture);
    }

    internal static string Identity(string path, string startCell)
        => "excel:" + Hash(Encoding.UTF8.GetBytes(NormalizePath(path).ToUpperInvariant() + "|" + NormalizeCell(startCell)));

    internal static ExcelParameterSource Read(string path, string startCell)
    {
        path = NormalizePath(path); startCell = NormalizeCell(startCell);
        var bytes = ReadBytes(path);
        using var stream = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000 || zip.Entries.Any(e => e.FullName.IndexOf("vbaProject", StringComparison.OrdinalIgnoreCase) >= 0
            || e.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("workbook macros and external workbook links are not supported");
        var workbook = Xml(zip, "xl/workbook.xml");
        var sheet = workbook.Root?.Element(Main + "sheets")?.Elements(Main + "sheet").FirstOrDefault()
            ?? throw new ArgumentException("workbook has no worksheet");
        var rels = Xml(zip, "xl/_rels/workbook.xml.rels");
        var relation = rels.Root?.Elements().SingleOrDefault(e => (string?)e.Attribute("Id") == (string?)sheet.Attribute(Rel + "id"))
            ?? throw new ArgumentException("first worksheet relationship is missing");
        if ((string?)relation.Attribute("TargetMode") == "External") throw new ArgumentException("external worksheet is not supported");
        var target = ((string?)relation.Attribute("Target") ?? "").Replace('\\', '/');
        if (target.Contains("..") || target.Contains(":") || target.Length == 0) throw new ArgumentException("invalid worksheet path");
        target = target.StartsWith("/") ? target.Substring(1) : "xl/" + target;
        var shared = zip.GetEntry("xl/sharedStrings.xml") is null ? Array.Empty<string>()
            : Xml(zip, "xl/sharedStrings.xml").Descendants(Main + "si").Select(Text).ToArray();
        var cells = Xml(zip, target).Descendants(Main + "c").ToDictionary(
            c => (string?)c.Attribute("r") ?? "", StringComparer.OrdinalIgnoreCase);
        var match = Regex.Match(startCell, @"([A-Z]+)([0-9]+)");
        var column = ColumnNumber(match.Groups[1].Value);
        var firstRow = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var rows = new List<Row>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var offset = 0; offset <= MaxParameters; offset++)
        {
            var number = firstRow + offset;
            var name = Value(cells, ColumnName(column) + number, shared).Trim();
            if (name.Length == 0) break;
            if (offset == MaxParameters) throw new ArgumentException("parameter block exceeds 1000 rows");
            if (!Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]*\z") || !names.Add(name))
                throw new ArgumentException("invalid or duplicate parameter name: " + name);
            var address = ColumnName(column + 1) + number;
            var raw = Value(cells, address, shared);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException(address + " must contain a finite saved numeric value; recalculate and save formula cells in Excel");
            var units = Value(cells, ColumnName(column + 2) + number, shared).Trim();
            if (units.Length == 0 || units.Length > 64) throw new ArgumentException("explicit units are required for " + name + " (use ul for unitless)");
            rows.Add(new Row(name, value, units));
        }
        if (rows.Count == 0) throw new ArgumentException("no parameter rows at start_cell; select the first parameter, not the column header");
        return new ExcelParameterSource(path, startCell, (string?)sheet.Attribute("name") ?? "", Hash(bytes), rows);
    }

    internal void VerifyUnchanged()
    {
        if (Hash(ReadBytes(Path)) != Sha256) throw new InvalidOperationException("workbook changed during the operation; save it and retry");
    }

    private static byte[] ReadBytes(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length == 0 || file.Length > MaxBytes) throw new ArgumentException("workbook must be nonempty and at most 16 MiB");
        using var buffer = new MemoryStream(); file.CopyTo(buffer);
        if (buffer.Length > MaxBytes) throw new ArgumentException("workbook exceeded 16 MiB while reading");
        return buffer.ToArray();
    }

    private static XDocument Xml(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new ArgumentException("workbook entry missing: " + name);
        if (entry.Length > 32 * 1024 * 1024) throw new ArgumentException("workbook XML entry exceeds 32 MiB");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        return XDocument.Load(reader);
    }

    private static string Value(Dictionary<string, XElement> cells, string address, string[] shared)
    {
        if (!cells.TryGetValue(address, out var cell)) return "";
        var type = (string?)cell.Attribute("t");
        var raw = (string?)cell.Element(Main + "v");
        if (cell.Element(Main + "f") is not null && raw is null)
            throw new ArgumentException(address + " has no saved formula result; recalculate and save in Excel");
        if (type == "e" || type == "b") throw new ArgumentException(address + " contains an error or boolean instead of parameter data");
        if (type == "inlineStr") return Text(cell.Element(Main + "is")!);
        if (type == "s")
        {
            if (!int.TryParse(raw, out var index) || index < 0 || index >= shared.Length) throw new ArgumentException("invalid shared string at " + address);
            return shared[index];
        }
        return raw ?? "";
    }
    private static string Text(XElement element) => string.Concat(element.Descendants(Main + "t").Select(e => e.Value));
    private static int ColumnNumber(string name) { var n = 0; foreach (var c in name.ToUpperInvariant()) n = n * 26 + c - 'A' + 1; return n; }
    private static string ColumnName(int n) { var s = ""; while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; } return s; }
    private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
}
