using Verifiabl.Client;
using Verifiabl.Internal;
using Xunit;

namespace Verifiabl.Tests;

public class V2IssuanceTests
{
    private static readonly DateTimeOffset IssuedAt = DateTimeOffset.Parse("2026-09-04T00:00:00Z");
    private static readonly byte[] Key = new byte[32];

    [Fact]
    public void PreparesAustralianSelfManagedApiManagedAndBatchRequests()
    {
        var prepared = V2Issuance.PrepareAustralian(new AustralianPiiFields { EmployeeName = "Private Person" },
            new AustralianPayslipV2 { PeriodEnd = "2026-08-31", PaymentDate = "2026-09-04", Currency = "AUD", Gross = 9000m, Paygw = 2250m, Net = 6750m },
            IssuedAt, Key);
        Assert.Equal(PayslipSchemas.AustralianV2, prepared.Registration.Schema);
        Assert.Equal(prepared.VerifiablReference, prepared.Registration.VerifiablReference);
        Assert.Equal(prepared.VerifiablReference, prepared.BarcodeParts(prepared.VerifiablReference).VerifiablReference);
        Assert.Equal(prepared.BarcodeParts(prepared.VerifiablReference).EncryptedPii, prepared.ApiManagedRegistration.EncryptedPii);
        Assert.False(Wire.ToWire(prepared.ApiManagedRegistration).ContainsKey("verifiabl_reference"));
        Assert.Equal(PayslipSchemas.AustralianV2, Wire.ToWire(prepared.Registration, prepared.VerifiablReference)["schema"]!.GetValue<string>());
        var batch = new BatchRecord
        {
            VerifiablReference = prepared.VerifiablReference,
            Schema = prepared.Registration.Schema,
            IssuedAt = prepared.Registration.IssuedAt,
            PayslipNonPii = prepared.Registration.PayslipNonPii,
            EncryptionMetadata = prepared.Registration.EncryptionMetadata,
        };
        Assert.Single(Wire.ToWire(new[] { batch })["records"]!.AsArray());
    }

    [Fact]
    public void PreparedAustralianRequestsSnapshotInputAndReturnedValues()
    {
        var earnings = new List<AustralianPayslipV2EarningsItem>
        {
            new() { Type = "ordinary", Amount = 100.50m },
        };
        var payslip = new AustralianPayslipV2
        {
            PeriodEnd = "2026-08-31",
            PaymentDate = "2026-09-04",
            Currency = "AUD",
            Gross = 9000.00m,
            Paygw = 2250.00m,
            Net = 6750.00m,
            Earnings = earnings,
        };
        var prepared = V2Issuance.PrepareAustralian(new AustralianPiiFields(), payslip, IssuedAt, Key);
        string originalRegistration = Wire.ToWire(prepared.Registration, prepared.VerifiablReference).ToJsonString();
        string originalApiManaged = Wire.ToWire(prepared.ApiManagedRegistration).ToJsonString();
        Assert.Contains("\"gross\":\"9000.00\"", originalRegistration);
        Assert.Contains("\"amount\":\"100.50\"", originalRegistration);
        byte[] originalCiphertext = prepared.BarcodeParts(prepared.VerifiablReference).EncryptedPii;
        var registration = prepared.Registration;
        var apiManaged = prepared.ApiManagedRegistration;
        var parts = prepared.BarcodeParts(prepared.VerifiablReference);

        earnings.Clear();
        registration.PayslipNonPii.PeriodEnd = "2026-01-01";
        Assert.IsType<List<AustralianPayslipV2EarningsItem>>(
            ((AustralianPayslipV2)registration.PayslipNonPii.TypedV2Payload!).Earnings).Clear();
        registration.EncryptionMetadata.Iv[0] ^= 0xff;
        registration.EncryptionMetadata.Tag[0] ^= 0xff;
        apiManaged.PayslipNonPii.PeriodEnd = "2026-01-01";
        Assert.IsType<List<AustralianPayslipV2EarningsItem>>(
            ((AustralianPayslipV2)apiManaged.PayslipNonPii.TypedV2Payload!).Earnings).Clear();
        apiManaged.EncryptionMetadata.Iv[0] ^= 0xff;
        apiManaged.EncryptionMetadata.Tag[0] ^= 0xff;
        apiManaged.EncryptedPii[0] ^= 0xff;
        parts.EncryptedPii[0] ^= 0xff;

        Assert.Equal(originalRegistration, Wire.ToWire(prepared.Registration, prepared.VerifiablReference).ToJsonString());
        Assert.Equal(originalApiManaged, Wire.ToWire(prepared.ApiManagedRegistration).ToJsonString());
        Assert.Equal(originalCiphertext, prepared.BarcodeParts(prepared.VerifiablReference).EncryptedPii);
    }

    [Fact]
    public void PreparesNewZealandWithPersistentReference()
    {
        string reference = VerifiablReference.Generate();
        var earnings = new List<NewZealandPayslipV2EarningsItem> { new() { Type = "ordinary", Amount = 123.40m } };
        var balances = new NewZealandPayslipV2LeaveBalances
        {
            Annual = new NewZealandPayslipV2LeaveBalancesAnnual { Amount = 5.50m, Unit = "days" },
        };
        var prepared = V2Issuance.PrepareNewZealand(new NewZealandPiiFields { EmployeeName = "Private Person" },
            new NewZealandPayslipV2 { PeriodEnd = "2026-08-31", PaymentDate = "2026-09-04", Currency = "NZD", Gross = 7600m, Paye = 1710m, Net = 5890m, Earnings = earnings, LeaveBalances = balances },
            IssuedAt, Key, reference);
        Assert.Equal(PayslipSchemas.NewZealandV2, prepared.Registration.Schema);
        Assert.Equal(reference, prepared.Registration.VerifiablReference);
        Assert.Equal(reference, prepared.Registration.VerifiablReference);
        Assert.False(Wire.ToWire(prepared.ApiManagedRegistration).ContainsKey("verifiabl_reference"));
        string original = Wire.ToWire(prepared.Registration, reference).ToJsonString();
        Assert.Contains("\"amount\":\"123.40\"", original);
        Assert.Contains("\"amount\":\"5.50\"", original);
        var first = prepared.Registration;
        var second = prepared.ApiManagedRegistration;
        var firstPayload = Assert.IsType<NewZealandPayslipV2>(first.PayslipNonPii.TypedV2Payload);
        var secondPayload = Assert.IsType<NewZealandPayslipV2>(second.PayslipNonPii.TypedV2Payload);
        Assert.NotSame(balances, firstPayload.LeaveBalances);
        Assert.NotSame(firstPayload.LeaveBalances, secondPayload.LeaveBalances);
        Assert.NotSame(firstPayload.LeaveBalances!.Annual, secondPayload.LeaveBalances!.Annual);
        earnings.Clear();
        first.PayslipNonPii.PeriodEnd = "2026-01-01";
        Assert.IsType<List<NewZealandPayslipV2EarningsItem>>(firstPayload.Earnings).Clear();
        second.PayslipNonPii.PeriodEnd = "2026-01-01";
        Assert.IsType<List<NewZealandPayslipV2EarningsItem>>(secondPayload.Earnings).Clear();
        Assert.Equal(original, Wire.ToWire(prepared.Registration, reference).ToJsonString());
        Assert.Contains("\"amount\":\"123.40\"", Wire.ToWire(prepared.ApiManagedRegistration).ToJsonString());
    }
}
