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
| `Verifiabl` | `V2Issuance`, `PreparedV2Payslip`, AU/NZ PII and address models, `Pii`, `VerifiablCrypto`, `VerifiablBarcode`, `BarcodeParts`, `VerifiablReference` and rendering options | None. Preparation and rendering run locally. |
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

For new AU/NZ v2 integrations, prepare the payslip once, register its non-PII fields, then build the barcode locally. You need your OAuth client ID, client secret, and encryption key from onboarding. Use the same prepared result for the request and barcode.

```csharp
using Verifiabl;
using Verifiabl.Client;

// Your 32-byte key, from onboarding. Load it from a secrets manager.
byte[] key = Convert.FromBase64String(
    Environment.GetEnvironmentVariable("VERIFIABL_ENCRYPTION_KEY_BASE64")!);

// 1. Select AU2 and au.payslip.v2, validate non-PII fields, and encrypt locally.
PreparedV2Payslip prepared = V2Issuance.PrepareAustralian(
    pii: new AustralianPiiFields
    {
        EmployeeName = "Jane A. Doe", EmployerName = "Example Payroll Pty Ltd",
        EmployerAbn = "12 345 678 901",
    },
    payslip: new AustralianPayslipV2
    {
        PeriodEnd = new DateOnly(2026, 5, 31), PaymentDate = new DateOnly(2026, 6, 4),
        Currency = PayslipCurrencies.Aud, PayFrequency = AustralianPayFrequencies.Monthly,
        Gross = 9000.00m, Paygw = 2250.00m, Net = 6750.00m,
        Earnings = [AustralianPayslipV2EarningsItem.Ordinary(9000.00m)],
    },
    issuedAt: DateTimeOffset.UtcNow, key: key);

// 2. Persist the registration and ciphertext together before sending.
// Registration has the reference, IV and tag, but not the ciphertext.
RegisterNonPiiRequest savedRegistration = prepared.Registration;
byte[] savedCiphertext = prepared.BarcodeParts(prepared.VerifiablReference).EncryptedPii;
// Persist savedRegistration and savedCiphertext atomically as binary data.
// After a restart, resend savedRegistration unchanged and render from savedCiphertext.
RegisterNonPiiResponse registration = await client.RegisterNonPiiAsync(savedRegistration);

// 3. Render from the saved ciphertext and the returned reference.
BarcodeSvgResult badge = VerifiablBarcode.CreateSvg(
    new BarcodeParts(registration.VerifiablReference, savedCiphertext),
    new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });
```

### AU2 and NZ2 payslip profiles

Use `V2Issuance.PrepareAustralian` as shown above or `V2Issuance.PrepareNewZealand`
for NZ. Each selects the matching schema and PII formatter internally. The AU2
formatter accepts employer name and ABN separately, then writes the ABN when
present or falls back to the name. For an API-rendered PNG, send
`prepared.ApiManagedRegistration` to `RegisterAndBuildBarcodeAsync` instead of
calling `RegisterNonPiiAsync`. Choose one flow per payslip. The API-managed
request omits the prepared self-managed reference; it cannot safely replay an
ambiguous failure.

```csharp
PreparedV2Payslip nzPrepared = V2Issuance.PrepareNewZealand(
    pii: new NewZealandPiiFields { EmployeeName = "Zoë Nguyễn", IrdNumber = "***-***-***" },
    payslip: new NewZealandPayslipV2
    {
        PeriodEnd = new DateOnly(2026, 5, 31), PaymentDate = new DateOnly(2026, 6, 4),
        Currency = PayslipCurrencies.Nzd,
        Gross = 7600.00m, Paye = 1710.00m, Net = 5890.00m,
    },
    issuedAt: DateTimeOffset.UtcNow, key: key);
// Alternative API-managed flow: the API returns a PNG and its own reference.
RegisterAndBuildBarcodeResponse nzResult =
    await client.RegisterAndBuildBarcodeAsync(nzPrepared.ApiManagedRegistration);
```

### Dates, codes and earnings lines

On .NET 8 and later, `PeriodStart`, `PeriodEnd` and `PaymentDate` are
`DateOnly`. The SDK sends them as `YYYY-MM-DD`. On .NET Framework 4.7.2 they are
`YYYY-MM-DD` strings, because .NET Framework has no `DateOnly` type.

Each field with a fixed set of values has a constants class, for example
`AustralianPayFrequencies`, `AustralianPaidLeaveTypes` and
`NewZealandLeaveBalanceUnits`. Each class has an `All` list. The properties stay
`string`, so you can send a code that the API accepts before you upgrade the
SDK. Printed-text fields such as `Award` and the NZ `TaxCode` have no constants.

Use the earnings line factories to set only the fields of one line type:

```csharp
Earnings =
[
    AustralianPayslipV2EarningsItem.Ordinary(8200.40m, units: 152m, rate: 53.95m),
    AustralianPayslipV2EarningsItem.PaidLeave(AustralianPaidLeaveTypes.PaidParental, 600.00m),
    AustralianPayslipV2EarningsItem.Allowance(AustralianAllowanceTypes.Tools, 20.00m),
    AustralianPayslipV2EarningsItem.OtherAllowance(AustralianOtherAllowanceCategories.HomeOffice, 200.00m),
],
```

