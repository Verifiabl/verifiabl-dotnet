using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class PayslipCurrenciesTests
{
    [Fact]
    public void PublishesPayableIso4217CodesInAlphabeticalOrder()
    {
        Assert.Equal(155, PayslipCurrencies.All.Count);
        Assert.All(["XTS", "XXX", "XAU", "CLF"], code => Assert.DoesNotContain(code, PayslipCurrencies.All));
        Assert.All(["XAF", "XOF", "XCD", "XPF", "JPY"], code => Assert.Contains(code, PayslipCurrencies.All));
        Assert.Equal(PayslipCurrencies.All.OrderBy(code => code, StringComparer.Ordinal), PayslipCurrencies.All);
        Assert.Equal(PayslipCurrencies.All.Count, PayslipCurrencies.All.Distinct().Count());
        Assert.All(PayslipCurrencies.All, code => Assert.Matches("\\A[A-Z]{3}\\z", code));
    }

    [Fact]
    public void NamedConstantsAreAcceptedCodes()
    {
        Assert.Subset(
            PayslipCurrencies.All.ToHashSet(),
            new HashSet<string>
            {
                PayslipCurrencies.Aud,
                PayslipCurrencies.Nzd,
                PayslipCurrencies.Usd,
                PayslipCurrencies.Gbp,
                PayslipCurrencies.Eur,
                PayslipCurrencies.Cad,
                PayslipCurrencies.Sgd,
                PayslipCurrencies.Hkd,
                PayslipCurrencies.Chf,
                PayslipCurrencies.Zar,
            });
    }
}
