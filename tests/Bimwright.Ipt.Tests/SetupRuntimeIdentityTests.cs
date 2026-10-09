using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Setup;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class SetupRuntimeIdentityTests
{
    [Fact]
    public void LegacyIdentityReportsLoadedArtifactWithoutClaimingTrust()
    {
        var root = Path.Combine(Path.GetTempPath(), "bw-identity-" + Guid.NewGuid().ToString("N"));
        var layout = RuntimeLayout.Resolve(root, "fixture-sid", "ipt-mcp");
        var assembly = typeof(SetupRuntimeIdentity).Assembly;
        var value = SetupRuntimeIdentity.Capture(layout, assembly, "plugin", 2027);
        Assert.False(value.Value<bool>("managed"));
        Assert.False(value.Value<bool>("publisher_verified"));
        Assert.Null(value.Value<string>("generation_id"));
        Assert.Equal(JTokenType.Null, JObject.Parse(value.ToString())["generation_id"]!.Type);
        Assert.Equal(2027, value.Value<int>("host_year"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(), value.Value<string>("artifact_sha256"));
        Assert.Equal(assembly.ManifestModule.ModuleVersionId.ToString("D"), value.Value<string>("module_version_id"));
        Assert.Equal("loaded_assembly", value.Value<string>("identity_basis"));
        Assert.Equal("on_disk_at_identity_capture", value.Value<string>("artifact_hash_basis"));
        Assert.NotNull(value.Value<string>("identity_captured_utc"));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Memory_loaded_plugin_keeps_runtime_identity_without_attributing_the_host_executable_to_it()
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("runtime-identity", isCollectible: true);
        try
        {
            var assembly = typeof(SetupRuntimeIdentity).Assembly;
            using var stream = new MemoryStream(File.ReadAllBytes(assembly.Location));
            var loaded = context.LoadFromStream(stream);
            var identity = SetupRuntimeIdentity.Capture(RuntimeLayout.Resolve(Path.GetTempPath(), "fixture-sid", "ipt-mcp"), loaded, "plugin", 2027);
            Assert.Equal(loaded.ManifestModule.ModuleVersionId.ToString("D"), identity.Value<string>("module_version_id"));
            Assert.Equal(assembly.GetCustomAttributes<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,
                identity.Value<string>("assembly_informational_version"));
            Assert.Null(identity.Value<string>("artifact_file"));
            Assert.Null(identity.Value<string>("artifact_sha256"));
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void ManagedGenerationIsBoundToRoutingReceiptAndNeverImpliesPublisherTrust()
    {
        var root = Path.Combine(Path.GetTempPath(), "bw-identity-" + Guid.NewGuid().ToString("N"));
        var product = Path.Combine(root, "BIMwright", "ipt-mcp");
        Directory.CreateDirectory(product);
        try
        {
            var receipt = new JObject { ["schemaVersion"] = 2, ["profile"] = "windows-current-user-v2", ["productId"] = "ipt-mcp",
                ["actorSid"] = "fixture-sid", ["layoutVersion"] = 1, ["dataSchema"] = 1,
                ["generationId"] = new string('a', 32), ["lifecycle"] = "installed_pending_activation" };
            File.WriteAllText(Path.Combine(product, "install.json"), receipt.ToString());
            var identity = SetupRuntimeIdentity.Capture(RuntimeLayout.Resolve(root, "fixture-sid", "ipt-mcp"),
                typeof(SetupRuntimeIdentity).Assembly, "gateway");
            Assert.True(identity.Value<bool>("managed"));
            Assert.Equal(new string('a', 32), identity.Value<string>("generation_id"));
            Assert.Equal(1, identity.Value<int>("data_schema"));
            Assert.False(identity.Value<bool>("publisher_verified"));
        }
        finally
        {
            Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(root));
            Directory.Delete(root, true);
        }
    }
}
