using Verifiabl;
using Verifiabl.Client;

// Example: pass an authenticated client and a key loaded from a secrets manager.
internal static class PreparedV2Example
{
    internal static async Task<(BarcodeSvgResult, RegisterAndBuildBarcodeResponse)> IssueAsync(
        IVerifiablClient client, byte[] providerKey)
    {
        PreparedV2Payslip au = V2Issuance.PrepareAustralian(
            pii: new AustralianPiiFields { EmployeeName = "Example Employee", EmployerName = "Example Pty Ltd" },
            payslip: new AustralianPayslipV2 { PeriodEnd = "2026-08-31", PaymentDate = "2026-09-04", Currency = "AUD", Gross = 9000m, Paygw = 2250m, Net = 6750m },
            issuedAt: DateTimeOffset.UtcNow, key: providerKey);
        // Atomically persist au.Registration and
        // au.BarcodeParts(au.VerifiablReference).EncryptedPii before the request.
        // On restart, replay the same registration and render with the saved ciphertext.
        RegisterNonPiiResponse result = await client.RegisterNonPiiAsync(au.Registration);
        BarcodeParts parts = au.BarcodeParts(result.VerifiablReference);
        BarcodeSvgResult svg = VerifiablBarcode.CreateSvg(parts, new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });

        PreparedV2Payslip nz = V2Issuance.PrepareNewZealand(
            pii: new NewZealandPiiFields { EmployeeName = "Example Employee", EmployerName = "Example NZ Ltd" },
            payslip: new NewZealandPayslipV2 { PeriodEnd = "2026-08-31", PaymentDate = "2026-09-04", Currency = "NZD", Gross = 7600m, Paye = 1710m, Net = 5890m },
            issuedAt: DateTimeOffset.UtcNow, key: providerKey);
        // No reference in this request: the API assigns one. Ambiguous failures
        // cannot safely be retried like self-managed registrations.
        RegisterAndBuildBarcodeResponse apiManaged = await client.RegisterAndBuildBarcodeAsync(nz.ApiManagedRegistration);
        // For batches, construct BatchRecord from each prepared.Registration and
        // pair each response reference with that prepared.BarcodeParts(reference).
        return (svg, apiManaged);
    }
}
