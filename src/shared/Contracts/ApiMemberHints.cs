using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Compile-error hints computed from the loaded API types (the Inventor interop and the Ipt helpers), so a send_code
/// compile error names members that do exist instead of only saying what does not. Host-free: the add-in passes the
/// type universe, tests pass their own. (Signatures for argument errors come from the script's semantic model:
/// ScriptSignatureHints.)
/// </summary>
public static class ApiMemberHints
{
    private const int MaxNear = 6;
    private const int MaxOwners = 4;
    private static readonly Regex MissingMember = new(@"^'(?<type>[\w.]+)' does not contain a definition for '(?<member>\w+)'", RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<Type, string[]> MemberCache = new();
    private static readonly ConcurrentDictionary<Type, (string Name, Type Type)[]> PropertyCache = new();

    /// <summary>CS1061/CS0117: nearest members of the type, and related types that do have the member.</summary>
    public static string? ForMissingMember(string code, string message, IReadOnlyList<Type> universe)
    {
        if (code is not ("CS1061" or "CS0117")) return null;
        var m = MissingMember.Match(message);
        if (!m.Success) return null;
        var typeName = Strip(m.Groups["type"].Value);
        var member = m.Groups["member"].Value;
        var type = universe.FirstOrDefault(t => DisplayName(t) == typeName) ?? universe.FirstOrDefault(t => t.Name == typeName);
        if (type is null) return null;

        var parts = new List<string>();
        var owners = universe.Where(t => t != type && !t.Name.StartsWith("IRx", StringComparison.Ordinal) && Members(t).Contains(member, StringComparer.Ordinal)).ToArray();
        var related = owners.Where(t => Related(t.Name, type.Name)).OrderBy(t => t.Name.Length).ToArray();
        var subtypes = related.Where(t => IsSubtype(t, type)).Take(MaxOwners).ToArray();
        var neighbours = related.Take(MaxOwners).ToArray();
        if (type.GetCustomAttributes(typeof(DefaultMemberAttribute), true).OfType<DefaultMemberAttribute>().Any(d => d.MemberName == member))
            parts.Add($"'{member}' is the indexer of {DisplayName(type)}: use x[index].");
        else if (subtypes.Length > 0)
            parts.Add($"'{member}' exists on {string.Join(", ", subtypes.Select(DisplayName))}: check the object with `x is {DisplayName(subtypes[0])}` and cast, e.g. (({DisplayName(subtypes[0])})x).{member}.");
        else if (neighbours.Length > 0)
        {
            var via = neighbours.Select(o => PathTo(type, o)).FirstOrDefault(p => p != null);
            parts.Add($"'{member}' exists on {string.Join(", ", neighbours.Select(DisplayName))}, {(neighbours.Length == 1 ? "a different object" : "different objects")} (not a cast)"
                + (via is null ? "." : $": use x.{via}.{member}."));
        }
        else if (owners.Length is > 0 and <= MaxOwners)
            parts.Add($"'{member}' exists on {string.Join(", ", owners.Select(DisplayName))}, not on {DisplayName(type)}.");
        else if (owners.Length == 0)
            parts.Add($"No API type has a member '{member}'.");

        var near = Members(type).Where(n => !n.Equals(member, StringComparison.Ordinal))
            .Select(n => (n, d: Distance(n.ToLowerInvariant(), member.ToLowerInvariant()), s: Shared(n, member)))
            .OrderByDescending(x => x.s).ThenBy(x => x.d).ThenBy(x => x.n, StringComparer.Ordinal)
            .Take(MaxNear).Select(x => x.n).ToArray();
        if (near.Length > 0) parts.Add($"{DisplayName(type)} members closest to '{member}': {string.Join(", ", near)}.");
        if (parts.Count == 0) return null;
        // Ships in the same add-in as Ipt.Api, so the pointer is always valid where this hint is produced.
        parts.Add($"All members: return Ipt.Api(\"{DisplayName(type)}\");");
        return string.Join(" ", parts);
    }

    /// <summary>Most lines <see cref="Describe"/> returns; more are counted in a last line.</summary>
    public const int MaxDescribed = 80;

    private static readonly ConcurrentDictionary<Type, string[]> SignatureCache = new();

    /// <summary>
    /// <c>Ipt.Api(type, filter)</c>: the real members of an API type with their signatures (an enum: its values), or the
    /// closest type/member names when nothing matches. A lookup, so it answers with lines instead of throwing.
    /// </summary>
    public static string[] Describe(string? typeName, string? filter, IReadOnlyList<Type> universe)
    {
        var wanted = Strip((typeName ?? "").Trim());
        if (wanted.Length == 0) return new[] { "Pass a type name, e.g. Ipt.Api(\"DrawingCurve\", \"color\")." };
        var type = universe.FirstOrDefault(t => string.Equals(DisplayName(t), wanted, StringComparison.OrdinalIgnoreCase))
                   ?? universe.FirstOrDefault(t => string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (type is null)
            return new[] { $"No API type '{wanted}'. Closest: {string.Join(", ", Nearest(universe.Select(DisplayName), wanted, 8))}." };

        var all = SignatureCache.GetOrAdd(type, Signatures);
        var text = filter?.Trim() ?? "";
        var hit = text.Length == 0 ? all : all.Where(l => l.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        if (hit.Length == 0)
            return new[] { $"No member of {DisplayName(type)} contains '{text}'. Closest: {string.Join(", ", Nearest(Members(type), text, 8))}." };
        if (hit.Length <= MaxDescribed) return hit;
        return hit.Take(MaxDescribed).Concat(new[] { $"… {hit.Length - MaxDescribed} more of {DisplayName(type)}: pass a filter, e.g. Ipt.Api(\"{DisplayName(type)}\", \"point\")." }).ToArray();
    }

    private static string[] Signatures(Type type)
    {
        var owner = DisplayName(type);
        if (type.IsEnum) return Enum.GetNames(type).Select(n => owner + "." + n).ToArray();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var lines = new List<string>();
        foreach (var t in new[] { type }.Concat(type.IsInterface ? type.GetInterfaces() : Array.Empty<Type>()))
        {
            foreach (var p in t.GetProperties(flags).Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)))
            {
                var index = p.GetIndexParameters();
                lines.Add(owner + "." + p.Name + (index.Length > 0 ? "[" + Args(index) + "]" : "") + " -> " + TypeName(p.PropertyType) + (p.CanWrite ? "" : " (read-only)"));
            }
            foreach (var m in t.GetMethods(flags).Where(m => !m.IsSpecialName && !m.Name.StartsWith("_", StringComparison.Ordinal)))
                lines.Add(owner + "." + m.Name + "(" + Args(m.GetParameters()) + ") -> " + TypeName(m.ReturnType));
        }
        return lines.Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal).ToArray();
    }

