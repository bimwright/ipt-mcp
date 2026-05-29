using System;
using System.Security.Cryptography;

namespace Bimwright.Inventor.Shared.Transport;

/// <summary>
/// Token helper for the descriptor-based auth model. The add-in generates a random token,
/// writes it into its TargetDescriptor (the source of truth), and hands the same token to
/// the transport at <c>Start</c>. The transport verifies each incoming envelope's
/// <c>auth_token</c> against that value with a constant-time compare.
///
/// Unlike rvt's global discovery-file <c>AuthToken</c>, this helper holds no static state and
/// writes no files — the descriptor (written by TargetDescriptorWriter) is authoritative.
/// </summary>
public static class AuthToken
{
    /// <summary>Generates a cryptographically-random 256-bit token as a lowercase hex string.</summary>
    public static string Generate()
    {
        var bytes = new byte[32];
#if NET5_0_OR_GREATER
        RandomNumberGenerator.Fill(bytes);
#else
        using (var rng = new RNGCryptoServiceProvider())
        {
            rng.GetBytes(bytes);
        }
#endif
        return ToHexLower(bytes);
    }

    /// <summary>Constant-time comparison of a candidate token against the expected token.</summary>
    public static bool Verify(string? expected, string? candidate)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(candidate)) return false;
        if (candidate!.Length != expected!.Length) return false;
        int diff = 0;
        for (int i = 0; i < expected.Length; i++)
            diff |= expected[i] ^ candidate[i];
        return diff == 0;
    }

    private static string ToHexLower(byte[] bytes)
    {
#if NET5_0_OR_GREATER
        return Convert.ToHexString(bytes).ToLowerInvariant();
#else
        var c = new char[bytes.Length * 2];
        const string hex = "0123456789abcdef";
        for (int i = 0; i < bytes.Length; i++)
        {
            c[i * 2] = hex[bytes[i] >> 4];
            c[i * 2 + 1] = hex[bytes[i] & 0xF];
        }
        return new string(c);
#endif
    }
}
