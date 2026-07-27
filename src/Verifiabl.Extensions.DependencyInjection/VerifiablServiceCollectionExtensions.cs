using System.Net.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Verifiabl.Client;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Verifiabl issuer client with
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class VerifiablServiceCollectionExtensions
{
    /// <summary>
    /// Name of the <see cref="IHttpClientFactory"/> client the SDK sends on.
    /// Use it to attach your own handlers or policies.
    /// </summary>
    public const string HttpClientName = "Verifiabl";

    /// <summary>
    /// Register <see cref="IVerifiablClient"/> as a singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Configures the client options.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IServiceCollection AddVerifiablClient(
        this IServiceCollection services,
        Action<VerifiablClientOptions> configureOptions)
    {
        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        return services.AddVerifiablClient((_, options) => configureOptions(options));
    }

    /// <summary>
    /// Register <see cref="IVerifiablClient"/> as a singleton, configuring it from
    /// other registered services (for credentials pulled from your own
    /// configuration or secrets abstraction).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Configures the client options.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// The registration is a singleton because the client caches OAuth access
    /// tokens; a scoped or transient lifetime would fetch a token per resolve.
    /// Option validation therefore surfaces on the first resolve, not here.
    /// </remarks>
    public static IServiceCollection AddVerifiablClient(
        this IServiceCollection services,
        Action<IServiceProvider, VerifiablClientOptions> configureOptions)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        IHttpClientBuilder builder = services
            .AddHttpClient(HttpClientName)
            // The SDK applies its own per-call deadline covering retries.
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);

#if NET8_0_OR_GREATER
        // Microsoft's documented pairing for a client held for the process lifetime:
        // stop the factory rotating handlers and let PooledConnectionLifetime recycle
        // connections, so DNS changes are still picked up. net472 has no SocketsHttpHandler.
        // https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#avoid-typed-clients-in-singleton-services
        builder
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
#endif

        // Singleton: the OAuth token cache lives on the client instance.
        services.TryAddSingleton<IVerifiablClient>(provider =>
        {
            var options = new VerifiablClientOptions();
            configureOptions(provider, options);
            options.HttpClient ??= provider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);
            return new VerifiablClient(options);
        });

        return services;
    }
}
