# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
