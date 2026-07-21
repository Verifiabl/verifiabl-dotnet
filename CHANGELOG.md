# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
