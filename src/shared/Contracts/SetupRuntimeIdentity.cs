using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Bimwright.Setup;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Observed runtime identity. The installer must match it to signed artifact bytes;
/// these fields alone are not installation or publisher verification.</summary>
public static class SetupRuntimeIdentity
{
    public static JObject Capture(RuntimeLayout layout, Assembly assembly, string component, int? hostYear = null)
    {
        if (component != "gateway" && component != "plugin") throw new ArgumentException("Unknown setup component.", nameof(component));
        using var process = Process.GetCurrentProcess();
        var location = assembly.Location;
        // Single-file gateways have no managed Assembly.Location. Identify their
        // executable instead; never attribute the Inventor executable to a plugin.
        if (string.IsNullOrEmpty(location) && component == "gateway" && assembly == Assembly.GetEntryAssembly())
            location = process.MainModule?.FileName;
        string? hash = null;
        if (!string.IsNullOrEmpty(location))
        {
            using var stream = new FileStream(location, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        return new JObject
        {
            ["schema_version"] = 2,
            ["product_id"] = layout.ProductId,
            ["component"] = component,
            ["managed"] = layout.Managed,
            ["generation_id"] = layout.GenerationId,
            ["data_schema"] = layout.DataSchema,
            ["host_year"] = hostYear,
            ["process_id"] = process.Id,
            ["process_started_utc"] = process.StartTime.ToUniversalTime().ToString("o"),
            ["assembly_informational_version"] = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            ["module_version_id"] = assembly.ManifestModule.ModuleVersionId.ToString("D"),
            ["artifact_file"] = string.IsNullOrEmpty(location) ? null : Path.GetFileName(location),
            ["artifact_sha256"] = hash,
            ["publisher_verified"] = false,
        };
    }
}
