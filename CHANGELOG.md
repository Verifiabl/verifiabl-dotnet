# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `VerifiablBarcode.CreatePng`: local branded-badge PNG rendering at pixel
  widths 480/720/960/1440, composited deterministically (pre-rasterised frame +
  exact pixel-aligned QR modules) with no native dependencies. The raster is
  byte-identical to the Node SDK's for the same record, enforced by fixture
  parity tests.

## [0.2.0]

A breaking rework of the public API surface in response to a .NET ergonomics
review, plus a retry-safety fix verified against the Verifiabl API source. The
package had no published consumers, so no migration shim is provided.

### Fixed

- Single-record registration could double-register a payslip: the SDK retried
  `503` on the assumption the request was never processed, but the API can
  emit `503` after the row commits (a database connection lost in the
  commit-acknowledgement window, or a platform `503` around a completed
  request). `RegisterNonPiiAsync` now mints a client-side
  `verifiabl_reference` (or uses `RegisterNonPiiRequest.VerifiablReference`
  when set), which puts the API on its idempotent path — an identical resend
  returns the stored record as a duplicate — and retries broadly like batch.
  `RegisterAndBuildBarcodeAsync`, whose reference is server-minted, now
  retries only `429`, which the API enforces before any processing.

### Added

- Optional `RegisterNonPiiRequest.VerifiablReference` for callers who want to
  pin the registration's idempotency key themselves.
- New package `Verifiabl.Extensions.DependencyInjection`, with
  `IServiceCollection.AddVerifiablClient(...)` overloads that register
  `IVerifiablClient` as a singleton wired to `IHttpClientFactory`. The core
  package stays free of `Microsoft.Extensions.*` dependencies.
- `IVerifiablClient`, the interface to depend on and to substitute in tests.
- `VerifiablException`, the abstract base every API failure now derives from, so
  a single `catch (VerifiablException)` covers the client.
- `VerifiablTimeoutException` (carrying the configured `Timeout`) and
  `VerifiablTransportException` (carrying the network fault as
  `InnerException`).

### Changed

- **Namespaces split in two.** Everything offline stays in `Verifiabl` (`Pii`,
  `VerifiablCrypto`, `VerifiablBarcode`, `VerifiablReference`,
  `EncryptionMetadata`, `VerifiablEndpoints`, ...); everything that talks to the
  API moved to `Verifiabl.Client` (`VerifiablClient`, `VerifiablClientOptions`,
  `VerifiablAuth`, the request/response types, the API exceptions and error
  codes). The assembly and package id are unchanged.
- **Mandatory request properties are now `required` and non-nullable**:
  `Schema`, `IssuedAt`, `PayslipNonPii`, and `EncryptionMetadata` on both
  registration requests, `EncryptedPii` on `RegisterAndBuildBarcodeRequest`,
  `VerifiablReference` on `BatchRecord`, `PeriodStart`/`PeriodEnd` on
  `PayslipNonPii`, and `Iv`/`Tag`/`KeyVersion` on `EncryptionMetadata`. Runtime
  validation is unchanged.
- **`VerifiablClient` is `sealed`** and implements `IVerifiablClient`; its
  methods are no longer `virtual`. Use the interface as the mocking seam.
- **`PayslipNonPii.AdditionalData` is now `IDictionary<string, object?>`**
  instead of `System.Text.Json.Nodes.JsonObject`, so the extension point needs no
  JSON library types. Strings, booleans, numbers, nested dictionaries, and
  sequences are mapped onto the wire body; anything else throws an
  `ArgumentException` naming the key.
- Failures that used to escape as `TimeoutException`, `HttpRequestException`, or
  `FormatException` now throw `VerifiablTimeoutException` and
  `VerifiablTransportException`. `VerifiablApiException` and
  `VerifiablAuthException` keep their shape and now derive from
  `VerifiablException`. Cancelling your own `CancellationToken` still throws
  `OperationCanceledException`, and caller mistakes still throw
  `ArgumentException` before anything is sent.

## [0.1.0] - 2026-07-21

### Added

- Initial release of the Verifiabl .NET SDK.
- `VerifiablClient` for the issuer API: `RegisterNonPiiAsync`, `RegisterAndBuildBarcodeAsync`,
  and `RegisterNonPiiBatchAsync` (up to 1000 records).
- OAuth2 client-credentials auth with token caching, single-flight refresh, and a
  transparent retry on `401`.
- Automatic, idempotency-aware retries with exponential backoff, jitter, and
  `Retry-After` support (`VerifiablClientOptions.MaxRetries`, default 2). Batch
  registration retries on throttling, timeouts, `5xx`, and network faults; the
  single-record endpoints retry only failures that leave the request unprocessed
  (`429`, `503`), so a retry cannot create a duplicate record.
- A `User-Agent` header carrying the SDK version, with no usage telemetry.
- `VerifiablCrypto.EncryptPii` (AES-256-GCM), `Pii.Format`/`Pii.Parse`, and
  `VerifiablReference.Generate`.
- `VerifiablBarcode` payload and scan-URL builders, PDF XMP metadata constants, and
  the branded "Secured by Verifiabl" SVG badge renderer.
- Targets `net472`, `net8.0`, and `net10.0`. Strong-named assembly.
