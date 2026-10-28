# Verifiabl.Issuer

The Verifiabl issuer SDK for .NET. It registers payslip records with Verifiabl and makes their QR codes.

## Documentation

- [Issuer SDK guide](https://docs.verifiabl.io/issuer-sdk?lang=dotnet)
- [API reference](https://docs.verifiabl.io/api-reference/sdks/dotnet)

## Requirements

.NET 8 or newer, or .NET Framework 4.7.2 or newer.

## Installation

```sh
dotnet add package Verifiabl.Issuer
```

For `Microsoft.Extensions.DependencyInjection`, also add [Verifiabl.Issuer.Extensions.DependencyInjection](https://www.nuget.org/packages/Verifiabl.Issuer.Extensions.DependencyInjection).

## Usage

Create one client with the client ID and client secret from your onboarding:

```csharp
using Verifiabl;
using Verifiabl.Client;

string clientId = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_ID")!;
string clientSecret = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_SECRET")!;

var client = new VerifiablClient(new VerifiablClientOptions
{
    Environment = VerifiablEnvironment.Sandbox,
    Auth = VerifiablAuth.ClientCredentials(clientId, clientSecret),
});
```

To prepare and register payslips and make their QR codes, follow the [issuer SDK guide](https://docs.verifiabl.io/issuer-sdk?lang=dotnet).

Before the first send, save each prepared `Registration` and its ciphertext together. After a failure or restart, send that saved registration unchanged. It carries its reference, so the API does not register it twice. Preparing the payslip again makes a new reference. An `ApiManagedRegistration` has no reference, so do not resend it after an ambiguous failure. See [Register again later](https://docs.verifiabl.io/issuer-sdk/retries-and-timeouts?lang=dotnet#register-again-later).

## Support

Report issues at [github.com/Verifiabl/verifiabl-dotnet](https://github.com/Verifiabl/verifiabl-dotnet/issues). To build and test the SDK, see [DEVELOPMENT.md](https://github.com/Verifiabl/verifiabl-dotnet/blob/main/DEVELOPMENT.md).

## License

MIT. See [LICENSE](https://github.com/Verifiabl/verifiabl-dotnet/blob/main/LICENSE).
