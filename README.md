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
| `Verifiabl` | `Pii`, `PiiFields`, the AU/NZ PII and address models, `VerifiablCrypto`, `EncryptedPii`, `EncryptionMetadata`, `VerifiablBarcode`, `BarcodeParts`, `BarcodeSvgOptions`, `VerifiablReference`, `VerifiablEnvironment`, `VerifiablEndpoints` | None. Pure functions you can call from anywhere, including a hot PDF-rendering loop. |
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

### AU2 and NZ2 payslip profiles

For `au.payslip.v2`, format the encrypted PII with `Pii.FormatAustralian` and
register the matching schema id. The formatter accepts the employer name and
ABN separately, then writes one employer identity: the ABN when supplied,
otherwise the name.

```csharp
string pii = Pii.FormatAustralian(new AustralianPiiFields
{
    EmployeeName = "Jane A. Doe",
    Position = "Senior Developer",
    Department = "Engineering",
    EmployerName = "Example Payroll Pty Ltd",
    EmployerAbn = "12 345 678 901",
    Bsb = "062-000",
    AccountNumber = "****5678",
    AccountName = "Jane A Doe",
    Address = new AustralianAddress
    {
        Lines = ["A204/11-17 Eve Street"],
        Suburb = "Erskineville",
        StateOrTerritory = "NSW",
        Postcode = "2043",
    },
});
EncryptedPii encrypted = VerifiablCrypto.EncryptPii(pii, key);

var nonPii = PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
{
    // PeriodStart may be omitted when the payslip prints only a period end.
    PeriodEnd = "2026-05-31",
    PaymentDate = "2026-06-04",
    Currency = PayslipCurrencies.Aud,
    Gross = new PayslipNumber(8125.00m, "$8,125.00"),
    Paygw = new PayslipNumber(2030.00m, "$2,030.00"),
    Net = new PayslipNumber(6095.00m, "$6,095.00"),
});

RegisterNonPiiResponse registration = await client.RegisterNonPiiAsync(new RegisterNonPiiRequest
{
    Schema = PayslipSchemas.AustralianV2,
    IssuedAt = DateTimeOffset.UtcNow,
    PayslipNonPii = nonPii,
    EncryptionMetadata = encrypted.Metadata,
});

BarcodeSvgResult badge = VerifiablBarcode.CreateSvg(
    new BarcodeParts(registration.VerifiablReference, encrypted.Ciphertext),
    new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });
```

For `nz.payslip.v2`, use `Pii.FormatNewZealand` with
`NewZealandPiiFields` and `NewZealandAddress`, then register
`PayslipSchemas.NewZealandV2`. NZ2 carries the printed employee IRD number,
employer name, account number and account name. It has no BSB or NZBN field.

Both formatters always write eight positions, including empty trailing
positions. AU addresses render as address lines followed by
`suburb state-or-territory postcode`; NZ addresses render as address lines,
optional suburb, then `city postcode`. Commas separate those rendered lines.
Country is implicit in AU2 or NZ2. Values preserve provider formatting and use
the current PII character restrictions. The complete UTF-8 plaintext, including
the discriminator and delimiters, is limited to 1024 bytes.

`Schema` selects only the non-PII payload contract. Choose the PII formatter
separately: `Pii.FormatAustralian` (AU2) for Australian records or
`Pii.FormatNewZealand` (NZ2) for New Zealand records. Today the examples use
AU2 with `au.payslip.v2` and NZ2 with `nz.payslip.v2`, but those matching `2`
suffixes are not a version-coupling rule. A future non-PII schema can still use
the same jurisdictional PII format, or the PII format can evolve without
renaming the non-PII schema. The verifier checks the PII marker against the
record's jurisdiction, not the schema version; a jurisdiction mismatch fails
verification. Legacy v1 verification returns this plaintext without parsing it.

`PayslipNumber` carries the v2 number object. Construct it from a `decimal` when
the provider performs arithmetic, or from an exact string when its scale must
be retained byte-for-byte. `display` is optional and should contain the printed
form only when it differs from `value`.

Currency is optional. When present, use one of the ten `PayslipCurrencies`
constants: AUD, NZD, USD, GBP, EUR, CAD, SGD, HKD, CHF or ZAR.

### Legacy P2 compatibility format