    private static string Args(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => TypeName(p.ParameterType) + " " + p.Name + (p.IsOptional ? " = optional" : "")));

    private static string TypeName(Type t) =>
        t.IsByRef ? "ref " + TypeName(t.GetElementType()!) : t.IsArray ? TypeName(t.GetElementType()!) + "[]"
        : t == typeof(double) ? "double" : t == typeof(bool) ? "bool" : t == typeof(int) ? "int" : t == typeof(string) ? "string"
        : t == typeof(object) ? "object" : t == typeof(void) ? "void" : t.Name;

    private static IEnumerable<string> Nearest(IEnumerable<string> names, string wanted, int count) =>
        names.Distinct(StringComparer.Ordinal)
            .Select(n => (n, d: Distance(n.ToLowerInvariant(), wanted.ToLowerInvariant()), s: Shared(n, wanted)))
            .OrderByDescending(x => x.s).ThenBy(x => x.d).ThenBy(x => x.n, StringComparer.Ordinal)
            .Take(count).Select(x => x.n);

    private static string[] Members(Type t) => MemberCache.GetOrAdd(t, type =>
        new[] { type }.Concat(type.IsInterface ? type.GetInterfaces() : Array.Empty<Type>())
            .SelectMany(x => x.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(mi => mi is PropertyInfo || mi is FieldInfo || mi is MethodInfo { IsSpecialName: false })
            .Select(mi => mi.Name).Where(n => !n.StartsWith("_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());

    private static (string Name, Type Type)[] Properties(Type t) => PropertyCache.GetOrAdd(t, type =>
        new[] { type }.Concat(type.IsInterface ? type.GetInterfaces() : Array.Empty<Type>())
            .SelectMany(x => x.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.GetIndexParameters().Length == 0 && !p.Name.StartsWith("_", StringComparison.Ordinal))
            .Select(p => (p.Name, p.PropertyType)).ToArray());

    /// <summary>Subtype-like (LinearGeneralDimension/GeneralDimension) or neighbour (DrawingCurve/DrawingCurveSegment) names.</summary>
    private static bool Related(string owner, string type) =>
        owner.IndexOf(type.TrimStart('_'), StringComparison.Ordinal) >= 0 || type.IndexOf(owner.TrimStart('_'), StringComparison.Ordinal) >= 0;

    /// <summary>
    /// A cast is only safe for a real subtype: the owner's name ends with the type's (LinearGeneralDimension/GeneralDimension)
    /// AND it has every member of the type. The name alone also matches neighbours (HoleTableCell/Cell, MeshFace/Face).
    /// </summary>
    private static bool IsSubtype(Type owner, Type type)
    {
        if (!owner.IsInterface || !type.IsInterface || !owner.Name.EndsWith(type.Name.TrimStart('_'), StringComparison.Ordinal)) return false;
        var mine = Members(type);
        return mine.Length > 0 && mine.All(n => Array.BinarySearch(Members(owner), n, StringComparer.Ordinal) >= 0);
    }

    /// <summary>A property of <paramref name="type"/> that returns <paramref name="owner"/> (DrawingCurveSegment.Parent: DrawingCurve).</summary>
    private static string? PathTo(Type type, Type owner) =>
        Properties(type).Where(p => p.Type == owner)
            .OrderBy(p => p.Name == owner.Name ? 0 : p.Name == "Parent" ? 1 : 2).ThenBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name).FirstOrDefault();

    private static string DisplayName(Type t) => t.DeclaringType is { } outer ? outer.Name + "." + t.Name : t.Name;

    private static string Strip(string name)
    {
        foreach (var prefix in new[] { "Inventor.", "Bimwright.Ipt.Scripting." })
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return name.Substring(prefix.Length);
        return name;
    }

    /// <summary>Length of the longest common substring (case-insensitive), so "IntentOne" ranks near "Intent".</summary>
    private static int Shared(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var best = 0;
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < b.Length; j++)
            {
                var k = 0;
                while (i + k < a.Length && j + k < b.Length && a[i + k] == b[j + k]) k++;
                if (k > best) best = k;
            }
        return best < 3 ? 0 : best;
    }

    private static int Distance(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }
}
