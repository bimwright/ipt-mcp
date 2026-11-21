using System;

namespace Bimwright.Ipt.Shared.Handlers.Code;

/// <summary>Host-free settings of the <c>send_code</c> script helpers (<c>Ipt.*</c>, loop guard, number cleaning).</summary>
public static class SendCodeHelpers
{
    /// <summary>Descriptor capability: the add-in imports <c>Ipt</c>, guards loops and cleans returned numbers.</summary>
    public const string Capability = Bimwright.Ipt.Shared.Contracts.SendCodeHelperHints.Capability;

    /// <summary>Descriptor capability: also <c>Ipt.Api</c>, <c>Ipt.Box(Point, Point)</c> and <c>Bbox.ToArray()</c> / a <c>(double[])</c> cast of a <c>Bbox</c>.</summary>
    public const string CapabilityV2 = Bimwright.Ipt.Shared.Contracts.SendCodeHelperHints.CapabilityV2;

    /// <summary>
    /// Time budget for the script's loops: what is left until the caller's deadline, minus a margin so the script
    /// stops and answers before the add-in reports TIMEOUT. 0 = no budget (no deadline known).
    /// </summary>
    public static long BudgetMs(DateTime? deadlineUtc, DateTime nowUtc)
    {
        if (deadlineUtc is not { } deadline) return 0;
        var left = (long)(deadline - nowUtc).TotalMilliseconds;
        var margin = Math.Min(2000, Math.Max(200, left / 20));
        return Math.Max(1, left - margin);
    }
}
