# Development

Install the .NET 8 and .NET 10 SDKs, matching the toolchains and targeting packs exercised by public
CI, then restore, build, and test with the standard .NET CLI:

```sh
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

The committed `global.json` keeps builds on the latest installed .NET 10 feature band, while the
`packages.lock.json` files make restores and CI dependency caches deterministic. After an intentional
package update, run `dotnet restore --force-evaluate` and commit the resulting lockfile changes. Linux
and macOS can build all library targets. Windows CI additionally runs the .NET Framework 4.7.2 tests.

The .NET Framework tests only run on Windows, but any OS can compile them. Build them before pushing
to catch APIs that .NET Framework lacks:

```sh
dotnet build Verifiabl.slnx --configuration Release --framework net472 --artifacts-path artifacts/net472 -p:BuildNetFrameworkTests=true -p:RestoreLockedMode=true
```

The separate artifacts path keeps this build from replacing the normal restore output. With
`BuildNetFrameworkTests`, test projects use `packages.windows.lock.json`.

By default the [issuer example](examples/issuer/SelfManagedIssuer/), the
[payload verifier example](examples/verifier/PayloadVerifier/) and the
[document verifier example](examples/verifier/DocumentVerifier/) build against the SDK projects in
this repository. `script/packed-package-check.mjs` also restores them from locally packed NuGet
packages and runs their offline paths as a package-consumer check. The document example reads back a
PDF carrying the issuer example's payload in its metadata. See each README for instructions.

## Verifier consumer contract

The solution includes `packages/Verifiabl.Verifier/consumer-tests`, an assembly
without internal SDK access. It exercises client fakes, public result/error
construction and application-owned mapping on the supported runtimes.

By default it references the source project. To test a packed NuGet package,
restore that project with `-p:VerifiablVerifierVersion=<packed-version>`, a
NuGet configuration mapping `Verifiabl.*` exclusively to your artifacts
directory, and an isolated packages directory and lockfile. Run `dotnet test`
with the same version property and `--no-restore`. Restore the solution normally
afterwards to return to source-project tests. Windows CI is still required for
.NET Framework execution; cross-compilation is not runtime qualification.

## Extraction package contract

`Verifiabl.Verifier.Extraction` reads a document and gives one payload to `IVerifierClient.VerifyAsync`. It works with any `IVerifierClient`, including fakes and decorators. It obeys these rules:

- **Failures.** A document with no single readable payload throws `PayloadExtractionException`, never `VerifierRejectedException`: only Verifiabl decides that a payload could not be verified. Verification failures pass through without change.
- **Retries.** Each verify call takes an optional idempotency key, so a caller can retry with the `IdempotencyKey` of a failure, not start a new verification that is charged.
- **One verification for each document.** When a document has a payload in its metadata and in a QR code, the package verifies the metadata payload. It reads the QR code only when there is no metadata payload.
- **Results.** Results wrap `VerificationResult` with the payload that the package read.
- **Bounds.** Input size, decoding work and time have limits separate from `VerifierClientOptions.Timeout`. The package obeys cancellation.
- **Isolation.** The package decodes and renders documents only in its WebAssembly sandbox, which has no access to files, the network or the host process.
- **Privacy.** Payload text and document images never occur in messages, logs or `ToString()`.

Verifier-specific mapping runs after extraction and verification, in the application.

## Wasmtime

`Verifiabl.Verifier.Extraction` ships Wasmtime's native libraries from the C API archives of
[Wasmtime's releases](https://github.com/bytecodealliance/wasmtime/releases).
[`Wasmtime.props`](packages/Verifiabl.Verifier.Extraction/src/Wasmtime.props) pins a
[long-term-support](https://docs.wasmtime.dev/stability-release.html) version and each archive's
SHA-256.

A build downloads the library an application or test project needs for its runtime and copies it to
that project's output. `dotnet pack` downloads every platform's, so it needs a `tar` that reads xz:
pack on Linux or macOS.

Dependabot does not report Wasmtime's
[advisories](https://github.com/bytecodealliance/wasmtime/security/advisories). To take a patch
release, set `WasmtimeVersion` and replace the hashes with the release's:

```sh
gh api repos/bytecodealliance/wasmtime/releases/tags/v<version> \
  --jq '.assets[] | select(.name | test("-(x86_64|aarch64)-(windows|linux|macos)-c-api")) | "\(.digest) \(.name)"'
```

Before moving to another major version, compare the headers in the archive's `include/` with
[`WasmtimeNative.cs`](packages/Verifiabl.Verifier.Extraction/src/Internal/Wasmtime/WasmtimeNative.cs),
which calls them unchecked.

## Generated API reference

The public API reference is generated from the projects and their XML documentation with the pinned
[DocFX](https://dotnet.github.io/docfx/) local tool. Generate the deterministic managed-reference
catalogue with:

```sh
node script/api-reference.mjs
```

The command restores the pinned tool and locked NuGet dependency graph, then writes the ignored
`generated/api/dotnet` directory. The catalogue contains public and protected APIs only; private and
internal implementation details are excluded by DocFX's default API filter. Only the customer docs
repository commits this catalogue, generated from an exact SDK revision with its pinned DocFX tool.
CI runs a non-mutating generation and public-API validation check:

```sh
node script/api-reference.mjs --check
```

## Strong-name signing

[`verifiabl.snk`](verifiabl.snk) intentionally contains an RSA **private key**, not just a public
key. It is publicly available and used exclusively for .NET strong-name signing. This gives the SDK
assemblies a stable identity and supports .NET Framework consumers that require strong-named
dependencies. Committing the key also lets contributors build modified assemblies with the same
identity without rebuilding every dependent library.

This follows Microsoft's [.NET strong-name guidance](https://github.com/dotnet/runtime/blob/main/docs/project/strong-name-signing.md),
which recommends checking in the strong-name private key for open-source libraries to enable drop-in
replacements. Microsoft's [strong-named assembly documentation](https://learn.microsoft.com/en-us/dotnet/standard/assembly/strong-named)
explicitly warns against relying on strong names for security.

Anyone can use this key to sign an assembly with the same strong-name identity. A matching signature
or public-key token is **not proof that Verifiabl published an assembly**, and must not be used to
authorize code or decide whether it is safe to load. NuGet publishing is authenticated separately
using GitHub Actions OIDC; this key grants no package-publishing permissions.

This deliberately public key must never be reused for encryption, authentication, NuGet package
signing, or any other security-sensitive purpose. Unlike this assembly-identity key, your PII
encryption keys, OAuth secrets, and other credentials must remain private.
