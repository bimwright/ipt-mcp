using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.IPart;

internal static class IPartInput
{
    internal const int MaxRows = 1000;
    internal const int MaxColumns = 48;
    internal static string Required(JObject input, string key)
    {
        if (input[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)input[key]))
            throw new ArgumentException(key + " must be a nonempty string");
        return ((string)input[key]!).Trim();
    }
    internal static string Member(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value != value.Trim()
            || Regex.IsMatch(value, "[<>:\"/\\\\|?*\\x00-\\x1f]") || value.EndsWith(".")
            || value.StartsWith(".") || value.StartsWith("=") || value.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(value.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase))
            throw new ArgumentException("member must be a safe, nonempty file stem, without .ipt: " + value);
        return value;
    }
    internal static string LocalPath(string value, string extension)
    {
        if (!Regex.IsMatch(value ?? "", @"\A[A-Za-z]:[\\/]")) throw new ArgumentException("An absolute local-drive path is required");
        var path = Path.GetFullPath(value!);
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected " + extension + " file");
        if (path.IndexOf(':', 2) >= 0) throw new ArgumentException("Alternate file streams are not supported");
        return path;
    }
    internal static JArray Columns(JToken? token)
    {
        if (token is not JArray columns || columns.Count == 0 || columns.Count > MaxColumns)
            throw new ArgumentException("columns must contain 1..48 {name,kind,target?} definitions");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "member", "part_number" };
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new JArray();
        foreach (var item in columns)
        {
            if (item is not JObject c) throw new ArgumentException("Each column must be an object");
            var name = Required(c, "name"); var kind = Required(c, "kind").ToLowerInvariant();
            if (!Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z") || !names.Add(name)) throw new ArgumentException("Invalid or duplicate column name: " + name);
            if (!new[] { "parameter", "material", "description", "suppression" }.Contains(kind)) throw new ArgumentException("Unsupported column kind: " + kind);
            var target = kind is "parameter" or "suppression" ? Required(c, "target") : kind;
            if (target.Length > 128 || target.IndexOfAny(new[] { '<', '>', '[', ']', '\r', '\n', '=' }) >= 0 || !targets.Add(kind + ":" + target))
                throw new ArgumentException("Invalid or duplicate column target: " + target);
            result.Add(new JObject { ["name"] = name, ["kind"] = kind, ["target"] = target });
        }
        return result;
    }
    internal static JArray Rows(JToken? token, JArray columns)
    {
        if (token is not JArray rows || rows.Count == 0 || rows.Count > MaxRows) throw new ArgumentException("rows must contain 1..1000 complete member rows");
        var known = new HashSet<string>(columns.Select(c => (string)c["name"]!), StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new JArray();
        foreach (var item in rows)
        {
            if (item is not JObject row) throw new ArgumentException("Each row must be an object");
            var member = Member(Required(row, "member"));
            if (!members.Add(member)) throw new ArgumentException("Duplicate member: " + member);
            var partNumber = row["part_number"] is null ? member : Required(row, "part_number");
            if (partNumber.Length > 255 || partNumber.StartsWith("=")) throw new ArgumentException("Invalid part_number");
            if (row["values"] is not JObject values || !known.SetEquals(values.Properties().Select(p => p.Name)))
                throw new ArgumentException("Each row.values must supply exactly the configured columns: " + string.Join(", ", known));
            var normalized = new JObject();
            foreach (var column in columns)
            {
                var name = (string)column["name"]!; var kind = (string)column["kind"]!; var value = values[name]!;
                if (value.Type == JTokenType.String)
                {
                    var s = ((string)value)!.Trim();
                    if (s.Length > 1024 || s.StartsWith("=") || s.Length == 0) throw new ArgumentException("Invalid or empty configured cell: " + name);
                    if (kind == "suppression")
                    { s = s.ToLowerInvariant(); if (s != "compute" && s != "suppress") throw new ArgumentException(name + " must be compute/suppress or a boolean (true=suppress)"); }
                    normalized[name] = s;
                }
                else if (kind == "suppression" && value.Type == JTokenType.Boolean) normalized[name] = (bool)value ? "suppress" : "compute";
                else if (kind == "parameter" && (value.Type == JTokenType.Float || value.Type == JTokenType.Integer))
                {
                    var number = (double)value;
                    if (double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException(name + " must be finite");
                    normalized[name] = number.ToString("R", CultureInfo.InvariantCulture);
                }
                else throw new ArgumentException("Invalid value type for " + name);
            }
            result.Add(new JObject { ["member"] = member, ["part_number"] = partNumber, ["values"] = normalized });
        }
        return result;
    }
    internal static string[] Members(JToken? token, int maximum = 50)
    {
        if (token is not JArray a || a.Count == 0 || a.Count > maximum || a.Any(t => t.Type != JTokenType.String))
            throw new ArgumentException("members must contain 1.." + maximum + " member names");
        var names = a.Select(t => Member((string)t!)).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("Duplicate member selection");
        return names;
    }
    internal static string Hash(byte[] bytes)
    { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    internal static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    internal static string FileHash(string path) { using var file = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
}
