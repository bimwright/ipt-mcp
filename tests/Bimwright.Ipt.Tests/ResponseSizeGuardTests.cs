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
}
