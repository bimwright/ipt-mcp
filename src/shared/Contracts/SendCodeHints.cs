using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Repair hints for <c>send_code</c> compile/runtime errors. The rule table is the embedded
/// <c>send-code-hints.json</c> (one source for the add-in, the tests and the offline evaluator):
/// each rule pairs a diagnostic id (<c>CS####</c> or <c>runtime</c>) with a regex over the
/// message; the first match wins. API-agnostic — compiled into the server, the tests and every add-in.
/// </summary>
public static class SendCodeHints
{
    public const string ResourceName = "Bimwright.Ipt.SendCodeHints.json";

    public sealed class Rule
    {
        public string Id { get; init; } = "";
        public string Code { get; init; } = "";
        public Regex Pattern { get; init; } = null!;
        public string Hint { get; init; } = "";
    }

    private static readonly Lazy<IReadOnlyList<Rule>> _rules = new(LoadEmbedded);

    public static IReadOnlyList<Rule> Rules => _rules.Value;

    /// <summary>Hint for one diagnostic, or null when no rule matches.</summary>
    public static Rule? Match(string code, string message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        foreach (var r in Rules)
        {
            if (!string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)) continue;
            if (r.Pattern.IsMatch(message)) return r;
        }
        return null;
    }

    internal static IReadOnlyList<Rule> Parse(string json)
    {
        var root = JObject.Parse(json);
        var list = new List<Rule>();
        foreach (var t in (JArray?)root["rules"] ?? new JArray())
        {
            list.Add(new Rule
            {
                Id = (string?)t["id"] ?? "",
                Code = (string?)t["code"] ?? "",
                Pattern = new Regex((string?)t["pattern"] ?? "(?!)", RegexOptions.CultureInvariant),
                Hint = (string?)t["hint"] ?? "",
            });
        }
        return list;
    }

    private static IReadOnlyList<Rule> LoadEmbedded()
    {
        try
        {
            using var s = typeof(SendCodeHints).Assembly.GetManifestResourceStream(ResourceName);
            if (s is null) return Array.Empty<Rule>();
            using var r = new StreamReader(s);
            return Parse(r.ReadToEnd());
        }
        catch
        {
            return Array.Empty<Rule>();   // hints are advisory — never break send_code
        }
    }
}
