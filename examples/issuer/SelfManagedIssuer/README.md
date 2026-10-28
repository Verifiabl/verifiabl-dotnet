# Self-managed issuer example (.NET)

This executable example prepares one fictional AU2 payslip and one fictional NZ2 payslip, exercises single (AU2) and mixed-schema batch registration in sandbox live mode, and generates matching self-managed SVG barcodes and PDF XMP payloads. Each record uses its jurisdiction's prepared-v2 helper to validate non-PII fields, format and encrypt PII locally, and keep its reference, registration request, and barcode parts together. The NZ example uses `paye`, not the AU `paygw` field.

## Run offline

Requires .NET 8 or later. Run this command from the example directory in the SDK checkout. Offline mode uses an ephemeral in-memory encryption key, makes no network requests, and writes example artifacts under `output/`:

```sh
dotnet run -- offline
```

## Run against the sandbox

Export the sandbox credentials and issuer encryption key issued during onboarding. The encryption key must be a canonical Base64-encoded 32-byte key.

```sh
export VERIFIABL_CLIENT_ID='your-sandbox-client-id'
export VERIFIABL_CLIENT_SECRET='your-sandbox-client-secret'
export VERIFIABL_ENCRYPTION_KEY_BASE64='your-base64-encoded-issuer-key'
dotnet run -- live
```

Each run uses a unique output directory. It writes SVG badges, matching v2 XMP payloads, registration manifests, and batch outcomes. Each manifest contains the prepared registration's snake_case non-PII fields and fixed request fields needed to retry an ambiguous registration result with the same Verifiabl reference. Reuse the persisted request; do not prepare again. The IV and authentication tag use Base64 in the manifest. Plaintext employee PII and the issuer encryption key must remain inside the issuer's trusted infrastructure.


## Package source

By default this example references the SDK project in the same repository. To check the package-consumer path instead, pack the SDK from `ecosystems/dotnet` with `dotnet pack --configuration Release --output artifacts`, then run `node script/packed-package-check.mjs artifacts`. This check sets `VerifiablIssuerVersion` to the locally built package version, restores the example from the packed NuGet package, and runs it offline. CI runs the same check.
