# Self-managed issuer example (.NET)

This executable example prepares one fictional AU2 payslip and one fictional NZ2 payslip, exercises single (AU2) and mixed-schema batch registration in sandbox live mode, and generates matching self-managed SVG barcodes and PDF XMP payloads. Each record pairs its jurisdictional PII formatter with the corresponding typed v2 non-PII payload (`AustralianPayslipV2` or `NewZealandPayslipV2`). The NZ example uses `paye`, not the AU `paygw` field.

## Run offline

Requires .NET 8 or later. Run this command from the example directory in the SDK checkout. Offline mode uses an ephemeral in-memory encryption key, makes no network requests, and writes example artifacts under `output/`:

```sh
dotnet run -- offline
```

## Run against the sandbox

Export the sandbox credentials and provider encryption key issued during onboarding. The encryption key must be a canonical Base64-encoded 32-byte key.

```sh
export VERIFIABL_CLIENT_ID='your-sandbox-client-id'
export VERIFIABL_CLIENT_SECRET='your-sandbox-client-secret'
export VERIFIABL_ENCRYPTION_KEY_BASE64='your-base64-encoded-provider-key'
dotnet run -- live
```

Each run uses a unique output directory. It writes SVG badges, matching v2 XMP payloads, registration manifests, and batch outcomes. Each manifest contains the full snake_case non-PII wire fields and the fixed request fields needed to retry an ambiguous registration result with the same Verifiabl reference. The IV and authentication tag use Base64 in the manifest. Plaintext employee PII and the provider encryption key must remain inside the issuer's trusted infrastructure.

## Documentation snippets

Named `snippet:start` and `snippet:end` regions in `Program.cs` are the source of the .NET examples displayed in the customer documentation. The checked-in `generated/snippets.json` catalogue is managed as part of the SDK release process. Do not edit it directly. To propose a snippet change, update the executable region in a contribution; the SDK maintainers will regenerate and validate the catalogue before release.

## Package source

By default this example references the SDK project in the same repository. To check the package-consumer path instead, pack the SDK from `ecosystems/dotnet` with `dotnet pack --configuration Release --output artifacts`, then run `node script/packed-package-check.mjs artifacts`. This check sets `VerifiablIssuerVersion` to the locally built package version, restores the example from the packed NuGet package, and runs it offline. CI runs the same check.
