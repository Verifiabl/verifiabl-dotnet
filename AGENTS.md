# Verifiabl .NET SDK

## Rules

- Keep offline `Verifiabl` code separate from networked `Verifiabl.Client`. Keep dependency injection optional. Do not add verifier functions.
- Preserve public APIs, strong naming, target frameworks and thread-safe client/token reuse.
- Keep PII out of non-PII fields, logs and errors. Use synthetic fixtures. Never commit live credentials or keys.
- Preserve generated v2 models and decimal-string wire values. The API owns full non-PII value validation. Do not copy another SDK's full validator. Preserve unknown/future schema pass-through.
- Regenerate models, profiles, fixtures, snippets and API references from source. Do not edit generated output.
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
| API, profiles, wire contracts or errors | [README](README.md) |
| Build, tests or API reference | [Development](README.md#development), [reference generation](README.md#generated-api-reference) |
| Dependency injection | [DI README](src/Verifiabl.Extensions.DependencyInjection/README.md) |
| Examples or packed packages | [Example guide](examples/SelfManagedIssuer/README.md) |
| Fixtures | [Fixture guide](tests/Verifiabl.Tests/Fixtures/README.md) |
| Signing or packages | [Strong naming](README.md#strong-name-signing), local `.github/workflows/` files |
