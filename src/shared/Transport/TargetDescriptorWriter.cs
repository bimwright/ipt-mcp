using System;
using System.IO;
using System.Threading;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Shared.Transport;

/// <summary>
/// Writes and refreshes the per-instance target descriptor JSON
/// (<c>dir/inventor-&lt;year&gt;-&lt;pid&gt;.json</c>) that the MCP server scans to discover live
/// add-ins, and runs a background heartbeat timer that re-stamps <c>last_heartbeat_utc</c> so the
/// server's <see cref="TargetRegistry"/> staleness check keeps the entry alive.
///
/// API-FREE on purpose: it never touches the Inventor API. The active-document title/path are
/// supplied by the caller (which holds the <c>Inventor.Application</c>), so this file compiles into
/// the net8 test project and can be unit-tested without Inventor. Ported from nwd's
/// <c>TargetDescriptorWriter</c> with the added heartbeat timer + document refresh.
/// </summary>
public sealed class TargetDescriptorWriter : IDisposable
{
    private readonly string _dir;
    private readonly TargetDescriptor _descriptor;
    private readonly object _gate = new();
    private Timer? _heartbeat;
    private bool _disposed;

    /// <summary>Default heartbeat cadence (30s) — well inside the registry's 120s staleness window.</summary>
    public const int DefaultHeartbeatMs = 30_000;

    /// <param name="dir">Descriptor directory (created if missing).</param>
    /// <param name="descriptor">
    /// The descriptor to persist and keep warm. Its <see cref="TargetDescriptor.InventorYear"/> and
    /// <see cref="TargetDescriptor.ProcessId"/> determine the file name.
    /// </param>
    public TargetDescriptorWriter(string dir, TargetDescriptor descriptor)
    {
        _dir = dir ?? throw new ArgumentNullException(nameof(dir));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    /// <summary>The descriptor file path for the given directory/year/pid.</summary>
    public static string GetPath(string dir, int year, int pid)
        => Path.Combine(dir, $"inventor-{year}-{pid}.json");

    /// <summary>The descriptor file path for this writer's descriptor.</summary>
    public string Path_ => GetPath(_dir, _descriptor.InventorYear, _descriptor.ProcessId);

    /// <summary>
    /// Optionally stamps the active-document title/path (the caller resolves these from the Inventor
    /// API), writes the descriptor once, then starts the heartbeat timer.
    /// </summary>
    public void Start(string? documentTitle = null, string? documentPath = null, int heartbeatMs = DefaultHeartbeatMs)
    {
        SetDocument(documentTitle, documentPath);
        WriteOnce();
        _heartbeat = new Timer(_ => Heartbeat(), null, heartbeatMs, heartbeatMs);
    }

    /// <summary>Updates the cached active-document title/path and re-writes the descriptor immediately.</summary>
    public void UpdateDocument(string? documentTitle, string? documentPath)
    {
        SetDocument(documentTitle, documentPath);
        WriteOnce();
    }

    private void SetDocument(string? documentTitle, string? documentPath)
    {
        lock (_gate)
        {
            _descriptor.DocumentTitle = documentTitle;
            _descriptor.DocumentPath = documentPath;
        }
    }

    private void Heartbeat()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _descriptor.LastHeartbeatUtc = DateTimeOffset.UtcNow;
        }
        WriteOnce();
    }

    /// <summary>Re-stamps <c>last_heartbeat_utc</c> and serializes the descriptor to disk.</summary>
    public void WriteOnce()
    {
        string path;
        string json;
        lock (_gate)
        {
            if (_disposed) return;
            _descriptor.LastHeartbeatUtc = DateTimeOffset.UtcNow;
            path = Path_;
            json = JsonConvert.SerializeObject(_descriptor, Formatting.Indented);
        }
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, json);
        }
        catch
        {
            // best effort — the server treats a missing/stale descriptor as "no target".
        }
    }

    /// <summary>Removes the descriptor file (best effort). Static so it can run from Deactivate without an instance.</summary>
    public static void Delete(string dir, int year, int pid)
    {
        try
        {
            var path = GetPath(dir, year, pid);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Stops the heartbeat and deletes the descriptor file so the target stops being advertised.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { _heartbeat?.Dispose(); } catch { }
        _heartbeat = null;
        Delete(_dir, _descriptor.InventorYear, _descriptor.ProcessId);
    }
}
