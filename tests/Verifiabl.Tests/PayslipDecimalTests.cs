using System.Text.Json;
using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class PayslipDecimalTests
{
    [Fact]
    public void GeneratedModelsWriteDecimalsAsExactStringsWithAnySerializerOptions()
    {
        var payslip = new AustralianPayslipV2
        {
            PeriodEnd = "2026-05-31",
            PaymentDate = "2026-06-01",
            Currency = PayslipCurrencies.Aud,
            Gross = 1.50m,
            Paygw = -0.0001m,
            Net = 1.5m,
            YtdGross = 8125.00m,
        };

        using JsonDocument body = JsonDocument.Parse(JsonSerializer.Serialize(payslip));

        Assert.Equal("1.50", body.RootElement.GetProperty("gross").GetString());
        Assert.Equal("-0.0001", body.RootElement.GetProperty("paygw").GetString());
        Assert.Equal("1.5", body.RootElement.GetProperty("net").GetString());
        Assert.Equal("8125.00", body.RootElement.GetProperty("ytd_gross").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("ytd_paygw").ValueKind);
    }

    [Fact]
    public void GeneratedModelsReadBackTheSameDecimalScale()
    {
        const string json = "{\"period_end\":\"2026-05-31\",\"payment_date\":\"2026-06-01\"," +
            "\"currency\":\"NZD\",\"gross\":\"76.00\",\"paye\":\"-1.5\",\"net\":\"0\",\"hours_paid\":\"47.3684\"}";

        NewZealandPayslipV2 payslip = JsonSerializer.Deserialize<NewZealandPayslipV2>(json)!;

        Assert.Equal("76.00", payslip.Gross.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(-1.5m, payslip.Paye);
        Assert.Equal(47.3684m, payslip.HoursPaid);
        Assert.Null(payslip.YtdGross);
    }

    [Theory]
    [InlineData("76")]
    [InlineData("\"1e3\"")]
    [InlineData("\"1,234.56\"")]
    [InlineData("\"0.12345678901234567890123456789\"")]
    [InlineData("\"007\"")]
    public void RefusesToReadAValueItCouldNotResendExactly(string gross)
    {
        string json = "{\"period_end\":\"2026-05-31\",\"payment_date\":\"2026-06-01\"," +
            $"\"currency\":\"NZD\",\"gross\":{gross},\"paye\":\"0\",\"net\":\"0\"}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NewZealandPayslipV2>(json));
    }
}
