# Verifiabl .NET SDK

Official .NET SDK for issuing Verifiabl payslip QR codes.

Add a scannable QR code to each payslip you issue. You register the non-PII payslip data with Verifiabl and encrypt the employee's personal details on your own infrastructure, so they live only inside the QR code on the document and never reach Verifiabl.

Verifiabl is for accredited payroll providers. You receive sandbox credentials at onboarding. Full documentation is at [docs.verifiabl.io](https://docs.verifiabl.io/).

## Installation

```bash
dotnet add package Verifiabl.Issuer
```

In an app that uses dependency injection, add the integration package too:

```bash
dotnet add package Verifiabl.Issuer.Extensions.DependencyInjection
```

The package id is `Verifiabl.Issuer` (issuer is the role this SDK serves, matching the Node SDK's `@verifiabl/issuer`); the root namespace stays `Verifiabl`.

Supported targets: .NET 8+, and .NET Framework 4.7.2+ (Windows). On .NET Framework, AES-GCM is provided by the bundled `Microsoft.Bcl.Cryptography` dependency.

## Two namespaces: offline and networked

The split is deliberate, so you can see at a glance which half of the SDK touches the network.

| Namespace | Contents | Network |
| --- | --- | --- |
| `Verifiabl` | `Pii`, `PiiFields`, `VerifiablCrypto`, `EncryptedPii`, `EncryptionMetadata`, `VerifiablBarcode`, `BarcodeParts`, `BarcodeSvgOptions`, `VerifiablReference`, `VerifiablEnvironment`, `VerifiablEndpoints` | None. Pure functions you can call from anywhere, including a hot PDF-rendering loop. |
| `Verifiabl.Client` | `IVerifiablClient`, `VerifiablClient`, `VerifiablClientOptions`, `VerifiablAuth`, the request/response types, `VerifiablApiException` and friends | Calls the Verifiabl issuer API. |
| `Verifiabl.Extensions.DependencyInjection` | `AddVerifiablClient` and `VerifiablServiceCollectionExtensions.HttpClientName` from the DI integration package | Registers the networked client in your service collection. |

Encryption, PII formatting, reference generation, and barcode rendering all happen on your infrastructure with no network call — the `Verifiabl` namespace has no client in it to make one.

## Registering the client

With dependency injection, using `Verifiabl.Issuer.Extensions.DependencyInjection`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Verifiabl;
using Verifiabl.Client;
using Verifiabl.Extensions.DependencyInjection;

builder.Services.AddVerifiablClient(options =>
{
    options.Environment = VerifiablEnvironment.Sandbox;
    options.Auth = VerifiablAuth.ClientCredentials(
        builder.Configuration["Verifiabl:ClientId"]!,
        builder.Configuration["Verifiabl:ClientSecret"]!);
});
```

`IVerifiablClient` is registered as a **singleton** — it caches OAuth access tokens, so a shorter lifetime would fetch a new token on every resolve — and its `HttpClient` comes from `IHttpClientFactory`. Inject `IVerifiablClient` wherever you need it; substitute your own implementation in tests.

Without dependency injection, construct it once and reuse it (it is thread-safe):

```csharp
var client = new VerifiablClient(new VerifiablClientOptions
{
    Environment = VerifiablEnvironment.Sandbox,
    Auth = VerifiablAuth.ClientCredentials(clientId, clientSecret),
});
```

## Getting started

This is the self-managed flow: register the payslip, encrypt the personal details locally, and generate the QR code yourself. You need three values from onboarding: your OAuth client ID and secret, and your encryption key.

```csharp
using Verifiabl;
using Verifiabl.Client;

// Your 32-byte key, from onboarding. Load it from a secrets manager.
byte[] key = Convert.FromBase64String(
    Environment.GetEnvironmentVariable("VERIFIABL_ENCRYPTION_KEY_BASE64")!);

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
    Address = "12 Example St, Sydney NSW 2000",
});
EncryptedPii encrypted = VerifiablCrypto.EncryptPii(pii, key);

