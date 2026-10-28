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

By default the [issuer example](examples/issuer/SelfManagedIssuer/) and the
[lender verifier example](examples/verifier/LenderVerifier/) build against the SDK projects in this
repository. `script/packed-package-check.mjs` also restores both from locally packed NuGet packages
and runs their offline paths as a package-consumer check; see their READMEs for instructions.

## Verifier consumer contract

The solution includes `packages/Verifiabl.Verifier/consumer-tests`, an assembly
without internal SDK access. It exercises client fakes, public result/error
construction and application-owned lender mapping on the supported runtimes.

By default it references the source project. To test a packed NuGet package,
restore that project with `-p:VerifiablVerifierVersion=<packed-version>`, a
NuGet configuration mapping `Verifiabl.*` exclusively to your artifacts
directory, and an isolated packages directory and lockfile. Run `dotnet test`
with the same version property and `--no-restore`. Restore the solution normally
afterwards to return to source-project tests. Windows CI is still required for
.NET Framework execution; cross-compilation is not runtime qualification.

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
