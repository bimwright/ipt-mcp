using System;

namespace Bimwright.Ipt.Server.Bake;

public static class BakeSuggestionStates
{
    public const string Open = "open";
    public const string Accepted = "accepted";
    public const string Dismissed = "dismissed";
    public const string Archived = "archived";

    public static bool IsValid(string state)
        => string.Equals(state, Open, StringComparison.Ordinal)
        || string.Equals(state, Accepted, StringComparison.Ordinal)
        || string.Equals(state, Dismissed, StringComparison.Ordinal)
        || string.Equals(state, Archived, StringComparison.Ordinal);
}