// 2. Register the non-PII data. Verifiabl returns a Verifiabl reference.
RegisterNonPiiResponse registration = await client.RegisterNonPiiAsync(new RegisterNonPiiRequest
{
    Schema = "au.payslip.v1",
    IssuedAt = DateTimeOffset.UtcNow,
    PayslipNonPii = new PayslipNonPii
    {
        PeriodStart = "2026-05-01",
        PeriodEnd = "2026-05-31",
        // au.payslip.v1 requires these; keys and value types are set by the schema.
        AdditionalData = new Dictionary<string, object?>
        {
            ["payment_date"] = "2026-06-04",
            ["currency"] = "AUD",
            ["gross_cents"] = 812_500,
            ["paygw_cents"] = 203_000,
            ["net_cents"] = 609_500,
            ["ytd_gross_cents"] = 8_937_500,
            ["ytd_paygw_cents"] = 2_233_000,
        },
    },
    EncryptionMetadata = encrypted.Metadata,
});

// 3. Render the QR code and embed the SVG in your payslip PDF.
BarcodeSvgResult badge = VerifiablBarcode.CreateSvg(
    new BarcodeParts(registration.VerifiablReference, encrypted.Ciphertext),
    new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });
```

### V2 / P2 format and V1 rollback

New documents use P2 plaintext and v2 barcode/XMP output by default. P2 is exactly
`P2|employeeName|position|department|employerAbn|bsb|accountNumber|accountName|address`.
The final address is unstructured, optional, preserved verbatim, and limited to 320 UTF-8 bytes.
Pipes, control characters, Unicode format characters, and malformed Unicode are rejected before
encryption. A v2 QR uses the short scan host with `#2.<BASE32>` and an explicit byte/alphanumeric
segment split; its XMP copy is the matching `2|reference|BASE32` returned by
`VerifiablBarcode.BuildPayload(parts)`.

V1/P1 remain permanently supported for existing documents and emergency writer rollback. Select
both explicitly so QR and XMP never mix versions:

```csharp
string legacyPlaintext = Pii.FormatV1(fields);
var legacyOptions = new BarcodeSvgOptions { Format = BarcodePayloadFormat.V1 };
BarcodeSvgResult legacyBadge = VerifiablBarcode.CreateSvg(parts, legacyOptions);
string legacyXmpPayload = VerifiablBarcode.BuildPayload(parts, BarcodePayloadFormat.V1);
```

### Scanner test pack

Generate synthetic v2 symbols for screen, print, fold, photocopy, camera, and hardware-scanner tests:

```bash
dotnet run --project tools/Verifiabl.ScannerPack -- ./artifacts/ver-460
```

Open `artifacts/ver-460/index.html` for screen, print, and fold tests. Open
`artifacts/ver-460/address-size-matrix.html` to compare dense-address QR error-correction and badge
size trade-offs. The output directory must not already exist, so a stale partial pack is never mixed
with a fresh run. The pack includes PNG files and a `manifest.json` file. The manifest records each
exact scan URL, XMP payload, ciphertext byte value, QR version, and error-correction level. All
fixture details are synthetic. Do not replace them with customer data. CI also publishes the same
pack as the `verifiabl-dotnet-scanner-pack` workflow artifact.

### Development shell

The pinned Nix shell supplies the .NET 8 and .NET 10 SDKs and runtimes:

```bash
nix develop
dotnet restore
dotnet test
```

Linux and macOS can build all library targets. Windows CI runs the .NET Framework 4.7.2 tests.


The compiler enforces the mandatory fields: `Schema`, `IssuedAt`, `PayslipNonPii`, and `EncryptionMetadata` are `required`, so an incomplete request will not build.

`AdditionalData` is passed to the API verbatim under the exact keys you supply. Values may be strings, booleans, numbers, `null`, nested dictionaries, or sequences of those; anything else throws an `ArgumentException` naming the key. Which keys your schema requires is documented per schema — the `au.payslip.v1` set is shown above.

