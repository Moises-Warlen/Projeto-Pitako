using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class QuotaLimitTests
{
    [Fact]
    public void Unlimited_ShouldHaveNullMaxAllowed_AndAllowAnyPositiveUsageWithoutArtificialCap()
    {
        var limit = QuotaLimit.Unlimited();

        Assert.True(limit.IsUnlimited);
        Assert.Null(limit.MaxAllowed);
        Assert.True(limit.CanAllocate(0));
        Assert.True(limit.CanAllocate(1_000_000));
        Assert.True(limit.CanAllocate(int.MaxValue - 10, 10));
    }

    [Fact]
    public void Limited_ShouldEnforceExactBoundary()
    {
        var limit = QuotaLimit.Limited(3).Value;

        Assert.False(limit.IsUnlimited);
        Assert.Equal(3, limit.MaxAllowed);
        Assert.True(limit.CanAllocate(0));
        Assert.True(limit.CanAllocate(2));
        Assert.False(limit.CanAllocate(3));
        Assert.False(limit.CanAllocate(2, 2));
    }

    [Fact]
    public void Limited_ShouldRejectNegativeMaxAllowed()
    {
        var result = QuotaLimit.Limited(-1);

        Assert.True(result.IsFailure);
        Assert.Equal("QuotaLimit.Negative", result.Error.Code);
    }
}
