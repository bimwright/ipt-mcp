using System;

namespace Bimwright.Ipt.Shared.Logging
{
    /// <summary>
    /// Privacy knobs for the command-history log and the send-code journal — the ipt-mcp
    /// counterpart of the rvt-mcp privacy-config subset. rvt reads a JSON config file;
    /// ipt is env-driven, so the same opt-ins arrive through environment variables (same
    /// names as rvt-mcp — the family shares these flags).
    ///
    /// <see cref="CacheSendCodeBodiesOrDefault"/>: keep send_code bodies in the in-memory
    /// session log so History can show + re-run them (default off → body redacted to a hash).
    ///
    /// <see cref="IsPersistSendCodeBodiesActive"/>: persist bake-redacted send_code bodies to
    /// send-code-journal.jsonl for a bounded window after the add-in starts (default off).
    /// Because there is no config-file "enabled at" timestamp, the add-in activation time
    /// anchors the TTL window.
    /// </summary>
    public sealed class IptPrivacyConfig
    {
        public const string EnvCacheSendCodeBodies = "BIMWRIGHT_CACHE_SEND_CODE_BODIES";
        public const string EnvPersistSendCodeBodies = "BIMWRIGHT_PERSIST_SEND_CODE_BODIES";
        public const string EnvPersistSendCodeBodiesTtl = "BIMWRIGHT_PERSIST_SEND_CODE_BODIES_TTL";

        /// <summary>First Load() time — anchors the persist TTL for the env-driven model.</summary>
        private static readonly DateTimeOffset ActivatedUtc = DateTimeOffset.UtcNow;

        public bool CacheSendCodeBodiesOrDefault { get; }
        public bool PersistSendCodeBodies { get; }
        public TimeSpan PersistSendCodeBodiesTtl { get; }

        public IptPrivacyConfig(Func<string, string?> getEnv)
        {
            CacheSendCodeBodiesOrDefault = Flag(getEnv(EnvCacheSendCodeBodies));
            PersistSendCodeBodies = Flag(getEnv(EnvPersistSendCodeBodies));
            PersistSendCodeBodiesTtl =
                PersistSendCodeTtl.TryParse(getEnv(EnvPersistSendCodeBodiesTtl), out var ttl)
                    ? PersistSendCodeTtl.Clamp(ttl)
                    : PersistSendCodeTtl.Default;
        }

        public bool IsPersistSendCodeBodiesActive(DateTimeOffset? now = null)
            => PersistSendCodeBodies && (now ?? DateTimeOffset.UtcNow) - ActivatedUtc < PersistSendCodeBodiesTtl;

        public static IptPrivacyConfig Load() => new(Environment.GetEnvironmentVariable);

        private static bool Flag(string? value)
            => !string.IsNullOrEmpty(value) &&
               (value!.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }
}
