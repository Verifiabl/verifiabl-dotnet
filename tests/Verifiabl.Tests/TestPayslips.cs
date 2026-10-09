using Verifiabl.Client;
using static Verifiabl.Tests.TestDates;

namespace Verifiabl.Tests;

internal static class TestPayslips
{
    /// <summary>A synthetic AU2 payslip. Each call returns a new instance.</summary>
    internal static PayslipNonPii Australian() => PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
    {
        PeriodStart = PayslipDate("2026-05-01"),
        PeriodEnd = PayslipDate("2026-05-31"),
        PaymentDate = PayslipDate("2026-06-04"),
        Currency = PayslipCurrencies.Aud,
        Gross = 9000.00m,
        Paygw = 2250.00m,
        Net = 6750.00m,
    });

    /// <summary>A free-form payslip for schemas this SDK has no typed model for.</summary>
    internal static PayslipNonPii FreeForm(
        IEnumerable<KeyValuePair<string, object?>>? additionalData = null,
        string periodStart = "2026-05-01") =>
        PayslipNonPii.ForFutureSchema(periodStart, "2026-05-31", additionalData);
}
