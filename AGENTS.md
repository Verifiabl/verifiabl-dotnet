# Verifiabl .NET SDK

## Layout

Each NuGet package lives in `packages/<PackageId>/`, with its project in `src/`, its tests in `tests/` and its NuGet README. A release target's changelog is in its main package folder. Examples live in `examples/<role>/`.

## Rules

- Keep offline `Verifiabl` code separate from networked `Verifiabl.Client`. Keep dependency injection optional. Do not add verifier functions to issuer packages.
- Preserve public APIs, strong naming, target frameworks and thread-safe client/token reuse.
- `Verifiabl.Verifier` holds no keys and never decrypts. It sends barcode text to the API unchanged and leaves validation to the API. `VerifiablPayloadText` only recognises payload-shaped text, so integrators with their own extraction need no add-on. It does not depend on `Verifiabl.Issuer`; its transport is a copy of the issuer client's, so keep token and retry behaviour in step.
- Verify a document's metadata payload, and read its QR code only when there is no metadata payload. Do not compare the two or report how they differ.
- Keep the verifier core limited to already-extracted payloads. Optional PDF/image extraction composes with `IVerifierClient.VerifyAsync`; verifier-specific mapping runs after verification in application-owned adapters. A verification shows that the payload matches the issuer's registered record, not that the document's visible contents do.
- `IVerifierClient` keeps one method. Add per-verification inputs as init-only properties on `VerificationRequest`, and add only fields the API accepts.
- `Verifiabl.Verifier.Extraction`, the optional extraction add-on, follows the [extraction package contract](DEVELOPMENT.md#extraction-package-contract), and also:
  - Use only the core's public API: no `InternalsVisibleTo` from the core, and no checks for `VerifierClient`, so fakes and decorators work.
  - Report "no single payload" with the core's `PayloadExtractionException`. Do not add `VerifierException` subtypes or wrap verification failures. Add a `PayloadExtractionFailure` value in the core when no existing one fits.
  - Keep the native Wasmtime dependency and the extraction module in this package, so `Verifiabl.Verifier` stays managed code only.
  - Group each document type's public types in its own namespace, such as `Verifiabl.Verifier.Extraction.Pdf`. Keep payload selection, the limit on concurrent reads and the module's host internal.
  - Take an optional idempotency key on every verify call and pass it to the request, so callers can retry without a second charge.
  - Give extraction its own time budget; never share the client's deadline. Decode and render untrusted files, and read QR codes, only inside the WebAssembly module, never with native or managed decoders in the host process. `verifiabl-extract.wasm` and the tests' `ExtractionFixtures` and `StandIns` are copies of the extraction module's build, fixtures and stand-ins, and `extraction-release.json` records the release they came from; do not edit them.
  - Give results and payload holders a public constructor for fakes and a `ToString()` without payload text.
  - Version with `Verifiabl.Verifier`, with consumer and packed-package checks like the core's. It is in the `dotnet/verifier` release target, so it releases with the core at its version; add a package to the target only with release approval.
- Keep PII out of non-PII fields, logs and errors. Use synthetic fixtures. Never commit live credentials or keys.
- Preserve generated v2 models and decimal-string wire values. The API owns full non-PII value validation. Do not copy another SDK's full validator. Preserve unknown/future schema pass-through.
- Regenerate models, profiles, fixtures and API references from source. Do not edit generated output.
- Keep this exported repository self-contained. Do not depend on private paths or tooling.
- Get approval before publication.

## Checks

Use the SDK in `global.json` and the README's supported runtimes. Run commands from this ecosystem root:

```sh
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --verify-no-changes --no-restore
node script/api-reference.mjs --check
```

For package changes, also run:

```sh
dotnet pack --configuration Release --no-build --output artifacts
node script/packed-package-check.mjs artifacts
```

Report checks not run. Linux tests do not replace Windows/.NET Framework coverage.

## Required reading

Read the relevant documents before edits.

| Task | Read |
| --- | --- |
| API, profiles, wire contracts or errors | [Issuer SDK guide](https://docs.verifiabl.io/issuer-sdk?lang=dotnet) |
| Build, tests or API reference | [Development](DEVELOPMENT.md), [reference generation](DEVELOPMENT.md#generated-api-reference) |
| Dependency injection | [DI README](packages/Verifiabl.Issuer.Extensions.DependencyInjection/README.md) |
| Examples or packed packages | [Example guide](examples/issuer/SelfManagedIssuer/README.md) |
| Fixtures | [Fixture guide](packages/Verifiabl.Issuer/tests/Fixtures/README.md) |
| Signing or packages | [Strong naming](DEVELOPMENT.md#strong-name-signing), local `.github/workflows/` files |
| Wasmtime version | [Wasmtime](DEVELOPMENT.md#wasmtime) |