An allowance of type `other` needs a category, so use `OtherAllowance` for it.
`Allowance` rejects `AustralianAllowanceTypes.Other`.

On .NET 8 and later, the SDK rejects a date left at `default(DateOnly)`
before it sends the record, because that value is the year 0001.

NZ2 carries the printed employee IRD number,
employer name, account number and account name. It has no BSB or NZBN field.

Both formatters always write eight positions, including empty trailing
positions. AU addresses render as address lines followed by
`suburb state-or-territory postcode`; NZ addresses render as address lines,
optional suburb, then `city postcode`. Commas separate those rendered lines.
Country is implicit in AU2 or NZ2. Values preserve provider formatting and use
the current PII character restrictions. The complete UTF-8 plaintext, including
the discriminator and delimiters, is limited to 1024 bytes.

The preparation helpers pair the AU2/NZ2 PII format with the matching v2
non-PII schema. Their input does not accept a schema, formatted plaintext, or
ciphertext. They do not check whether input values describe a real payslip or
whether printed non-PII strings contain personal information. Keep employee
PII out of non-PII fields. Advanced integrations can still select the schema
and formatter separately with the low-level APIs. The PII format and non-PII
schema versions are independent; legacy v1 verification remains supported.
The verifier currently interprets AU2 only for `au.payslip.v2` and NZ2 only
for `nz.payslip.v2`. Future non-PII schemas need an explicit verifier reader
mapping before reusing either PII format; an unknown schema falls back to raw
PII text rather than structured fields.

Every AU2 and NZ2 amount, rate and quantity is a `decimal`. The SDK sends it
as a plain decimal JSON string, for example `"1234.56"`, with no rounding and
with the scale of the `decimal` value: `1.50m` is sent as `"1.50"` and `1.5m`
as `"1.5"`. Negative values are valid in every field.

`Currency` is required. Use a current ISO 4217 currency code; fund codes and
codes with no minor unit (for example `XAU` or `XXX`) are not accepted, because
wages are paid in legal tender. The `PayslipCurrencies` constants cover common
codes, and `PayslipCurrencies.All` lists every accepted code. The SDK rejects
any other value before it sends the record.

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

The command restores the pinned tool and locked NuGet dependency graph, then writes the ignored
`generated/api/dotnet` directory. The catalogue contains public and protected APIs only; private and
internal implementation details are excluded by DocFX's default API filter. Only the customer docs
repository commits this catalogue, generated from an exact SDK revision with its pinned DocFX tool.
CI runs a non-mutating generation and public-API validation check:

```bash
node script/api-reference.mjs --check
```

The compiler enforces the mandatory fields: `Schema`, `IssuedAt`, `PayslipNonPii`, and `EncryptionMetadata` are `required`, so an incomplete request will not build.

For low-level AU/NZ v2 registrations, use `PayslipNonPii.FromAustralianV2(new AustralianPayslipV2 { ... })` or `FromNewZealandV2(new NewZealandPayslipV2 { ... })`. The recommended `V2Issuance` helpers do this for you. These types and nested fields are generated from the Node Zod wire shape; they cannot carry arbitrary extra fields. The API validates dates, values, allowed codes, and cross-field rules. Free-form `AdditionalData` is rejected for these two known profiles; for legacy and future schemas it is passed to the API under the exact keys you supply. Values may be strings, booleans, numbers, `null`, nested dictionaries, or sequences of those; anything else throws an `ArgumentException` naming the key. Which keys your schema requires is documented per schema — the `au.payslip.v1` set is shown above.

