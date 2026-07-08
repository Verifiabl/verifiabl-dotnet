# Verifiabl .NET SDK

Official .NET SDK for issuing Verifiabl payslip QR codes.

Add a scannable QR code to each payslip you issue. You register the non-PII payslip data with Verifiabl and encrypt the employee's personal details on your own infrastructure, so they live only inside the QR code on the document and never reach Verifiabl.

Verifiabl is for accredited payroll providers. You receive sandbox credentials at onboarding. Full documentation is at [docs.verifiabl.io](https://docs.verifiabl.io/).

## Installation

```bash
dotnet add package Verifiabl
```

Supported targets: .NET 8+, and .NET Framework 4.7.2+ (Windows). On .NET Framework, AES-GCM is provided by the bundled `Microsoft.Bcl.Cryptography` dependency.

## Getting started

This is the self-managed flow: register the payslip, encrypt the personal details locally, and generate the QR code yourself. You need four values from onboarding: your OAuth client ID and secret, your encryption key, and your key version.

```csharp
using Verifiabl;

var client = new VerifiablClient(new VerifiablClientOptions
{
    Environment = VerifiablEnvironment.Sandbox,
    Auth = VerifiablAuth.ClientCredentials(
        Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_ID")!,
        Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_SECRET")!),
});

// Your 32-byte key and key version, from onboarding. Load the key from a secrets manager.
byte[] key = Convert.FromBase64String(
    Environment.GetEnvironmentVariable("VERIFIABL_ENCRYPTION_KEY_BASE64")!);
string keyVersion = Environment.GetEnvironmentVariable("VERIFIABL_KEY_VERSION")!; // e.g. "0f8fad5b-...e.1"

// 1. Format and encrypt the employee's details locally.
string pii = Pii.Format(new PiiFields
{
    EmployeeName = "Jane A. Doe",
    Position = "Senior Developer",
    Department = "Engineering",
    EmployerAbn = "12345678901",
    Bsb = "062-000",
    AccountNumber = "12345678",
    AccountName = "Jane A Doe",
});
EncryptedPii encrypted = VerifiablCrypto.EncryptPii(pii, key, keyVersion);

// 2. Register the non-PII data. Verifiabl returns a Verifiabl reference.
RegisterNonPiiResponse registration = await client.RegisterNonPiiAsync(new RegisterNonPiiRequest
{
    Schema = "au.payslip.v1",
    IssuedAt = DateTimeOffset.UtcNow,
    PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
    EncryptionMetadata = encrypted.Metadata,
});

// 3. Render the QR code and embed the SVG in your payslip PDF.
BarcodeSvgResult badge = VerifiablBarcode.CreateSvg(
    new BarcodeParts(registration.VerifiablReference, encrypted.Ciphertext),
    new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });
```

`VerifiablBarcode.CreateSvg` produces a standalone SVG that scales to any size without losing quality; embed it directly in your PDF pipeline. If you need a raster image instead, let the API build a PNG for you with `client.CreateBarcodeAsync`, or rasterise the SVG with your own renderer. See the [docs](https://docs.verifiabl.io/) for both flows.

Create the client once and reuse it: it caches OAuth tokens and is thread-safe. In services that use dependency injection, supply an `HttpClient` from `IHttpClientFactory` via `VerifiablClientOptions.HttpClient`.

### Retries and idempotency

Failed requests are retried automatically with exponential backoff (`VerifiablClientOptions.MaxRetries`, default 2). The Verifiabl reference is the idempotency key, so retries are only applied where they are safe. Batch registration generates its own references, so the API deduplicates a re-send — it retries on throttling, timeouts, `5xx`, and network faults. The single-record endpoints let the API assign the reference and are not deduplicated, so they retry only failures that leave the request unprocessed (`429`, `503`); use batch when you need fully idempotent retries.

## Batch registration

For pay runs, register up to 1000 records in one request with `RegisterNonPiiBatchAsync`. The provider generates each Verifiabl reference up-front with `VerifiablReference.Generate()` and includes it on each record, so the whole batch can go in one round trip. Results are returned index-aligned to the input; one bad record never fails the whole batch.

```csharp
DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
var prepared = payslips.Select(payslip =>
{
    string verifiablReference = VerifiablReference.Generate();
    EncryptedPii encrypted = VerifiablCrypto.EncryptPii(Pii.Format(payslip.Pii), key, keyVersion);
    // Keep the ciphertext alongside the reference locally: you need both to render the barcode.
    return (verifiablReference, encrypted, payslip);
}).ToList();

RegisterNonPiiBatchResponse batch = await client.RegisterNonPiiBatchAsync(
    prepared.Select(item => new BatchRecord
    {
        VerifiablReference = item.verifiablReference,
        Schema = "au.payslip.v1",
        IssuedAt = issuedAt,
        PayslipNonPii = new PayslipNonPii
        {
            PeriodStart = item.payslip.PeriodStart,
            PeriodEnd = item.payslip.PeriodEnd,
        },
        EncryptionMetadata = item.encrypted.Metadata,
    }));

foreach (BatchRecordResult result in batch.Results)
{
    if (result.Status == BatchRecordStatuses.Error)
    {
        logger.LogError(
            "Record {Reference} failed: {Code} {Detail}",
            result.VerifiablReference, result.Code, result.Detail);
    }
}
```

## Environments

Set `Environment` to `VerifiablEnvironment.Production` (the default) or `VerifiablEnvironment.Sandbox`. Pass the same value to the client and the barcode renderer, so the scan URL printed on the document matches where the record was registered.

## Errors

Failed requests throw `VerifiablApiException` with a stable `Code` and a `RequestId` to quote to support. Auth failures throw `VerifiablAuthException`. Calls that exceed the configured timeout throw `TimeoutException`.

```csharp
try
{
    await client.RegisterNonPiiAsync(request);
}
catch (VerifiablApiException exception) when (exception.Code == VerifiablErrorCodes.ValidationFailed)
{
    logger.LogWarning("Validation failed, request id {RequestId}", exception.RequestId);
}
```

## Security

Employee PII is encrypted on your infrastructure and never reaches Verifiabl. Keep your encryption key and OAuth secret in a secrets manager. See the [security model](https://docs.verifiabl.io/architecture) for the full detail.

## Documentation

Full API reference, the alternative API flow, barcode placement rules, and the security model are at [docs.verifiabl.io](https://docs.verifiabl.io/).

## License

[MIT](./LICENSE)
