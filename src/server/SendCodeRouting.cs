using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bimwright.Ipt.Shared.ToolBaker;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>Advisory first-match routing, independent of script execution and repair hints.</summary>
internal static class SendCodeRouting
{
    private sealed record Rule(string Tool, JObject Args, string Reason, bool QueryOnly,
        Regex[] All, Regex[] Any, Regex[] Exclude);

    private static readonly Regex Mutation = Pattern(
        @"\.\s*\w+\s*(?:\[[^;\r\n]*?\]\s*)?(?:=(?!=|>)|\+=|-=|\*=|/=|\?\?=)|" +
        @"\.\s*(?:Add\w*|Delete\w*|Remove\w*|Clear\w*|Save\w*|Open\w*|Close\w*|Replace\w*|Set\w*|Update\w*|Copy\w*|Move\w*|Transform\w*|Activate|Suppress|Unsuppress|DoBoolean)\s*\(");
    // Scan increments in original text: stripping strings can join '+' + literal + '+'
    // into a false '++'. A textual false positive only suppresses an advisory query hint.
    private static readonly Regex Increment = Pattern(@"\.\s*\w+\s*(?:\[[^;\r\n]*?\]\s*)?(?:\+\+|--)|(?:\+\+|--)\s*\w+\s*\.");
    private static readonly IReadOnlyList<Rule> Rules = Load();

    internal static JObject? Match(string? code, bool usesModules = false, ISet<string>? availableTools = null)
    {
        if (usesModules || string.IsNullOrWhiteSpace(code)) return null;
        try
        {
            // Reuse the policy's lexer: comments/literals are not evidence of API use;
            // expressions inside interpolated strings still are.
            var source = BakeCompilerPolicy.StripLiteralsAndComments(code);
            foreach (var rule in Rules)
            {
                if (availableTools is not null && !availableTools.Contains(rule.Tool)) continue;
                if (rule.QueryOnly && (Mutation.IsMatch(source) || Increment.IsMatch(code))) continue;
                if (!rule.All.All(p => p.IsMatch(source)) ||
                    (rule.Any.Length > 0 && !rule.Any.Any(p => p.IsMatch(source))) ||
                    rule.Exclude.Any(p => p.IsMatch(source))) continue;
                return new JObject
                {
                    ["tool"] = rule.Tool, ["args_sketch"] = rule.Args.DeepClone(), ["reason"] = rule.Reason,
                };
            }
        }
        catch (RegexMatchTimeoutException) { /* advisory only; preserve the original response */ }
        return null;
    }

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(25));

    private static IReadOnlyList<Rule> Load()
    {
        try
        {
            using var stream = typeof(SendCodeRouting).Assembly.GetManifestResourceStream("Bimwright.Ipt.SendCodeRouting.json");
            if (stream is null) return Array.Empty<Rule>();
            using var reader = new StreamReader(stream);
            return ((JArray)JObject.Parse(reader.ReadToEnd())["rules"]!).Select(r => new Rule(
                (string)r["tool"]!, (JObject)r["args_sketch"]!, (string)r["reason"]!,
                (bool?)r["query_only"] == true,
                Patterns(r["all_patterns"]), Patterns(r["any_patterns"]), Patterns(r["exclude_patterns"]))).ToArray();
        }
        catch { return Array.Empty<Rule>(); } // A missing/bad advisory table must never break send_code.
    }

    private static Regex[] Patterns(JToken? patterns) => patterns is JArray array
        ? array.Values<string>().Select(p => Pattern(p!)).ToArray() : Array.Empty<Regex>();
}
