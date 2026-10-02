// Canonical shared runtime source for BIMwright setup layout v1. Vendor unchanged.
// A layout receipt selects paths; it is NOT publisher verification or installer ownership proof.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Setup
{
    public sealed class RuntimeLayout
    {
        private static readonly Dictionary<string, FileStream> ProcessLeases = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, RuntimeLayout> ProcessLayouts = new Dictionary<string, RuntimeLayout>(StringComparer.OrdinalIgnoreCase);
        public string ProductId { get; }
        public string FamilyRoot { get; }
        public string ProductRoot { get; }
        public string DataRoot { get; }
        public string RuntimeRoot { get; }
        public string GatewayLogPath { get; }
        public string WriterLeasePath { get; }
        public bool Managed { get; }
        public string? GenerationId { get; }
        public int? DataSchema { get; }

        private RuntimeLayout(string localRoot, string productId, bool managed, string? generation, int? schema)
        {
            ProductId = productId;
            FamilyRoot = Path.Combine(localRoot, managed ? "BIMwright" : "Bimwright");
            ProductRoot = Path.Combine(FamilyRoot, productId);
            DataRoot = managed ? Path.Combine(ProductRoot, "data") : ProductRoot;
            RuntimeRoot = managed ? Path.Combine(ProductRoot, "runtime") : ProductRoot;
            GatewayLogPath = Path.Combine(managed ? DataRoot : FamilyRoot, productId + "-calls.jsonl");
            WriterLeasePath = Path.Combine(FamilyRoot, ".setup", "leases", productId + ".lock");
            Managed = managed; GenerationId = generation; DataSchema = schema;
            foreach (var path in new[] { ProductRoot, DataRoot, RuntimeRoot, WriterLeasePath }) AssertNoReparse(path);
        }

        public static RuntimeLayout ForCurrentUser(string productId)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var sid = CurrentSid();
            lock (ProcessLeases)
            {
                if (ProcessLayouts.TryGetValue(ProfileKey(local, sid, productId), out var active)) return active;
                return Resolve(local, sid, productId);
            }
        }

        public static RuntimeLayout StartForCurrentUser(string productId) => Start(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CurrentSid(), productId);

        public static RuntimeLayout Start(string local, string actorSid, string productId)
        {
            lock (ProcessLeases)
            {
                ValidateProduct(productId);
                ValidateLocalRoot(local);
                var key = ProfileKey(local, actorSid, productId);
                if (ProcessLayouts.TryGetValue(key, out var active)) return active;
                // Acquire the barrier BEFORE resolving the receipt. Otherwise a cutover
                // between Resolve and Acquire could start a writer on stale legacy paths.
                new RuntimeLayout(local, productId, false, null, null).HoldWriterLeaseForProcess();
                active = Resolve(local, actorSid, productId);
                ProcessLayouts.Add(key, active);
                return active;
            }
        }

        // Explicit arguments allow host-free fixtures. Production callers always use ForCurrentUser.
        public static RuntimeLayout Resolve(string localRoot, string actorSid, string productId)
        {
            ValidateProduct(productId);
            ValidateLocalRoot(localRoot);
            localRoot = Path.GetFullPath(localRoot);
            var root = Path.Combine(localRoot, "BIMwright", productId);
            var receiptPath = Path.Combine(root, "install.json");
            AssertNoReparse(receiptPath);
            if (!File.Exists(receiptPath))
            {
                if (Directory.Exists(receiptPath) || Exists(Path.Combine(root, "app")) ||
                    Exists(Path.Combine(root, "data")) || Exists(Path.Combine(root, "runtime")))
                    throw new InvalidOperationException("setup_managed_receipt_missing");
                return new RuntimeLayout(localRoot, productId, false, null, null);
            }
            JObject receipt;
            try
            {
                using (var stream = new FileStream(receiptPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > 1024 * 1024) throw new InvalidOperationException("receipt_too_large");
                    using (var text = new StreamReader(stream, new UTF8Encoding(false, true), true))
                    using (var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
                    {
                        receipt = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                        if (reader.Read()) throw new InvalidOperationException("trailing_receipt_content");
                    }
                }
                RequireInteger(receipt, "schemaVersion", 2);
                RequireString(receipt, "profile", "windows-current-user-v2");
                RequireString(receipt, "productId", productId);
                RequireString(receipt, "actorSid", actorSid);
                RequireInteger(receipt, "layoutVersion", 1);
                RequireInteger(receipt, "dataSchema", 1);
                var generation = receipt["generationId"];
                var generationValue = generation?.Type == JTokenType.String ? generation.Value<string>() : null;
                if (generationValue == null || !Regex.IsMatch(generationValue, "\\A[0-9a-f]{32}\\z"))
                    throw new InvalidOperationException("generation_invalid");
                var lifecycle = receipt["lifecycle"]?.Type == JTokenType.String ? receipt["lifecycle"]!.Value<string>() : null;
                if (lifecycle != "installed_pending_activation" && lifecycle != "verified")
                    throw new InvalidOperationException("installation_not_active");
                return new RuntimeLayout(localRoot, productId, true, generationValue, 1);
            }
            catch (Exception error) when (error is JsonException || error is IOException || error is ArgumentException ||
                error is InvalidOperationException || error is OverflowException || error is InvalidCastException || error is UnauthorizedAccessException)
            {
                // A recognized managed install must never silently revert to legacy stores.
                throw new InvalidOperationException("setup_managed_receipt_invalid");
            }
        }

        public FileStream AcquireWriterLease()
        {
            AssertNoReparse(WriterLeasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(WriterLeasePath)!);
            // FileShare.Read lets multiple writers hold the barrier. Maintenance needs
            // FileShare.None, so it cannot overlap any cooperating runtime process.
            return new FileStream(WriterLeasePath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
        }

        public void HoldWriterLeaseForProcess()
        {
            lock (ProcessLeases)
            {
                if (!ProcessLeases.ContainsKey(WriterLeasePath))
                    ProcessLeases.Add(WriterLeasePath, AcquireWriterLease());
            }
            // Intentionally retained through add-in Deactivate and finalizers. Only
            // process exit releases it; teardown callbacks can still write logs/data.
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        private static void ValidateLocalRoot(string path)
        {
            // Windows rooted paths such as C:relative or \\relative are not absolute.
            if (string.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"\A[A-Za-z]:[\\/]"))
                throw new InvalidOperationException("setup_profile_path_invalid");
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current)) throw new InvalidOperationException("setup_profile_path_invalid");
                current = Path.GetDirectoryName(current);
            }
        }
        private static string ProfileKey(string local, string sid, string product) => Path.GetFullPath(local) + "|" + sid + "|" + product;
        private static string CurrentSid()
        {
#if NET5_0_OR_GREATER
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("setup_windows_required");
#endif
            using (var identity = WindowsIdentity.GetCurrent())
                return identity.User?.Value ?? throw new InvalidOperationException("setup_user_identity_missing");
        }
        private static void ValidateProduct(string productId)
        {
            if (productId != "ipt-mcp" && productId != "rvt-mcp" && productId != "dwg-mcp" && productId != "nwd-mcp")
                throw new InvalidOperationException("setup_product_invalid");
        }
        private static void RequireString(JObject value, string name, string expected)
        {
            if (value[name]?.Type != JTokenType.String || (string?)value[name] != expected)
                throw new InvalidOperationException("receipt_identity_invalid");
        }
        private static void RequireInteger(JObject value, string name, int expected)
        {
            if (value[name]?.Type != JTokenType.Integer || (long)value[name]! != expected)
                throw new InvalidOperationException("receipt_schema_invalid");
        }
        private static void AssertNoReparse(string path)
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("setup_reparse_point");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
        }
    }
}
