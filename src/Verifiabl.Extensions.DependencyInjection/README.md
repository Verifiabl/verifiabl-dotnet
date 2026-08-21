# Verifiabl.Issuer.Extensions.DependencyInjection

Dependency-injection wiring for the [Verifiabl .NET SDK](https://www.nuget.org/packages/Verifiabl.Issuer). Registers `IVerifiablClient` and connects it to `IHttpClientFactory`, so the core `Verifiabl.Issuer` package stays dependency-light.

```bash
dotnet add package Verifiabl.Issuer.Extensions.DependencyInjection
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using Verifiabl;
using Verifiabl.Client;

builder.Services.AddVerifiablClient(options =>
{
    options.Environment = VerifiablEnvironment.Sandbox;
    options.Auth = VerifiablAuth.ClientCredentials(clientId, clientSecret);
});
```

Then inject `IVerifiablClient`:

```csharp
public sealed class PayrollService(IVerifiablClient verifiabl)
{
    public Task<RegisterNonPiiResponse> RegisterAsync(RegisterNonPiiRequest request) =>
        verifiabl.RegisterNonPiiAsync(request);
}
```

Use the `Action<IServiceProvider, VerifiablClientOptions>` overload when the credentials come from another registered service.

The client is registered as a **singleton**: it caches OAuth access tokens, so a scoped or transient lifetime would fetch a fresh token on every resolve. Its `HttpClient` comes from the named factory client `Verifiabl` (`VerifiablServiceCollectionExtensions.HttpClientName`), which you can further configure with `AddHttpClient("Verifiabl")`. Setting `options.HttpClient` yourself takes precedence.

Registration uses `TryAdd` semantics: the **first** `AddVerifiablClient` call wins and later calls are silently ignored, so register the client once at composition-root level rather than from multiple modules.

Full documentation: [docs.verifiabl.io](https://docs.verifiabl.io/).

## License

[MIT](https://github.com/Verifiabl/verifiabl-dotnet/blob/main/LICENSE)
