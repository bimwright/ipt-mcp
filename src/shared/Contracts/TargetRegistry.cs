using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Shared.Contracts;

public sealed class TargetRegistry
{
    private readonly string _dir;
    private readonly TimeSpan _maxAge;
    public TargetRegistry(string directory, int maxAgeSeconds = 120)
    { _dir = directory; _maxAge = TimeSpan.FromSeconds(maxAgeSeconds); }

    public IReadOnlyList<TargetDescriptor> List()
    {
        if (!Directory.Exists(_dir)) return Array.Empty<TargetDescriptor>();
        var now = DateTimeOffset.UtcNow;
        var live = new List<TargetDescriptor>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            TargetDescriptor? d;
            try { d = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(file)); }
            catch { continue; }
            if (d is null) continue;
            if (!string.Equals(d.HostApp, "Inventor", StringComparison.OrdinalIgnoreCase)) continue;
            if (d.InventorYear is < 2022 or > 2027) continue;
            if (now - d.LastHeartbeatUtc > _maxAge) { DeleteQuietly(file); continue; }
            if (!IsProcessAlive(d.ProcessId)) { DeleteQuietly(file); continue; }
            live.Add(d);
        }
        return live.OrderByDescending(x => x.LastHeartbeatUtc).ToList();
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try { System.Diagnostics.Process.GetProcessById(pid); return true; }
        catch { return false; }
    }

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
