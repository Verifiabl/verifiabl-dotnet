# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

The optional extraction add-ons are not published on NuGet yet.

### Added

- `Verifiabl.Verifier.Pdf`, an optional add-on, verifies a payslip PDF's XMP
  metadata payload through `IVerifierClient.VerifyAsync`, and reads the QR code
  in its embedded images only when there is no metadata payload. It uses
  PdfPig and never renders the PDF. Size, page, image and pixel limits and a
  time budget bound work on untrusted PDFs. JPEG images need a decoder from the
  application. It depends on a PdfPig 0.1.17 prerelease, because 0.1.16 ends
  the process on some small crafted PDFs; the stable release will depend on
  stable PdfPig 0.1.17. `VerifyPdfAsync` takes an optional `idempotencyKey`,
  so a PDF verification can be retried with its failure's key.
- `Verifiabl.Verifier.Extraction` holds the managed QR reader that extraction
  add-ons share.
  Both packages version with `Verifiabl.Verifier`.

## [0.1.0-rc.2] - 2026-11-10

Second release candidate of the verifier SDK. It follows the API's verify
error contract and sends an `Idempotency-Key`, so retries are charged once.

### Changed

- `RequestId` on results and exceptions reads only the `X-Request-Id` header,
  which the API sends on every response. After retries it is the last
  attempt's. Token-endpoint failures no longer carry one: that endpoint is not
  a Verifiabl API service.
- **Breaking:** `IVerifierClient.VerifyAsync` takes a `VerificationRequest`, so
  per-verification fields, such as `IdempotencyKey`, do not change the
  interface. `VerifyAsync(string)` is removed: each call made a new request
  with a new key, so retrying it could charge a completed verification again.
  Build a `VerificationRequest` and send the same instance to retry.
- A retry whose `Retry-After` or backoff would outlast `Timeout` is skipped. The
  call fails with the last response and its `RetryAfter` instead of a timeout.
- The User-Agent reports the full package version, including prerelease labels.
- **Breaking:** errors follow the API's verify error contract. `NOT_VERIFIED`
  (`VerifierErrorCodes.NotVerified`) is the only `VerifierRejectedException`,
  including local empty or over-long text. `VALIDATION_FAILED`,
  `PAYLOAD_TOO_LARGE` and `UNSUPPORTED_MEDIA_TYPE` are non-retryable
  `VerifierServiceException`s. `DecryptionFailed`, `VerifiablReferenceNotFound`
  and `KeyVersionUnavailable`, which verification no longer returns, and
  `VerifierRejectedException.FieldErrors` are removed.

### Added

- `VerificationRequest.IdempotencyKey`. Each request gets a new key. Every
  attempt sends it in the `Idempotency-Key` header, including automatic retries
  and a second send of the same request. Verifiabl charges a retried
  verification once. To retry from a new request, set the property to a saved
  key. `VerificationResult.IdempotencyKey` and `VerifierException.IdempotencyKey`
  return the key. Store it with your decision.
- `VerifierErrorCodes.IdempotencyKeyReused`: the key was used for a different
  payslip, or first used more than 1 hour ago. It is a non-retryable
  `VerifierServiceException`.
- `PayloadExtractionException` and `PayloadExtractionFailure`, the failure
  extraction packages throw when no single payload can be read. It derives from
  `VerifierException`, so one catch covers extraction and verification.
- `VerificationResult.RequestId`, so Verifiabl support can find a verification.
- `VerifiablPayloadText` recognises Verifiabl payload text, with no
  third-party dependency, for integrators who extract payloads with their own
  PDF or QR libraries.

## [0.1.0-rc.1] - 2026-10-28

First release candidate of the verifier SDK.

### Added

- `VerifierClient.VerifyAsync` verifies a payslip from its QR code text through
  `POST /v1/verifications/payload`, with OAuth client credentials, token reuse,
  retries and a per-call deadline.
- Structured results preserve unknown schemas and decimal strings. Public
  result and exception constructors support consumer fakes.
- Failures are typed by what to do next. `VerifierRejectedException` reports a
  barcode that failed verification, including empty or over-long text.
  `VerifierAuthException` covers token failures and API 401 or 403 responses,
  and `VerifierServiceException` other Verifiabl errors.
  `VerifierException.IsRetryable` says whether a failed call may succeed later.
- Common request IDs and server Retry-After delays support application recovery.
  Malformed verification responses retain HTTP status and request IDs without
  exposing response bodies.
- Public exception constructors take only defining values; request IDs, retry
  delays, statuses and field errors are init-only properties, so adding
  diagnostics later does not change constructor signatures.
  `VerifierClient.MaxBarcodeLength` is read-only rather than constant.
- Application-owned lender mapping runs after verification. PDF/image
  extraction is reserved for optional follow-up packages.
