namespace Verifiabl;

/// <summary>
/// How the client authenticates to the Verifiabl API.
/// </summary>
/// <remarks>
/// Deployed environments use OAuth2 client credentials: pass the client ID and
/// secret issued during onboarding via <see cref="ClientCredentials"/> and the
/// client fetches, caches, and refreshes access tokens automatically. The static
/// <see cref="ApiKey"/> form sends a fixed bearer token and exists for local
/// development against a stack that accepts one.
/// </remarks>
public abstract class VerifiablAuth
{
    private protected VerifiablAuth()
    {
    }

    /// <summary>
    /// OAuth2 client credentials issued by Verifiabl during onboarding.
    /// </summary>
    /// <param name="clientId">OAuth client ID.</param>
    /// <param name="clientSecret">
    /// OAuth client secret. Load from a secrets manager; never hard-code it.
    /// </param>
    /// <param name="tokenUrl">
    /// OAuth token endpoint (default: the environment's auth service, e.g.
    /// https://auth.verifiabl.io/oauth/token). Overrides must use a Verifiabl
    /// auth host, or localhost for local development.
    /// </param>
    public static VerifiablAuth ClientCredentials(
        string clientId,
        string clientSecret,
        Uri? tokenUrl = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("clientId and clientSecret are required.");
        }

        return new ClientCredentialsAuth(clientId.Trim(), clientSecret.Trim(), tokenUrl);
    }

    /// <summary>A fixed bearer token, for local development only.</summary>
    public static VerifiablAuth ApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("apiKey must not be empty.", nameof(apiKey));
        }

        return new ApiKeyAuth(apiKey.Trim());
    }

    internal sealed class ClientCredentialsAuth : VerifiablAuth
    {
        internal ClientCredentialsAuth(string clientId, string clientSecret, Uri? tokenUrl)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            TokenUrl = tokenUrl;
        }

        internal string ClientId { get; }

        internal string ClientSecret { get; }

        internal Uri? TokenUrl { get; }
    }

    internal sealed class ApiKeyAuth : VerifiablAuth
    {
        internal ApiKeyAuth(string apiKey)
        {
            Key = apiKey;
        }

        internal string Key { get; }
    }
}
