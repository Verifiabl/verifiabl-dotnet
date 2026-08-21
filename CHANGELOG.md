# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.0]

The package ids change, and the PII wire format gains an address field.

To upgrade, replace the package reference. Assembly names, namespaces, and types
are unchanged, so no source in a consuming project changes. Replace the
reference, do not add the new package next to the old one: the two packages
contain the same assembly names, so a project that references both gets two
copies of `Verifiabl.dll` and the build fails or resolves to an unexpected
version. The old `Verifiabl` and `Verifiabl.Extensions.DependencyInjection`
packages stay on nuget.org, with 0.2.0 as their last release. 0.3.0 is tagged
in this repository but was never published to nuget.org, so nothing in the wild
uses those package ids beyond 0.2.0.

### Changed

- Package ids are now `Verifiabl.Issuer` and
  `Verifiabl.Issuer.Extensions.DependencyInjection`. The name states which side
  of Verifiabl the package serves, matches the npm SDK's `@verifiabl/issuer`,
  and keeps the plain `Verifiabl` name free for a future package.
- `Pii.Format` emits the `P2` wire format, which appends an address field to the
  seven `P1` fields. `Pii.Parse` reads `P2` and `P1`. Documents issued before
  `P2` carry `P1` and cannot be reissued, so `P1` stays supported.
- `Pii.FieldOrder` has eight entries, because it gives the field order of the
  format that `Pii.Format` writes. Code that uses its length to read a `P1`
  string must use seven instead, or let `Pii.Parse` select the layout.
- Field values must not contain U+2028 or U+2029. These two characters are
  separators, not control characters, so the previous check let them through
  although they break a field in the same way as a newline. A value that
  contains one now throws, as a value with a newline always did.

### Added

- `PiiFields.Address`, an optional single-line address. It obeys the same rules
  as the other fields: no pipe character, no control characters, and a limit of
  256 characters.

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
