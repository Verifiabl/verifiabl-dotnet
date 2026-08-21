# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Package ids renamed**: `Verifiabl` is now `Verifiabl.Issuer`, and
  `Verifiabl.Extensions.DependencyInjection` is now
  `Verifiabl.Issuer.Extensions.DependencyInjection`. Issuer is the role this
  SDK serves, matching the Node SDK's `@verifiabl/issuer`; the .NET spelling
  follows NuGet's PascalCase dotted convention. The old ids stay on nuget.org
  through 0.4.0 and receive no further releases; they are to be deprecated on
  nuget.org in favour of the new ids. Assembly names and namespaces are
  unchanged: code still uses `using Verifiabl;` and `using Verifiabl.Client;`.

### Added

- V2 barcode and XMP writers are now the default, with canonical uppercase
  unpadded RFC 4648 Base32, short-host scan URLs, and explicit QR byte/alphanumeric
  segments. Select `BarcodePayloadFormat.V1` explicitly only for rollback.
- `Pii.Format` now emits P2 by default and `PiiFields.Address` carries the optional
  unstructured address with a 320-byte UTF-8 ceiling. `Pii.FormatV1` retains the
  permanent legacy writer. P2 rejects delimiters, controls, Unicode format
  characters, and malformed UTF-16 before encryption.
- QR version reporting and cross-SDK v2 raster parity fixtures.
- A synthetic scanner pack generator for screen, print, fold, photocopy, camera,
  and hardware-scanner testing.
- A pinned Nix development shell with .NET 8 and .NET 10 tooling.
- Embedded package icon, so the NuGet listing carries the Verifiabl mark.
- `VerifiablIvReuseException`, thrown when a single registration is rejected
  because the record's encryption IV is already registered to your issuer. It
  derives from `VerifiablApiException`, so existing handling still catches it,
  and its message carries the remedy: encrypt again for a fresh IV, resend, and
  rebuild any barcode already rendered from the previous ciphertext. The SDK
  does not re-encrypt and retry for you, because `VerifiablCrypto.EncryptPii`
  already draws a fresh IV on every call, so a collision means encryption
  metadata was replayed and hiding that would mask a broken integration.
- `BatchRecordResult.IsIvReused`, matching the batch error result for the same
  rejection. It covers both cases the API reports, a collision with a stored
  record and a repeat within the same batch, which differ only in `Detail`.
- `VerifiablErrorCodes.IvReused` and `VerifiablErrorCodes.Conflict`. `CONFLICT`
  was already returned for a Verifiabl reference registered with different data
  but was missing from the list.

## [0.3.0]

A breaking change to the encryption helper: the key version is retired.

Upgrading is optional and can wait. The API still accepts `key_version` from
0.1.0 and 0.2.0 and discards it, so an existing integration keeps registering
successfully and only meets the new signature when it chooses to upgrade. No
migration shim is provided; recompile against the two-argument call instead.

### Removed

- `EncryptionMetadata.KeyVersion` and the `keyVersion` parameter of
  `VerifiablCrypto.EncryptPii`, which is now `EncryptPii(plaintext, key)`.
  Verifiabl resolves the decryption key at verification time by testing the
  provider's active keys against the GCM authentication tag, so the value this
  SDK collected, validated, and sent was discarded server-side.
  `KEY_VERSION_UNAVAILABLE` remains in the error codes: the verification API
  still returns it.

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
