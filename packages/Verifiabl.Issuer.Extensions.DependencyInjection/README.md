# Verifiabl.Issuer.Extensions.DependencyInjection

Registers the [Verifiabl.Issuer](https://www.nuget.org/packages/Verifiabl.Issuer) client with `Microsoft.Extensions.DependencyInjection`.

## Installation

```sh
dotnet add package Verifiabl.Issuer.Extensions.DependencyInjection
```

## Usage

```csharp
using Verifiabl;
using Verifiabl.Client;
using Verifiabl.Extensions.DependencyInjection;

builder.Services.AddVerifiablClient(options =>
{
    options.Environment = VerifiablEnvironment.Sandbox;
    options.Auth = VerifiablAuth.ClientCredentials(clientId, clientSecret);
});
```

Then inject `IVerifiablClient`.

## Documentation

- [Issuer SDK guide](https://docs.verifiabl.io/issuer-sdk?lang=dotnet)
- [API reference](https://docs.verifiabl.io/api-reference/sdks/dotnet/dependency-injection)

## License

MIT. See [LICENSE](https://github.com/Verifiabl/verifiabl-dotnet/blob/main/LICENSE).