`VerifiablBarcode.CreateSvg` produces a standalone SVG that scales to any size without losing quality; embed it directly in your PDF pipeline when it supports vector images. If it needs a raster image, use `VerifiablBarcode.CreatePng`: it composites the badge deterministically with no native dependencies, so the same record produces the byte-identical raster in every Verifiabl SDK, and QR module edges stay crisp (rasterising the SVG with a general renderer blurs them and costs scannability). PNG output comes in fixed pixel widths (480, 720, 960 or 1440; the physical print size is set where you place the image in the PDF). See the [docs](https://docs.verifiabl.io/) for both flows.

### Placing the badge

The badge is the navy header and the QR code on a white ground, and the QR code spans the full badge width. Keep a clear light margin of at least a tenth of the badge width on the left, the right and the bottom of the badge. That margin is the QR quiet zone. Scanners need it, and the badge does not carry it itself.

For a short, wide space, set `Layout = BarcodeLayout.Horizontal`. The QR code then spans the full badge height, with a white gap and then a light-tinted "Secured by Verifiabl" frame to its right. Keep the same clear margin, a tenth of the badge height, above, below and to the left of the badge. The gap supplies the margin on the right.

```csharp
var options = new BarcodeSvgOptions { Layout = BarcodeLayout.Horizontal };
BarcodeSvgResult svg = VerifiablBarcode.CreateSvg(parts, options);
BarcodePngResult png = VerifiablBarcode.CreatePng(parts, options);
```

The horizontal badge renders the QR code at the same size as the vertical badge. Its minimum SVG width is 940 (the vertical minimum is 480), and its PNG widths are 940, 1410, 1880 and 2820. Unless you set them, `BarcodeSvgOptions.Width` defaults to the layout's minimum and PNG output defaults to 720 pixels wide for the vertical layout and 1410 for the horizontal layout.

### Retries and idempotency

Failed requests are retried automatically with exponential backoff (`VerifiablClientOptions.MaxRetries`, default 2). The Verifiabl reference is the idempotency key. The v2 preparation helpers create a reference for `RegisterNonPiiAsync` and batch registration. Persist the prepared registration and the ciphertext from `prepared.BarcodeParts(prepared.VerifiablReference).EncryptedPii` together before the first call; the registration includes the reference and encryption metadata but not the ciphertext. Reuse the same request after a process restart and render from the saved ciphertext and the returned reference. Do not prepare and encrypt again for an idempotent replay. The client retries these requests on throttling, timeouts, `5xx`, and network faults. `RegisterAndBuildBarcodeAsync` lets the API assign its own reference and retries only `429`, which is enforced before processing.

## Batch registration

For pay runs, register up to 1000 records in one request with `RegisterNonPiiBatchAsync`. Prepare each AU/NZ v2 record with its jurisdiction's helper first. Results match the input order; one bad record does not fail the whole batch.

```csharp
DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
var prepared = payslips.Select(payslip => payslip.Country == "AU"
    ? V2Issuance.PrepareAustralian(payslip.AustralianPii, payslip.AustralianPayslip, issuedAt, key)
    : V2Issuance.PrepareNewZealand(payslip.NewZealandPii, payslip.NewZealandPayslip, issuedAt, key)
).ToList();
// Persist each prepared reference and request before sending.
RegisterNonPiiBatchResponse batch = await client.RegisterNonPiiBatchAsync(
    prepared.Select((item, index) => new BatchRecord
    {
        VerifiablReference = item.VerifiablReference,
        Schema = item.Registration.Schema,
        IssuedAt = item.Registration.IssuedAt,
        PayslipNonPii = item.Registration.PayslipNonPii,
        EncryptionMetadata = item.Registration.EncryptionMetadata,
        ExternalId = payslips[index].ExternalId,
    }).ToList());

for (int i = 0; i < batch.Results.Count; i++)
{
    BatchRecordResult result = batch.Results[i];
    if (result.Status == BatchRecordStatuses.Created || result.Status == BatchRecordStatuses.Duplicate)
    {
        BarcodeParts parts = prepared[i].BarcodeParts(result.VerifiablReference);
        // Render this record's barcode from parts.
    }
    else
    {
        // Handle result.Code; do not parse result.Detail.
    }
}
```

## Executable example

[`examples/SelfManagedIssuer/PreparedV2Example.cs`](./examples/SelfManagedIssuer/PreparedV2Example.cs) shows both prepared v2 flows. The [full executable example](./examples/SelfManagedIssuer/) also demonstrates advanced manual formatting and encryption. It prepares one payslip per jurisdiction, registers the AU2 record individually and both records in a mixed-schema batch in sandbox live mode, and writes SVG barcodes and matching PDF XMP payloads. By default the example builds against the SDK project in this repository. Repository CI also restores it from a locally packed NuGet package and runs it offline as a package-consumer check; see its README for instructions.

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

### Strong-name signing

[`verifiabl.snk`](./verifiabl.snk) intentionally contains an RSA **private key**, not just a public
key. It is publicly available and used exclusively for .NET strong-name signing. This gives the SDK
assemblies a stable identity and supports .NET Framework consumers that require strong-named
dependencies. Committing the key also lets contributors build modified assemblies with the same
identity without rebuilding every dependent library.

This follows Microsoft's [.NET strong-name guidance](https://github.com/dotnet/runtime/blob/main/docs/project/strong-name-signing.md),
which recommends checking in the strong-name private key for open-source libraries to enable drop-in
replacements. Microsoft's [strong-named assembly documentation](https://learn.microsoft.com/en-us/dotnet/standard/assembly/strong-named)
explicitly warns against relying on strong names for security.

Anyone can use this key to sign an assembly with the same strong-name identity. A matching signature
or public-key token is **not proof that Verifiabl published an assembly**, and must not be used to
authorize code or decide whether it is safe to load. NuGet publishing is authenticated separately
using GitHub Actions OIDC; this key grants no package-publishing permissions.

This deliberately public key must never be reused for encryption, authentication, NuGet package
signing, or any other security-sensitive purpose. Unlike this assembly-identity key, your PII
encryption keys, OAuth secrets, and other credentials must remain private.

## Documentation

Full API reference, the alternative API flow, barcode placement rules, and the security model are at [docs.verifiabl.io](https://docs.verifiabl.io/).

## License

[MIT](./LICENSE)
