using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class CnpjTests
{
    [Theory]
    [InlineData("11222333000181", "11.222.333/0001-81")]
    [InlineData("11.222.333/0001-81", "11.222.333/0001-81")]
    [InlineData("06990590000123", "06.990.590/0001-23")]
    public void Create_ShouldSucceed_ForValidCnpj(string input, string expectedFormatted)
    {
        var result = Cnpj.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedFormatted, result.Value.Formatted);
        Assert.Equal(14, result.Value.Digits.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567890123")]
    [InlineData("00000000000000")]
    [InlineData("11111111111111")]
    [InlineData("11222333000182")]
    [InlineData("11.222.333/0001-91")]
    public void Create_ShouldFail_ForInvalidCnpj(string? input)
    {
        var result = Cnpj.Create(input);

        Assert.True(result.IsFailure);
    }
}
