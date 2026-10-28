# Verifiabl .NET SDK

## Layout

Each NuGet package lives in `packages/<PackageId>/`, with its project in `src/`, its tests in `tests/` and its NuGet README. A release target's changelog is in its main package folder. Examples live in `examples/<role>/`.

## Rules

- Keep offline `Verifiabl` code separate from networked `Verifiabl.Client`. Keep dependency injection optional. Do not add verifier functions to issuer packages.
- Preserve public APIs, strong naming, target frameworks and thread-safe client/token reuse.
- `Verifiabl.Verifier` holds no keys and never decrypts. It sends barcode text to the API unchanged and leaves parsing to the API. It does not depend on `Verifiabl.Issuer`; its transport is a copy of the issuer client's, so keep token and retry behaviour in step.
- Keep the verifier core limited to already-extracted payloads. Optional PDF/image extraction composes with `IVerifierClient.VerifyAsync`; lender-specific mapping runs after verification in application-owned adapters. Verification authenticates payload data, not a document's visible contents.
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
