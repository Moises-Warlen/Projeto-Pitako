using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class MoneyTests
{
    [Fact]
    public void Create_ShouldRejectNegativeAmount_ByDefault()
    {
        var result = Money.Create(-10.50m);

        Assert.True(result.IsFailure);
        Assert.Equal("Money.Negative", result.Error.Code);
    }

    [Fact]
    public void Arithmetic_ShouldPreservePrecision_AndRejectDifferentCurrencies()
    {
        var a = Money.Create(15.25m, "brl").Value;
        var b = Money.Create(4.75m, "BRL").Value;

        var sum = a.Add(b);
        var multiplied = sum.Multiply(2m);
        var usd = Money.Create(10m, "USD").Value;

        Assert.Equal(20.00m, sum.Amount);
        Assert.Equal("BRL", sum.Currency);
        Assert.Equal(40.00m, multiplied.Amount);
        Assert.Throws<InvalidOperationException>(() => a.Add(usd));
    }
}
