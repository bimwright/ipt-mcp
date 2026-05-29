using System;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Security;

public static class SecretMasker
{
    private static readonly Regex TokenField = new("(\"auth_token\"\\s*:\\s*\")[^\"]+(\")", RegexOptions.Compiled);
    private static readonly Regex LongSecret = new("\\b[A-Za-z0-9+/=]{24,}\\b", RegexOptions.Compiled);

    public static string Mask(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        var s = TokenField.Replace(input, "$1***$2");
        s = LongSecret.Replace(s, "***");
        return s;
    }
}
