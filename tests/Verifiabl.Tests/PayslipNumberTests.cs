using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class PayslipNumberTests
{
    [Fact]
    public void PublishesTheV2CurrencyAllowList()
    {
        Assert.Equal(
            ["AUD", "NZD", "USD", "GBP", "EUR", "CAD", "SGD", "HKD", "CHF", "ZAR"],
            PayslipCurrencies.All);
    }

    [Fact]
    public void PreservesAnExactStringValueAndPrintedDisplay()
    {
        var number = new PayslipNumber("47.3684", "$47.3684/hr");

        Assert.Equal("47.3684", number.Value);
        Assert.Equal("$47.3684/hr", number.Display);
    }

    [Fact]
    public void PreservesDecimalScale()
    {
        var number = new PayslipNumber(1.50m);

        Assert.Equal("1.50", number.Value);
        Assert.Null(number.Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1,234.56")]
    [InlineData("$1.00")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("1.5\n")]
    [InlineData("\n1.5")]
    public void RejectsValuesTheCanonicalV2SchemaCannotStore(string value)
    {
        Assert.Throws<ArgumentException>(() => new PayslipNumber(value));
    }
}
