# Verifiabl .NET SDKs

This repository holds the Verifiabl .NET SDKs. Each package is in `packages/`.

- [`Verifiabl.Issuer`](packages/Verifiabl.Issuer): the issuer SDK, published as [Verifiabl.Issuer](https://www.nuget.org/packages/Verifiabl.Issuer) on NuGet.
- [`Verifiabl.Issuer.Extensions.DependencyInjection`](packages/Verifiabl.Issuer.Extensions.DependencyInjection): registers the issuer client with `Microsoft.Extensions.DependencyInjection`.
- [`Verifiabl.Verifier`](packages/Verifiabl.Verifier): the verifier SDK for lenders, which verifies already-extracted QR or PDF metadata payloads.
- [`examples/issuer`](examples/issuer): runnable issuer examples.
- [`examples/verifier`](examples/verifier): runnable verifier examples.

Documentation is at [docs.verifiabl.io](https://docs.verifiabl.io/issuer-sdk?lang=dotnet). To build and test the packages, see [DEVELOPMENT.md](DEVELOPMENT.md).

## Verifying a release

Verify a package's build provenance with the [GitHub CLI](https://cli.github.com/). NuGet.org
repository-signs packages after upload, so the release workflow attests a link from each signed
package to its attested build. The issuer packages are released together at one version.

```sh
VERSION='<package version>'
PACKAGE=verifiabl.issuer # or verifiabl.issuer.extensions.dependencyinjection, verifiabl.verifier
TAG_PREFIX=issuer # verifier for verifiabl.verifier
LOWER_VERSION="$(printf '%s' "$VERSION" | tr '[:upper:]' '[:lower:]')"
FILE="$PACKAGE.$LOWER_VERSION.nupkg"
curl -fsSLo "$FILE" "https://api.nuget.org/v3-flatcontainer/$PACKAGE/$LOWER_VERSION/$FILE"
gh attestation verify "$FILE" \
  --repo Verifiabl/verifiabl-dotnet \
  --signer-workflow Verifiabl/verifiabl-dotnet/.github/workflows/release.yml \
  --source-ref "refs/tags/$TAG_PREFIX/v$VERSION" \
  --predicate-type https://verifiabl.io/attestations/nuget-repository-signature/v1 \
  --deny-self-hosted-runners
```

To check the build attestation directly, delete NuGet.org's signature, which restores the uploaded
package byte for byte:

```sh
cp "$FILE" unsigned.nupkg
zip -dq unsigned.nupkg .signature.p7s
gh attestation verify unsigned.nupkg \
  --repo Verifiabl/verifiabl-dotnet \
  --signer-workflow Verifiabl/verifiabl-dotnet/.github/workflows/release.yml \
  --source-ref "refs/tags/$TAG_PREFIX/v$VERSION" \
  --deny-self-hosted-runners
```

## License

MIT. See [LICENSE](LICENSE).
