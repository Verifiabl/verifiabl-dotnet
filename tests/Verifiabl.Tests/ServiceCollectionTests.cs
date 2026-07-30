using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Verifiabl.Client;
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
            KeyVersion = "0f8fad5b-d9cb-469f-a165-70867728950e.1",
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
    public void InvalidOptionsSurfaceOnTheFirstResolve()
    {
        var services = new ServiceCollection();

        // Registration itself never validates: the options delegate only runs on resolve.
        services.AddVerifiablClient(options => options.Timeout = TimeSpan.Zero);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<ArgumentException>(() => provider.GetRequiredService<IVerifiablClient>());
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
        var services = new ServiceCollection();
        services.AddVerifiablClient(options =>
        {
            options.Auth = VerifiablAuth.ApiKey("static-key");
            options.HttpClient = new HttpClient(handler);
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
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(VerifiablServiceCollectionExtensions.HttpClientName);

        // The SDK applies its own deadline, so the transport must not impose one.
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
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
