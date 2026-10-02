using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>
/// Saved <c>send_code</c> helper modules (S2): C# declarations stored once and compiled in front of
/// a script with <c>inventor_send_code(modules: [...])</c>, so recurring helpers are not re-sent on
/// every call. Lives server-side under <c>%LOCALAPPDATA%\Bimwright\ipt-mcp\modules\</c>: one
/// <c>&lt;name&gt;.csx</c> per module plus <c>index.json</c> (hash, description, requires, signatures).
/// A module may <c>require</c> other modules; <see cref="Resolve"/> expands them transitively,
/// dependencies first.
/// </summary>
public sealed class CodeModuleStore
{
    public const int MaxModuleBytes = 64 * 1024;
    public const int MaxModules = 200;

    private readonly string _dir;
    private readonly object _gate = new();

    public CodeModuleStore() : this(DefaultDirectory) { }

    public CodeModuleStore(string directory) => _dir = directory;

    public static string DefaultDirectory => Path.Combine(
        Bimwright.Setup.RuntimeLayout.ForCurrentUser("ipt-mcp").DataRoot, "modules");

    public string Directory => _dir;

    public sealed class Entry
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("hash")] public string Hash { get; set; } = "";
        [JsonProperty("description")] public string? Description { get; set; }
        [JsonProperty("requires")] public List<string> Requires { get; set; } = new();
        [JsonProperty("bytes")] public int Bytes { get; set; }
        [JsonProperty("saved_utc")] public string SavedUtc { get; set; } = "";
        [JsonProperty("signatures")] public JToken? Signatures { get; set; }
    }

    public static string? ValidateName(string? name)
        => name is not null && SendCodeSource.ModuleNamePattern.IsMatch(name)
            ? null
            : "module name must match [a-z][a-z0-9_]{0,63} (lower-case letters, digits, underscore)";

    public IReadOnlyList<Entry> List()
    {
        lock (_gate) return ReadIndex().Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    public bool TryGet(string name, out Entry entry, out string code)
    {
        lock (_gate)
        {
            code = "";
            if (!ReadIndex().TryGetValue(name, out entry!)) return false;
            var file = Path.Combine(_dir, name + ".csx");
            if (!File.Exists(file)) return false;
            code = File.ReadAllText(file, Encoding.UTF8);
            return true;
        }
    }

    public Entry Save(string name, string code, string? description, IReadOnlyList<string> requires, JToken? signatures)
    {
        var privacyError = ValidateSourceForPersistence(code);
        if (privacyError != null) throw new ArgumentException(privacyError, nameof(code));
        lock (_gate)
        {
            var index = ReadIndex();
            if (!index.ContainsKey(name) && index.Count >= MaxModules)
                throw new InvalidOperationException($"module store is full ({MaxModules} modules); delete unused modules first");
            System.IO.Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, name + ".csx"), code, new UTF8Encoding(false));
            var entry = new Entry
            {
                Name = name,
                Hash = SendCodeSource.Hash(code),
                Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
                Requires = requires.ToList(),
                Bytes = Encoding.UTF8.GetByteCount(code),
                SavedUtc = DateTime.UtcNow.ToString("o"),
                Signatures = signatures,
            };
            index[name] = entry;
            WriteIndex(index);
            return entry;
        }
    }

    public static string? ValidateSourceForPersistence(string code)
        => string.Equals(code, Bimwright.Ipt.Shared.Security.BakeRedactor.RedactSource(code), StringComparison.Ordinal)
            ? null : "Code modules cannot persist credential-like values; remove embedded credentials before saving.";

    public bool Delete(string name, out string? blockedBy)
    {
        lock (_gate)
        {
            blockedBy = null;
            var index = ReadIndex();
            if (!index.ContainsKey(name)) return false;
            var dependents = index.Values.Where(e => e.Requires.Contains(name, StringComparer.Ordinal)).Select(e => e.Name).ToList();
            if (dependents.Count > 0)
            {
                blockedBy = string.Join(", ", dependents);
                return false;
            }
            index.Remove(name);
            WriteIndex(index);
            try { File.Delete(Path.Combine(_dir, name + ".csx")); } catch { }
            return true;
        }
    }

    /// <summary>
    /// Expands <paramref name="names"/> plus their <c>requires</c>, dependencies first, each once.
    /// <paramref name="extra"/> (name → (code, requires)) injects a not-yet-saved module (save-time dry compile).
    /// </summary>
    public bool Resolve(IEnumerable<string> names, out List<(string Name, string Code)> ordered, out string? error,
        (string Name, string Code, IReadOnlyList<string> Requires)? extra = null)
    {
        ordered = new List<(string, string)>();
        error = null;
        var done = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string, string)>();
        string? err = null;

        bool Visit(string n)
        {
            if (done.Contains(n)) return true;
            if (!visiting.Add(n)) { err = "module dependency cycle at " + n; return false; }
            string code;
            IReadOnlyList<string> reqs;
            if (extra is { } x && x.Name == n)
            {
                code = x.Code;
                reqs = x.Requires;
            }
            else if (TryGet(n, out var entry, out var saved))
            {
                code = saved;
                reqs = entry.Requires;
            }
            else
            {
                var known = List().Select(e => e.Name).ToList();
                err = $"unknown code module '{n}'. Saved modules: " + (known.Count == 0 ? "(none)" : string.Join(", ", known));
                return false;
            }
            foreach (var r in reqs)
                if (!Visit(r)) return false;
            visiting.Remove(n);
            done.Add(n);
            result.Add((n, code));
            return true;
        }

        foreach (var n in names)
        {
            var nameError = ValidateName(n);
            if (nameError != null) { error = nameError + $" (got '{n}')"; return false; }
            if (!Visit(n)) { error = err; return false; }
        }
        ordered = result;
        return true;
    }

    private Dictionary<string, Entry> ReadIndex()
    {
        var file = Path.Combine(_dir, "index.json");
        if (!File.Exists(file)) return new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            var list = JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(file, Encoding.UTF8)) ?? new List<Entry>();
            return list.Where(e => ValidateName(e.Name) == null)
                       .GroupBy(e => e.Name, StringComparer.Ordinal)
                       .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, Entry>(StringComparer.Ordinal);
        }
    }

    private void WriteIndex(Dictionary<string, Entry> index)
    {
        System.IO.Directory.CreateDirectory(_dir);
        var tmp = Path.Combine(_dir, "index.json.tmp");
        File.WriteAllText(tmp, JsonConvert.SerializeObject(index.Values.OrderBy(e => e.Name, StringComparer.Ordinal), Formatting.Indented),
            new UTF8Encoding(false));
        File.Copy(tmp, Path.Combine(_dir, "index.json"), overwrite: true);
        File.Delete(tmp);
    }
}