`Pii.Format(PiiFields)` writes P2 plaintext for existing integrations. P2 is exactly
`P2|employeeName|position|department|employerAbn|bsb|accountNumber|accountName|address`.
P2 preserves valid Unicode without normalization. Writers limit the complete plaintext, including
framing and delimiters, to 1024 UTF-8 bytes. Readers continue to accept oversized P2 plaintext from
legacy documents. The pipe, malformed Unicode, and Unicode General Categories Cc (control), Cf
(format), Zl (line separator), and Zp (paragraph separator) are rejected before encryption. Ordinary
international Unicode remains valid. A v2 QR uses uppercase, unpadded RFC 4648 Base32 and the
short scan host with `#2.<BASE32>`, with an explicit byte/alphanumeric segment split; its XMP copy
is the matching `2|reference|BASE32` returned by
`VerifiablBarcode.BuildPayload(parts)`.

Ciphertext, IV, and authentication tags are binary values and the SDK exposes
all three as `byte[]`. You can persist them directly in binary database columns.
The SDK performs encoding only at an external boundary: base64url for issuer API
requests and Base32 for v2 barcode and XMP output.

### Development

Install the .NET 8 and .NET 10 SDKs, matching the toolchains and targeting packs exercised by public
CI, then restore, build, and test with the standard .NET CLI:

```bash
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

The committed `global.json` keeps builds on the latest installed .NET 10 feature band, while the
`packages.lock.json` files make restores and CI dependency caches deterministic. After an intentional
package update, run `dotnet restore --force-evaluate` and commit the resulting lockfile changes. Linux
and macOS can build all library targets. Windows CI additionally runs the .NET Framework 4.7.2 tests.

### Generated API reference

The public API reference is generated from the projects and their XML documentation with the pinned
[DocFX](https://dotnet.github.io/docfx/) local tool. Generate the deterministic managed-reference
catalogue with:

```bash
node script/api-reference.mjs
```

The command restores the pinned tool and locked NuGet dependency graph, then replaces
`generated/api/dotnet`. The catalogue contains public and protected APIs only; private and internal
implementation details are excluded by DocFX's default API filter. It is checked in so the customer
docs can import an exact SDK revision without running .NET or accessing this repository at build time.
CI runs the non-mutating freshness check:

```bash
node script/api-reference.mjs --check
```

The compiler enforces the mandatory fields: `Schema`, `IssuedAt`, `PayslipNonPii`, and `EncryptionMetadata` are `required`, so an incomplete request will not build.

For AU/NZ v2, use `PayslipNonPii.FromAustralianV2(new AustralianPayslipV2 { ... })` or `FromNewZealandV2(new NewZealandPayslipV2 { ... })`. These types and nested fields are generated from the Node Zod wire shape; they cannot carry arbitrary extra fields. The API validates dates, values, allowed codes, and cross-field rules. Free-form `AdditionalData` is rejected for these two known profiles; for legacy and future schemas it is passed to the API under the exact keys you supply. Values may be strings, booleans, numbers, `null`, nested dictionaries, or sequences of those; anything else throws an `ArgumentException` naming the key. Which keys your schema requires is documented per schema — the `au.payslip.v1` set is shown above.

`VerifiablBarcode.CreateSvg` produces a standalone SVG that scales to any size without losing quality; embed it directly in your PDF pipeline when it supports vector images. If it needs a raster image, use `VerifiablBarcode.CreatePng`: it composites the badge deterministically with no native dependencies, so the same record produces the byte-identical raster in every Verifiabl SDK, and QR module edges stay crisp (rasterising the SVG with a general renderer blurs them and costs scannability). PNG output comes in fixed pixel widths (480, 720, 960 or 1440; the physical print size is set where you place the image in the PDF). See the [docs](https://docs.verifiabl.io/) for both flows.

### Placing the badge

The badge is the navy header and the QR code on a white ground, and the QR code spans the full badge width. Keep a clear light margin of at least a tenth of the badge width on the left, the right and the bottom of the badge. That margin is the QR quiet zone. Scanners need it, and the badge does not carry it itself.

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

## Executable example

[`examples/SelfManagedIssuer`](./examples/SelfManagedIssuer/) is an executable self-managed flow for both AU2 and NZ2. It prepares one payslip per jurisdiction, registers the AU2 record individually and both records in a mixed-schema batch in sandbox live mode, and writes SVG barcodes and matching PDF XMP payloads. By default the example builds against the SDK project in this repository. Repository CI also restores it from a locally packed NuGet package and runs it offline as a package-consumer check; see its README for instructions.

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
