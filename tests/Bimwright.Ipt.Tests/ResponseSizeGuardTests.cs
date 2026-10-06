using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// <see cref="ResponseSizeGuard.Check"/> passes payloads at/under the byte limit and
/// fails larger ones with RESPONSE_TOO_LARGE plus the configured limit in the message.
/// </summary>
public sealed class ResponseSizeGuardTests
{
    [Fact]
    public void UnderLimitPassesThrough()
    {
        Assert.True(ResponseSizeGuard.Check("small", 1024, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void AtExactLimitPassesThrough()
    {
        // 5 ASCII bytes, limit 5 → allowed (boundary is inclusive).
        Assert.True(ResponseSizeGuard.Check("12345", 5, out _));
    }

    [Fact]
    public void OverLimitReportsCodeAndByteLimit()
    {
        var ok = ResponseSizeGuard.Check(new string('x', 5000), 2048, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal(InventorErrorCodes.RESPONSE_TOO_LARGE, error!.Code);
        Assert.Contains("2048", error.Message);
    }

    // ---- F3-a: three-tier Evaluate ------------------------------------------------------

    [Fact]
    public void Evaluate_SmallPayload_NoWarningNoReject()
    {
        var d = ResponseSizeGuard.Evaluate("list_parameters", "small");
        Assert.False(d.Reject);
        Assert.Null(d.WarningLevel);
        Assert.Null(d.AgentWarning);
    }

    [Fact]
    public void Evaluate_AtWarningBoundary_Warns()
    {
        var d = ResponseSizeGuard.Evaluate("get_assembly_bom", new string('x', ResponseSizeGuard.WarningBytes));
        Assert.Equal("warning", d.WarningLevel);
        Assert.False(d.Reject);
        Assert.Contains("warning", d.AgentWarning);
        Assert.Contains("max_rows", d.AgentWarning);   // per-command hint from the catalog
    }

    [Fact]
    public void Evaluate_BelowWarningBoundary_NoWarning()
    {
        var d = ResponseSizeGuard.Evaluate("x", new string('x', ResponseSizeGuard.WarningBytes - 1));
        Assert.Null(d.WarningLevel);
    }

    [Fact]
    public void Evaluate_AtStrongBoundary_StillPlainWarning()
    {
        // '>' boundary: exactly 256 KiB is still the warning band.
        var d = ResponseSizeGuard.Evaluate("x", new string('x', ResponseSizeGuard.StrongWarningBytes));
        Assert.Equal("warning", d.WarningLevel);
        Assert.False(d.Reject);
    }

    [Fact]
    public void Evaluate_AboveStrongThreshold_StrongWarning()
    {
        var d = ResponseSizeGuard.Evaluate("x", new string('x', ResponseSizeGuard.StrongWarningBytes + 1));
        Assert.Equal("strong_warning", d.WarningLevel);
        Assert.Contains("strong warning", d.AgentWarning);
    }

    [Fact]
    public void Evaluate_AtRejectBoundary_StillStrongWarning()
    {
        // '>' boundary: exactly 700 KiB warns strongly but is not rejected.
        var d = ResponseSizeGuard.Evaluate("x", new string('x', ResponseSizeGuard.RejectBytes));
        Assert.Equal("strong_warning", d.WarningLevel);
        Assert.False(d.Reject);
    }

    [Fact]
    public void Evaluate_NullCommandAndPayload_NoWarningNoReject()
    {
        var d = ResponseSizeGuard.Evaluate(null, null);
        Assert.False(d.Reject);
        Assert.Null(d.WarningLevel);
    }

    [Fact]
    public void Evaluate_AboveRejectBudget_RejectsWithHint()
    {
        var d = ResponseSizeGuard.Evaluate("send_code", new string('x', ResponseSizeGuard.RejectBytes + 1));
        Assert.True(d.Reject);
        Assert.Null(d.WarningLevel);
        Assert.Contains("send_code", d.RejectError);
        Assert.Contains("auto-spills", d.RejectError);  // send_code-specific hint
    }

    [Fact]
    public void Evaluate_UnknownCommand_UsesFallbackHint()
    {
        var d = ResponseSizeGuard.Evaluate("nonexistent_cmd", new string('x', ResponseSizeGuard.RejectBytes + 1));
        Assert.True(d.Reject);
        Assert.Contains("Narrow the query", d.RejectError);
    }
}