`VerifiablBarcode.CreateSvg` produces a standalone SVG that scales to any size without losing quality; embed it directly in your PDF pipeline when it supports vector images. If it needs a raster image, use `VerifiablBarcode.CreatePng`: it composites the badge deterministically with no native dependencies, so the same record produces the byte-identical raster in every Verifiabl SDK, and QR module edges stay crisp (rasterising the SVG with a general renderer blurs them and costs scannability). PNG output comes in fixed pixel widths (480, 720, 960 or 1440; the physical print size is set where you place the image in the PDF). See the [docs](https://docs.verifiabl.io/) for both flows.

### Retries and idempotency

Failed requests are retried automatically with exponential backoff (`VerifiablClientOptions.MaxRetries`, default 2). The Verifiabl reference is the idempotency key, so retries are only applied where they are safe. `RegisterNonPiiAsync` generates a reference client-side (or uses the one you set on the request), so the API deduplicates a re-send and the SDK retries it on throttling, timeouts, `5xx`, and network faults — same as batch registration. `RegisterAndBuildBarcodeAsync` lets the API assign the reference and cannot be deduplicated, so it retries only `429`, which is enforced before any processing.

## Batch registration

For pay runs, register up to 1000 records in one request with `RegisterNonPiiBatchAsync`. The provider generates each Verifiabl reference up-front with `VerifiablReference.Generate()` and includes it on each record, so the whole batch can go in one round trip. Results are returned index-aligned to the input; one bad record never fails the whole batch.

```csharp
DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
var prepared = payslips.Select(payslip =>
{
    string verifiablReference = VerifiablReference.Generate();
    EncryptedPii encrypted = VerifiablCrypto.EncryptPii(Pii.Format(payslip.Pii), key);
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
            AdditionalData = item.payslip.SchemaFields,
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

Every failure an API call reports derives from `VerifiablException`, so one catch clause covers the client. Match the specific types when you want to react differently.

```csharp
try
{
    await client.RegisterNonPiiAsync(request);
}
catch (VerifiablApiException exception) when (exception.Code == VerifiablErrorCodes.ValidationFailed)
{
    logger.LogWarning("Validation failed, request id {RequestId}", exception.RequestId);
}
catch (VerifiablException exception)
{
    // VerifiablApiException, VerifiablAuthException, VerifiablTimeoutException,
    // and VerifiablTransportException all land here.
    logger.LogError(exception, "Verifiabl registration failed");
}
```

| Exception | Raised when |
| --- | --- |
| `VerifiablApiException` | The API returned a non-2xx response. Carries `Status`, a stable `Code`, the parsed `Body`, and a `RequestId` to quote to support. |
| `VerifiablIvReuseException` | A registration was rejected because the record's encryption IV is already registered to your issuer. Derives from `VerifiablApiException`, so the catch clause above still covers it. |
| `VerifiablAuthException` | An OAuth access token could not be obtained. |
| `VerifiablTimeoutException` | The call exceeded `VerifiablClientOptions.Timeout`, which covers the token fetch, the request, and every retry. |
| `VerifiablTransportException` | A network fault prevented a response (the `HttpRequestException` is the `InnerException`), or a 2xx response was not usable JSON. |

Two things are deliberately *not* `VerifiablException`: an `ArgumentException` for an incomplete or malformed request, thrown before anything is sent, and an `OperationCanceledException` when you cancel the `CancellationToken` you passed in.

### Reused encryption IV

Registration rejects an IV that your issuer has already used. `VerifiablCrypto.EncryptPii` draws a fresh IV on every call, so this occurs when stored `EncryptionMetadata` is sent again with different content.

The SDK does not re-encrypt and retry for you. Encrypt the payslip again, resend the record with the new encryption metadata, and rebuild any barcode that you rendered from the previous ciphertext. Resending the record unchanged gives the same result.

Single registrations throw `VerifiablIvReuseException`, whose `Code` is `VerifiablErrorCodes.IvReused`.

```csharp
try
{
    await client.RegisterNonPiiAsync(request);
}
catch (VerifiablIvReuseException)
{
    // Encrypt again for a fresh IV and ciphertext, then register and render again.
    EncryptedPii encrypted = VerifiablCrypto.EncryptPii(pii, key);
}
```

Batch records come back as an error result, which `BatchRecordResult.IsIvReused` matches. It covers both cases the API reports: a collision with a stored record, and a repeat within the same batch (where the first record still registers).

```csharp
RegisterNonPiiBatchResponse batch = await client.RegisterNonPiiBatchAsync(records);
List<string> toReEncrypt = batch.Results
    .Where(result => result.IsIvReused)
    .Select(result => result.VerifiablReference)
    .ToList();
```

## Security

Employee PII is encrypted on your infrastructure and never reaches Verifiabl. Keep your encryption key and OAuth secret in a secrets manager. See the [security model](https://docs.verifiabl.io/architecture) for the full detail.

## Documentation

Full API reference, the alternative API flow, barcode placement rules, and the security model are at [docs.verifiabl.io](https://docs.verifiabl.io/).

## License

[MIT](./LICENSE)
