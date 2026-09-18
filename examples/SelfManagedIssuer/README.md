# Self-managed issuer example (.NET)

This executable example prepares two fictional payslips, exercises single and batch registration in sandbox live mode, and generates matching self-managed SVG barcodes and PDF XMP payloads. It follows the same flow as the Node example.

## Run offline

Requires .NET 8 or later. Offline mode uses an ephemeral in-memory encryption key, makes no network requests, and writes example artifacts under `output/`:

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

Each run uses a unique output directory. It writes SVG badges, matching v2 XMP payloads, registration manifests, and batch outcomes. Each manifest contains the fixed request fields needed to retry an ambiguous registration result with the same Verifiabl reference. The IV and authentication tag use Base64 in the manifest. Plaintext employee PII and the provider encryption key must remain inside the issuer's trusted infrastructure.

## Documentation snippets

Named `snippet:start` and `snippet:end` regions in `Program.cs` are the source of the .NET examples displayed in the customer documentation. The checked-in `generated/snippets.json` catalogue is managed as part of the SDK release process. Do not edit it directly. To propose a snippet change, update the executable region in a contribution; the SDK maintainers will regenerate and validate the catalogue before release.

The project targets `Verifiabl.Issuer` 0.9.0 and deliberately has no `ProjectReference`. Repository CI restores it from the package built earlier in the job, compiles it, and runs it offline, so missing package files or public APIs fail as they would for a customer without making a sandbox request.
