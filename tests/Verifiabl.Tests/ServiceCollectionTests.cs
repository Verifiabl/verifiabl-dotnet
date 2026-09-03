using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Verifiabl.Client;
using Verifiabl.Extensions.DependencyInjection;
using Xunit;

namespace Verifiabl.Tests;

public class ServiceCollectionTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";

    private static RegisterNonPiiRequest ValidRequest() => new()
    {
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
        EncryptionMetadata = new EncryptionMetadata
        {
            Iv = "AAAAAAAAAAAAAAAA",
            Tag = "AAAAAAAAAAAAAAAAAAAAAA",
        },
    };

    [Fact]
    public void ResolvesTheSameSingletonInstance()
    {
        var services = new ServiceCollection();
        services.AddVerifiablClient(options =>
            options.Auth = VerifiablAuth.ApiKey("static-key"));

        using ServiceProvider provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IVerifiablClient>();
        var second = provider.GetRequiredService<IVerifiablClient>();

        // The token cache lives on the instance, so a second resolve must not rebuild it.
        Assert.Same(first, second);
        Assert.IsType<VerifiablClient>(first);
    }

    [Fact]
    public void ConfiguresOptionsFromOtherRegisteredServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(VerifiablAuth.ApiKey("from-di"));
        services.AddVerifiablClient((provider, options) =>
        {
            options.Auth = provider.GetRequiredService<VerifiablAuth>();
            options.Environment = VerifiablEnvironment.Sandbox;
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IVerifiablClient>());
    }

    [Fact]
    public void InvalidOptionsSurfaceThroughTheOptionsPipeline()
    {
        var services = new ServiceCollection();

        services.AddVerifiablClient(options =>
        {
            options.Auth = VerifiablAuth.ApiKey("static-key");
            options.Timeout = TimeSpan.Zero;
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException optionsError = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<VerifiablClientOptions>>().Value);
        OptionsValidationException clientError = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IVerifiablClient>());

        Assert.Contains(
            optionsError.Failures,
            failure => failure.Contains("Timeout must be positive.", StringComparison.Ordinal));
        Assert.Contains(
            clientError.Failures,
            failure => failure.Contains("Timeout must be positive.", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidTokenUrlSurfacesThroughTheOptionsPipeline()
    {
        var services = new ServiceCollection();

        services.AddVerifiablClient(options =>
            options.Auth = VerifiablAuth.ClientCredentials(
                "client-id",
                "client-secret",
                new Uri("https://example.com/oauth/token")));

        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException optionsError = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<VerifiablClientOptions>>().Value);

        Assert.Contains(
            optionsError.Failures,
            failure => failure.Contains("tokenUrl must use a Verifiabl auth host", StringComparison.Ordinal));
    }

    [Fact]
    public void SingletonUsesTheOptionsMonitorInstanceValidatedAtStartup()
    {
        var services = new ServiceCollection();
        int configureCount = 0;
        using var httpClient = new HttpClient(new FakeHttpHandler());
        services.AddVerifiablClient(options =>
        {
            configureCount++;
            options.Auth = VerifiablAuth.ApiKey("static-key");
            options.HttpClient = httpClient;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        VerifiablClientOptions validatedOptions = provider
            .GetRequiredService<IOptionsMonitor<VerifiablClientOptions>>()
            .CurrentValue;
        provider.GetRequiredService<IVerifiablClient>();

        Assert.Equal(1, configureCount);
        Assert.Same(httpClient, validatedOptions.HttpClient);
    }

    [Fact]
    public async Task ACallerSuppliedHttpClientWinsOverTheFactoryClient()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"verifiabl_reference\":\"{Reference}\"}}")),
        };
        using var httpClient = new HttpClient(handler);
        var services = new ServiceCollection();
        services.AddVerifiablClient(options =>
        {
            options.Auth = VerifiablAuth.ApiKey("static-key");
            options.HttpClient = httpClient;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        IVerifiablClient client = provider.GetRequiredService<IVerifiablClient>();
        RegisterNonPiiResponse response = await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal(Reference, response.VerifiablReference);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void RegistersTheNamedFactoryClientTheSdkSendsOn()
    {
        var services = new ServiceCollection();
        services.AddVerifiablClient(options =>
            options.Auth = VerifiablAuth.ApiKey("static-key"));

        using ServiceProvider provider = services.BuildServiceProvider();
        using HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(VerifiablServiceCollectionExtensions.HttpClientName);

        // The SDK applies its own deadline, so the transport must not impose one.
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

#if NET472
    [Fact]
    public void CallerSuppliedHttpClientDoesNotConfigureNetFrameworkConnectionLease()
    {
        var issuerUri = new Uri("http://localhost:41801");
        ServicePoint issuerServicePoint = ServicePointManager.FindServicePoint(issuerUri);
        issuerServicePoint.ConnectionLeaseTimeout = -1;

        using var httpClient = new HttpClient(new FakeHttpHandler());
        var services = new ServiceCollection();
        services.AddVerifiablClient(options =>
        {
            options.Auth = VerifiablAuth.ApiKey("static-key");
            options.IssuerBaseUrl = issuerUri;
            options.HttpClient = httpClient;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVerifiablClient>();

        Assert.Equal(-1, issuerServicePoint.ConnectionLeaseTimeout);
    }

    [Fact]
    public void FactoryClientConfiguresNetFrameworkConnectionLeaseForIssuerAndTokenOrigins()
    {
        var issuerUri = new Uri("http://localhost:41802");
        var tokenUri = new Uri("http://localhost:41803/oauth/token");
        ServicePoint issuerServicePoint = ServicePointManager.FindServicePoint(issuerUri);
        ServicePoint tokenServicePoint = ServicePointManager.FindServicePoint(tokenUri);
        issuerServicePoint.ConnectionLeaseTimeout = -1;
        tokenServicePoint.ConnectionLeaseTimeout = -1;

        var services = new ServiceCollection();
        services.AddVerifiablClient(options =>
        {
            options.Auth = VerifiablAuth.ClientCredentials("client-id", "client-secret", tokenUri);
            options.IssuerBaseUrl = issuerUri;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVerifiablClient>();

        int expectedLease = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;
        Assert.Equal(expectedLease, issuerServicePoint.ConnectionLeaseTimeout);
        Assert.Equal(expectedLease, tokenServicePoint.ConnectionLeaseTimeout);
    }
#endif

    [Fact]
    public void KeyedRegistrationsDoNotSuppressTheUnkeyedClient()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IVerifiablClient>(
            "tenant",
            (_, _) => throw new InvalidOperationException("Keyed registration should not be resolved."));
        services.AddVerifiablClient(options =>
            options.Auth = VerifiablAuth.ApiKey("static-key"));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<VerifiablClient>(provider.GetRequiredService<IVerifiablClient>());
    }

    [Fact]
    public void FirstRegistrationWinsCompletely()
    {
        var services = new ServiceCollection();
        services.AddVerifiablClient(options =>
        {
            options.Auth = VerifiablAuth.ApiKey("first");
            options.Environment = VerifiablEnvironment.Sandbox;
        });
        services.AddVerifiablClient(options =>
        {
            options.Auth = VerifiablAuth.ApiKey("second");
            options.Environment = VerifiablEnvironment.Production;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        VerifiablClientOptions options = provider.GetRequiredService<IOptions<VerifiablClientOptions>>().Value;

        Assert.Equal(VerifiablEnvironment.Sandbox, options.Environment);
    }

    [Fact]
    public void RejectsNullArguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddVerifiablClient((Action<VerifiablClientOptions>)null!));
        Assert.Throws<ArgumentNullException>(
            () => services.AddVerifiablClient((Action<IServiceProvider, VerifiablClientOptions>)null!));
    }
}
