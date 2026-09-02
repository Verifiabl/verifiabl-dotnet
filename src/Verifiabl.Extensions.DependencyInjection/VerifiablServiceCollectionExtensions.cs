using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Verifiabl;
using Verifiabl.Client;

namespace Verifiabl.Extensions.DependencyInjection;

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

#if NET472
    private static readonly TimeSpan NetFrameworkConnectionLease = TimeSpan.FromMinutes(2);
#endif

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
    /// Options are registered with the Microsoft.Extensions.Options pattern and
    /// ValidateOnStart, so hosts that run startup validation fail before serving
    /// traffic when the configured client options are invalid.
    /// Registration uses TryAdd semantics: once <see cref="IVerifiablClient"/>
    /// is registered, later <c>AddVerifiablClient</c> calls are no-ops.
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

        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(IVerifiablClient)
                && !descriptor.IsKeyedService))
        {
            return services;
        }

        services.AddSingleton<IConfigureOptions<VerifiablClientOptions>>(provider =>
            new ConfigureNamedOptions<VerifiablClientOptions>(
                Options.DefaultName,
                options => configureOptions(provider, options)));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<VerifiablClientOptions>, VerifiablClientOptionsValidator>());

        services
            .AddOptions<VerifiablClientOptions>()
            .ValidateOnStart();

#if NET8_0_OR_GREATER
        // Microsoft's documented pairing for a client held for the process lifetime:
        // stop the factory rotating handlers and let PooledConnectionLifetime recycle
        // connections, so DNS changes are still picked up.
        // https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#avoid-typed-clients-in-singleton-services
        services
            .AddHttpClient(HttpClientName)
            // The SDK applies its own per-call deadline covering retries.
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
#else
        services
            .AddHttpClient(HttpClientName)
            // The SDK applies its own per-call deadline covering retries.
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
#endif

        // Singleton: the OAuth token cache lives on the client instance.
        services.TryAddSingleton<IVerifiablClient>(provider =>
        {
            VerifiablClientOptions options = provider
                .GetRequiredService<IOptions<VerifiablClientOptions>>()
                .Value;

#if NET472
            ConfigureNetFrameworkConnectionLease(options);
#endif

            if (options.HttpClient is not null)
            {
                return new VerifiablClient(options);
            }

            return new VerifiablClient(CloneWithHttpClient(
                options,
                provider
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(HttpClientName)));
        });

        return services;
    }

    private static VerifiablClientOptions CloneWithHttpClient(
        VerifiablClientOptions options,
        HttpClient httpClient) => new()
        {
            Auth = options.Auth,
            Environment = options.Environment,
            IssuerBaseUrl = options.IssuerBaseUrl,
            Timeout = options.Timeout,
            MaxRetries = options.MaxRetries,
            HttpClient = httpClient,
            OnRequest = options.OnRequest,
            OnResponse = options.OnResponse,
            OnError = options.OnError,
        };

    private sealed class VerifiablClientOptionsValidator : IValidateOptions<VerifiablClientOptions>
    {
        public ValidateOptionsResult Validate(string? name, VerifiablClientOptions options)
        {
            if (name is not null && name != Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            try
            {
                VerifiablClient.ValidateOptions(options);
            }
            catch (ArgumentException exception)
            {
                return ValidateOptionsResult.Fail(exception.Message);
            }

            return ValidateOptionsResult.Success;
        }
    }

#if NET472
    private static void ConfigureNetFrameworkConnectionLease(VerifiablClientOptions options)
    {
        Uri issuerBaseUri = ResolveIssuerBaseUri(options);
        ServicePoint servicePoint = ServicePointManager.FindServicePoint(issuerBaseUri);
        servicePoint.ConnectionLeaseTimeout = (int)NetFrameworkConnectionLease.TotalMilliseconds;
    }

    private static Uri ResolveIssuerBaseUri(VerifiablClientOptions options)
    {
        if (options.IssuerBaseUrl is not null)
        {
            return new Uri(options.IssuerBaseUrl.GetLeftPart(UriPartial.Authority));
        }

        return options.Environment == VerifiablEnvironment.Sandbox
            ? new Uri(VerifiablEndpoints.SandboxIssuerBaseUrl)
            : new Uri(VerifiablEndpoints.ProductionIssuerBaseUrl);
    }
#endif
}
